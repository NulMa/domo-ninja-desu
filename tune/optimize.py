"""외부 최적화 루프 — Optuna ask-and-tell + 축차 반감법 (`17` §3 · §5 · §8).

    tune/.venv/Scripts/python.exe tune/optimize.py --trials 60

★ `sim` 을 프로세스로 호출하므로 ask-and-tell 을 쓴다. `study.optimize(fn)` 은
  콜백 안에서 예외가 나면 trial 상태가 애매해지는데, 밖에서 명시적으로 돌리면
  **어디서 죽었는지가 코드에 보인다.**

★ 예산 축은 **시드**다. 빌드 공간을 자르면 `M3a`·`M3b`·`M3c` 가 무너져 `D` 가 0 이 된다
  (`evaluate.Evaluator` 주석의 실측). 저예산 → 고예산으로 시드를 늘리며
  중간값을 보고하고, ASHA 가 가망 없는 세트를 일찍 끊는다.

★ study.db 는 커밋하지 않는다 (`D-49-b`). 대신 매 실행 끝에
  **시도 횟수 · 목적함수 궤적 · 상위 파라미터 세트**를 JSON 으로 남긴다 —
  텍스트라 diff 가 읽히고, 도구 없이 열리고, `[BAL]` 커밋에서 바로 인용된다.
"""

from __future__ import annotations

import argparse
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import optuna

import space
from evaluate import Evaluator, SimError, dirty, sim_commit

REPORTS = Path(__file__).resolve().parent / "reports"

# 축차 반감법의 예산 사다리. 시드 수다.
# 저예산 순위가 고예산 순위를 대략 예측한다는 가정 위에 선다 (`17` §3) —
# CRN(시드 시작점 고정)이 분산을 줄여 그 가정을 받쳐준다.
SEED_LADDER = [1, 2, 4]


def select_params(morris_path: Path | None, top_k: int) -> list[space.Param]:
    """Morris 결과가 있으면 상위 K 개만 탐색한다.

    ★ 손으로 고르면 그게 곧 편향이다. 없으면 전부 돌린다 —
      **없다는 이유로 몰래 줄이지는 않는다.**
    """
    params = space.all_params()
    if morris_path is None or not morris_path.exists():
        return params

    ranked = json.loads(morris_path.read_text(encoding="utf-8"))["ranked"]
    keep = {row["name"] for row in ranked[:top_k] if row["mu_star"] > 0}
    chosen = [p for p in params if p.name in keep]

    return chosen or params


def make_sampler(name: str, seed: int) -> optuna.samplers.BaseSampler:
    """`17` §5 — 차원 수와 상호작용 강도가 샘플러를 고른다.

    Morris 실측에서 σ 가 μ* 보다 컸다 = 상호작용이 강하고 비선형이다.
    저차원(≤20)·소예산에서 그 조건이면 CMA-ES 가 일관되게 강하다고 보고된다.
    """
    if name == "tpe":
        return optuna.samplers.TPESampler(seed=seed)
    if name == "random":
        return optuna.samplers.RandomSampler(seed=seed)
    return optuna.samplers.CmaEsSampler(seed=seed, warn_independent_sampling=False)


def main() -> int:
    ap = argparse.ArgumentParser(description="밸런스 외부 최적화 루프")
    ap.add_argument("--trials", type=int, default=40)
    ap.add_argument("--sampler", choices=["cmaes", "tpe", "random"], default="cmaes")
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--top-k", type=int, default=12,
                    help="Morris 상위 몇 개를 탐색할지. 0 이면 전부")
    ap.add_argument("--morris", default=str(REPORTS / "morris.json"))
    ap.add_argument("--out", default=None)
    args = ap.parse_args()

    morris_path = Path(args.morris) if args.top_k > 0 else None
    params = select_params(morris_path, args.top_k)
    bounds = space.bounds(params)
    base = space.baseline(params)

    ev = Evaluator()
    baseline_result = ev.run({})

    print(f"탐색 축 {len(params)}개 / 전체 {len(space.all_params())}개 · "
          f"샘플러 {args.sampler} · 시도 {args.trials}")
    print(f"기준선 D = {baseline_result.d:.4f}  위반 {baseline_result.objective.get('violations')}")
    print()

    study = optuna.create_study(
        direction="maximize",
        sampler=make_sampler(args.sampler, args.seed),
        # ASHA. 사다리의 각 단을 rung 으로 본다.
        pruner=optuna.pruners.SuccessiveHalvingPruner(
            min_resource=1, reduction_factor=2, min_early_stopping_rate=0),
    )

    trajectory: list[dict] = []
    errors: list[str] = []

    for n in range(args.trials):
        trial = study.ask()
        values = {
            p.name: trial.suggest_float(p.name, bounds[p.name][0], bounds[p.name][1])
            for p in params
        }
        overrides = space.to_overrides(params, values)

        d = 0.0
        pruned = False
        try:
            for rung, seeds in enumerate(SEED_LADDER):
                d = ev.run(overrides, seeds=seeds).d
                trial.report(d, rung)
                if trial.should_prune():
                    pruned = True
                    break
        except SimError as ex:
            # 검증 규칙(R01~R24)에 걸리는 조합은 "그 값은 못 쓴다" 는 정보다.
            # 0 으로 기록하고 이유를 남긴다 — 조용히 버리면 왜 안 뽑혔는지 알 수 없다.
            errors.append(f"trial {n}: {str(ex).splitlines()[0]}")
            d = 0.0

        if pruned:
            study.tell(trial, state=optuna.trial.TrialState.PRUNED)
        else:
            study.tell(trial, d)

        trajectory.append({
            "trial": n,
            "d": round(d, 6),
            "pruned": pruned,
            "best": round(study.best_value, 6) if study.best_trial else None,
        })

        mark = "가지치기" if pruned else ""
        best = f"{study.best_value:.4f}" if study.best_trial else "—"
        print(f"  [{n + 1:>3}/{args.trials}] D={d:.4f}  최고={best}  {mark}")

    # ─────────────────────────────────────────────────────────
    completed = [t for t in study.trials if t.state == optuna.trial.TrialState.COMPLETE]
    top = sorted(completed, key=lambda t: -(t.value or 0))[:5]

    best_values = dict(study.best_params) if study.best_trial else {}
    best_overrides = space.to_overrides(params, best_values) if best_values else {}
    best_full = ev.run(best_overrides, seeds=5) if best_overrides else baseline_result

    report = {
        "provenance": {
            "generatedAt": datetime.now(timezone.utc).isoformat(),
            "simCommit": sim_commit(),
            "workingTreeDirty": dirty(),
            "sampler": args.sampler,
            "samplerSeed": args.seed,
            "trials": args.trials,
            "seedLadder": SEED_LADDER,
            "simCalls": ev.calls,
            "searchAxes": [p.name for p in params],
            "searchAxisCount": len(params),
            "totalAxisCount": len(space.all_params()),
            "_ladderNote":
                "예산 축은 시드다. 빌드 공간을 자르면 M3a·M3b·M3c 가 무너져 D 가 0 이 된다 "
                "— evaluate.Evaluator 주석의 실측 참조.",
        },
        "baseline": {
            "d": round(baseline_result.d, 6),
            "violations": baseline_result.objective.get("violations"),
            "values": {k: base[k] for k in base},
        },
        "best": {
            "d": round(best_full.d, 6),
            "dAtSearchBudget": round(study.best_value, 6) if study.best_trial else None,
            "violations": best_full.objective.get("violations"),
            "values": {k: (int(round(v)) if next(p for p in params if p.name == k).integer
                           else round(v, 6))
                       for k, v in best_values.items()},
            "overrides": best_overrides,
        },
        "top5": [
            {
                "d": round(t.value or 0, 6),
                "values": {k: round(v, 4) for k, v in t.params.items()},
            }
            for t in top
        ],
        "trajectory": trajectory,
        "errors": errors,
        "counts": {
            "complete": len(completed),
            "pruned": sum(1 for t in study.trials if t.state == optuna.trial.TrialState.PRUNED),
        },
    }

    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    out = Path(args.out) if args.out else REPORTS / f"optuna-{stamp}.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    print()
    print(f"기준선 D = {baseline_result.d:.4f}")
    print(f"최고   D = {best_full.d:.4f}  (시드 5 재평가)")
    print(f"위반: {best_full.objective.get('violations')}")
    print(f"가지치기 {report['counts']['pruned']} / 완주 {report['counts']['complete']} "
          f"· sim 호출 {ev.calls}회")
    print(f"\n→ {out}")

    if best_full.d <= baseline_result.d:
        print("\n⚠️ 기준선을 못 넘었다. 시도 수를 늘리거나 탐색 축을 다시 고른다.")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
