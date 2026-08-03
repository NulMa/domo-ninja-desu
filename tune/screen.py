"""Morris 스크리닝 — 어느 파라미터를 탐색할지 측정으로 고른다 (`17` §4).

    py -m tune.screen  또는  tune/.venv/Scripts/python.exe tune/screen.py --trajectories 8

★ 손으로 고르면 그게 곧 편향이다. 31개를 전부 탐색하면 예산이 증발한다.
  Morris 는 각 파라미터를 한 번에 하나씩 흔들어 기본 효과를 모으고,
  평균 μ* 과 표준편차 σ 를 낸다.

    μ* 큼  → 영향이 크다
    σ  큼  → 다른 파라미터와 상호작용하거나 비선형이다

★ 부수 효과가 본 효과만큼 크다.
  "어떤 지표에도 영향이 없다" 가 나오면 그 파라미터는 게임에서 빼도 된다 —
  `08` §8 범위 축소 우선순위를 감이 아니라 측정으로 정한다.

⚠️ SALib 은 연속 공간을 가정하는데 우리 스탯은 정수다. 반올림 때문에
  작은 흔들림이 같은 값으로 접힐 수 있다 — 그래서 `--levels` 를 4 로 두어
  격자 간격이 반올림보다 크게 만든다. 이 한계는 리포트에 같이 적는다.
"""

from __future__ import annotations

import argparse
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import numpy as np
from SALib.analyze import morris as morris_analyze
from SALib.sample import morris as morris_sample

import space
from evaluate import Evaluator, SimError, dirty, sim_commit

REPORTS = Path(__file__).resolve().parent / "reports"


def run(trajectories: int, levels: int, seeds: int) -> dict:
    params = space.all_params()
    base = space.baseline(params)
    bounds = space.bounds(params)
    names = [p.name for p in params]

    problem = {
        "num_vars": len(names),
        "names": names,
        "bounds": [list(bounds[n]) for n in names],
    }

    # 표본 수 = trajectories × (P + 1). 31개면 궤적 8 에 256회다.
    samples = morris_sample.sample(problem, N=trajectories, num_levels=levels)

    ev = Evaluator(seeds=seeds)
    outputs = np.zeros(len(samples))
    failures: list[str] = []

    print(f"파라미터 {len(names)}개 · 궤적 {trajectories} · 평가 {len(samples)}회 "
          f"(시드 {seeds} × 전수 4,320빌드)")

    for i, row in enumerate(samples):
        values = {n: float(v) for n, v in zip(names, row)}
        try:
            outputs[i] = ev.run(space.to_overrides(params, values)).d
        except SimError as ex:
            # ★ 삼키지 않는다. 다만 한 점이 죽었다고 스크리닝 전체를 버리지도 않는다 —
            #   검증 규칙에 걸리는 조합은 실제로 "그 값은 못 쓴다" 는 정보다.
            #   D=0 으로 기록하고 몇 개가 그랬는지 리포트에 남긴다.
            outputs[i] = 0.0
            failures.append(f"[{i}] {str(ex).splitlines()[0]}")

        if (i + 1) % 25 == 0 or i + 1 == len(samples):
            print(f"  {i + 1}/{len(samples)}  D 평균 {outputs[:i + 1].mean():.4f}")

    result = morris_analyze.analyze(problem, samples, outputs, num_levels=levels)

    ranked = sorted(
        (
            {
                "name": names[k],
                "mu_star": round(float(result["mu_star"][k]), 6),
                "sigma": round(float(result["sigma"][k]), 6),
                "baseline": base[names[k]],
                "bounds": [round(bounds[names[k]][0], 4), round(bounds[names[k]][1], 4)],
            }
            for k in range(len(names))
        ),
        key=lambda r: -r["mu_star"],
    )

    return {
        "provenance": {
            "generatedAt": datetime.now(timezone.utc).isoformat(),
            "simCommit": sim_commit(),
            "workingTreeDirty": dirty(),
            "method": "Morris (SALib)",
            "trajectories": trajectories,
            "levels": levels,
            "evaluations": len(samples),
            "seedsPerEvaluation": seeds,
            "buildLimit": 0,
            "_buildLimitNote":
                "빌드 공간을 자르면 M3a·M3b·M3c 가 무너져 D 가 0 이 된다(실측). "
                "예산 축은 시드다 — 17 §3.",
            "_integerNote":
                "스탯이 정수라 덮어쓰기 시점에 반올림된다. num_levels 를 격자 간격이 "
                "반올림보다 크도록 잡았지만, μ* 가 아주 작은 파라미터는 "
                "'영향이 없다' 와 '격자에 안 잡혔다' 가 구분되지 않는다.",
            "_botPolicyNote":
                "★ 아이템 가격(statBoost·teamBoost·conditionalBoost·healItem)의 μ* 가 0 인 것은 "
                "게임의 성질이 아니라 봇 정책의 결과다 — 봇은 아이템을 사지 않는다(08 §6.1, "
                "빌드 공간이 스킬만). 이 값들을 '영향이 없으니 빼도 된다' 로 읽으면 "
                "실제 플레이에서만 작동하는 축을 지우게 된다. M6 을 뺀 것(D-72)과 같은 계열이다.",
        },
        "failures": failures,
        "outputs": {
            "mean": round(float(outputs.mean()), 6),
            "max": round(float(outputs.max()), 6),
            "zeroCount": int((outputs == 0).sum()),
        },
        "ranked": ranked,
    }


def main() -> int:
    ap = argparse.ArgumentParser(description="Morris 스크리닝")
    ap.add_argument("--trajectories", type=int, default=8)
    ap.add_argument("--levels", type=int, default=4)
    ap.add_argument("--seeds", type=int, default=1)
    ap.add_argument("--out", default=str(REPORTS / "morris.json"))
    args = ap.parse_args()

    report = run(args.trajectories, args.levels, args.seeds)

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    print()
    print("영향 큰 순 (μ*):")
    for row in report["ranked"][:10]:
        print(f"  {row['name']:<26} μ*={row['mu_star']:.4f}  σ={row['sigma']:.4f}")

    dead = [r["name"] for r in report["ranked"] if r["mu_star"] < 1e-9]
    if dead:
        print()
        print(f"어떤 지표에도 영향 없음 {len(dead)}개: {', '.join(dead)}")

    print(f"\n→ {out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
