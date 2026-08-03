"""`sim` 바이너리를 호출해 목적함수 `D` 를 얻는다.

★ 이 층에는 게임 로직이 한 줄도 없다 (`D-49-a`).
  결정론은 C#, 통계는 Python — 언어를 나눈 게 아니라 역할을 나눈 것이다.

★ 프로세스 경계를 파일로 긋는다.
  최적화기가 죽어도 마지막 결과가 디스크에 남고, 심사자가 `sim` 만 따로 돌려 재현할 수 있다.
"""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import tempfile
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


class SimError(RuntimeError):
    """`sim` 이 0 이 아닌 코드로 끝났다.

    ★ 삼키지 않는다. 종료 코드 4 는 "덮어쓰기 경로가 아무 데도 안 맞았다" 인데,
      그걸 조용히 넘기면 최적화기는 값을 바꿨다고 믿고 지표가 안 움직인 것을
      *"그 파라미터는 영향이 없다"* 로 학습한다. Morris 결과가 통째로 거짓말이 된다.
    """


@dataclass
class Result:
    d: float
    """목적함수. 0~1. 하나라도 목표 밖이면 1 미만이다."""

    metrics: dict
    """`M1`~`M7` 원본. 리포트에 그대로 싣는다."""

    objective: dict
    """항별 만족도와 위반 목록."""

    elapsed_ms: float


def find_sim() -> Path:
    """빌드된 `sim` 을 찾는다.

    경로를 상수로 박지 않는다 — 출력이 `artifacts/bin/...` 으로 재배치돼 있고
    (`Directory.Build.props`) Debug/Release 가 갈린다.
    """
    candidates = sorted(
        (ROOT / "artifacts" / "bin" / "DomoNinja.Sim").rglob("DomoNinja.Sim.dll"),
        key=lambda p: (0 if "release" in str(p).lower() else 1, -p.stat().st_mtime),
    )
    if not candidates:
        raise SimError(
            "sim 산출물을 못 찾았다. 먼저 빌드한다:\n"
            "  dotnet build sim/DomoNinja.Sim.csproj -c Release"
        )
    return candidates[0]


class Evaluator:
    """파라미터 값 → `D`.

    ★ `seeds` 를 밖에서 받는다. 축차 반감법(`17` §3)이 같은 평가기를 예산만 바꿔 부른다.
    """

    def __init__(
        self,
        sim: Path | None = None,
        *,
        seeds: int = 3,
        seed_start: int = 1,
        stage: str = "S1",
        meta: str = "meta0",
        build_limit: int = 0,
    ) -> None:
        """
        ⚠️ `build_limit` 을 0(전부) 이외로 두면 안 된다. 실측:

        ```
        seeds=1 builds=200   D = 0.0000     seeds=1 builds=all  D = 0.6371
        seeds=3 builds=200   D = 0.0000     seeds=3 builds=all  D = 0.6369
        ```

        ★ **`M3a`·`M3b`·`M3c` 가 빌드 공간 위에서 정의된 지표**라서다.
        빌드 열거가 체계적이라 앞쪽 N 개는 같은 캐릭터·같은 액티브를 공유한다 —
        잘라내면 *"사장된 선택지"* 가 통계가 아니라 **자르는 위치의 함수**가 되고,
        기하평균이라 그 하나가 전체를 0 으로 만든다. **기울기가 통째로 사라진다.**

        → 축차 반감법(`17` §3)의 예산 축은 **시드**다. 전수 4,320 빌드가
        시드 1개에 0.8초라 예산 문제도 아니다.
        """
        self.sim = sim or find_sim()
        self.seeds = seeds
        self.seed_start = seed_start
        self.stage = stage
        self.meta = meta
        self.build_limit = build_limit
        self.calls = 0

    def params_json(self, overrides: dict, *, seeds: int | None = None,
                    build_limit: int | None = None) -> dict:
        body = {
            "seeds": seeds if seeds is not None else self.seeds,
            # ★ CRN — 예산이 달라져도 시드 시작점을 고정한다 (`17` §1).
            #   세트마다 다른 시드를 주면 차이 = 진짜 효과 + 서로 다른 상점 운이 된다.
            "seedStart": self.seed_start,
            "stage": self.stage,
            "meta": self.meta,
            "buildLimit": build_limit if build_limit is not None else self.build_limit,
            "includeBuilds": False,
        }
        if overrides:
            body["overrides"] = overrides
        return body

    def run(self, overrides: dict, *, seeds: int | None = None,
            build_limit: int | None = None) -> Result:
        self.calls += 1

        work = Path(tempfile.mkdtemp(prefix="domotune-"))
        try:
            params_path = work / "params.json"
            out_path = work / "metrics.json"

            params_path.write_text(
                json.dumps(self.params_json(overrides, seeds=seeds, build_limit=build_limit),
                           ensure_ascii=False, indent=2),
                encoding="utf-8",
            )

            proc = subprocess.run(
                ["dotnet", str(self.sim), "--run", str(params_path), "--out", str(out_path)],
                capture_output=True, text=True, encoding="utf-8", errors="replace",
                cwd=str(ROOT),
            )

            if proc.returncode != 0:
                raise SimError(
                    f"sim 종료 코드 {proc.returncode}\n"
                    f"{(proc.stderr or proc.stdout or '').strip()}"
                )
            if not out_path.exists():
                raise SimError("sim 이 0 으로 끝났는데 결과 파일이 없다")

            report = json.loads(out_path.read_text(encoding="utf-8"))

        finally:
            shutil.rmtree(work, ignore_errors=True)

        objective = report.get("objective") or {}
        if "D" not in objective:
            raise SimError("리포트에 objective.D 가 없다 — sim 이 목적함수를 안 냈다")

        return Result(
            d=float(objective["D"]),
            metrics=report.get("metrics") or {},
            objective=objective,
            elapsed_ms=float((report.get("throughput") or {}).get("elapsedMs") or 0.0),
        )


def sim_commit() -> str:
    """리포트에 박을 커밋 해시 (`D-55`). 어느 코드로 낸 숫자인지가 남아야 재현이 성립한다."""
    try:
        out = subprocess.run(
            ["git", "rev-parse", "--short", "HEAD"],
            capture_output=True, text=True, cwd=str(ROOT), timeout=10,
        )
        return out.stdout.strip() or "unknown"
    except Exception:
        return "unknown"


def dirty() -> bool:
    """작업 트리가 지저분한가. 지저분하면 커밋 해시만으로 재현되지 않는다."""
    try:
        out = subprocess.run(
            ["git", "status", "--porcelain"],
            capture_output=True, text=True, cwd=str(ROOT), timeout=10,
        )
        return bool(out.stdout.strip())
    except Exception:
        return True


__all__ = ["Evaluator", "Result", "SimError", "find_sim", "sim_commit", "dirty", "os"]
