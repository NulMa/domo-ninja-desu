"""탐색 공간 — 어느 값을 얼마나 흔들 것인가.

★ 이 파일이 `/tune` 의 유일한 게임 지식이다.
   나머지 모듈(evaluate·screen·optimize)은 여기서 나온 이름과 범위만 보고 돌아간다.
   장르가 바뀌어도 이 파일만 갈아끼우면 파이프라인이 그대로 산다 — `05` §1.6 이 요구한 형태다.

★ 범위를 "현재값 ± n%" 로 잡는다.
   절대값으로 적으면 `/data` 가 바뀔 때마다 여기도 같이 고쳐야 하고,
   한쪽만 고쳐지면 **최적화기가 이미 버려진 구간을 뒤진다.**

⚠️ 여기 없는 값은 최적화 대상이 아니다. 그게 곧 "무엇을 기계에 맡기지 않았는가" 의 목록이고,
   기술 문서 5장에 그대로 들어간다.
"""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path

DATA = Path(__file__).resolve().parent.parent / "data"


@dataclass(frozen=True)
class Param:
    """탐색 축 하나."""

    name: str
    """사람이 읽는 이름. Morris 리포트와 Optuna study 에 이 이름이 쓰인다."""

    file: str
    """`/data` 안의 파일 이름."""

    path: str
    """JSONPath. `sim` 의 `overrides` 가 그대로 받는다."""

    span: float
    """현재값 대비 흔들 폭. 0.3 이면 ±30%."""

    integer: bool = True
    """정수 스탯인가. hp·attack·간격은 전부 정수다 (`_schema` §7)."""

    lo_abs: float | None = None
    """하한 절대값. 간격이 0 이 되면 매 틱 행동이라 `R19` 가 막는다 — 거기 닿기 전에 자른다."""


# ─────────────────────────────────────────────────────────────
# 캐릭터 스탯 — 6명 × 4축.
#
# range 는 뺐다. 정수 1칸 차이가 전투를 질적으로 바꿔서(붙는가 안 붙는가)
# 연속 탐색축으로 두면 목적함수가 계단이 된다. 사거리는 저작이 정한다.
# ─────────────────────────────────────────────────────────────
CHARACTERS = ["C1", "C2", "C3", "C4", "C5", "C6"]
CHARACTER_STATS = [
    ("hp", 0.30, 20.0),
    ("attack", 0.30, 1.0),
    ("attackInterval", 0.30, 4.0),
    ("moveInterval", 0.30, 2.0),
]


def _character_params() -> list[Param]:
    out: list[Param] = []
    for cid in CHARACTERS:
        for stat, span, lo in CHARACTER_STATS:
            out.append(Param(
                name=f"{cid}.{stat}",
                file="characters.json",
                path=f"$.characters[?(@.id=='{cid}')].{stat}",
                span=span,
                lo_abs=lo,
            ))
    return out


# ─────────────────────────────────────────────────────────────
# 경제 — 재화가 빌드 성립을 정한다.
#
# `M3c`(보조 채택률)가 D+4 에 상한을 넘겼는데, 보조를 두 개 다 살 수 있느냐가
# 곧 채택률이라 가격이 직접 닿는 축이다.
# ─────────────────────────────────────────────────────────────
ECONOMY = [
    Param("price.skillActivate", "economy.json", "$.prices.skillActivate", 0.5, lo_abs=1),
    Param("price.supportSkill.lo", "economy.json", "$.prices.supportSkill[0]", 0.5, lo_abs=1),
    Param("price.supportSkill.hi", "economy.json", "$.prices.supportSkill[1]", 0.5, lo_abs=1),
    Param("price.statBoost", "economy.json", "$.prices.statBoost", 0.5, lo_abs=1),
    Param("price.teamBoost", "economy.json", "$.prices.teamBoost", 0.5, lo_abs=1),
    Param("price.conditionalBoost", "economy.json", "$.prices.conditionalBoost", 0.5, lo_abs=1),
    Param("price.healItem", "economy.json", "$.prices.healItem", 0.5, lo_abs=1),
]


def all_params() -> list[Param]:
    return _character_params() + ECONOMY


# ─────────────────────────────────────────────────────────────


def _load(file: str) -> dict:
    return json.loads((DATA / file).read_text(encoding="utf-8"))


def _lookup(doc: dict, path: str) -> float:
    """탐색 공간이 쓰는 JSONPath 부분집합만 푼다.

    ★ 전체 JSONPath 파서를 가져오지 않는다. 여기서 필요한 형태는 두 가지뿐이고
      (`$.a.b`, `$.a[?(@.id=='X')].b`, `$.a[0]`), 라이브러리를 하나 더 걸면
      **C# 쪽 SelectTokens 와 해석이 갈릴 여지**가 생긴다.
      실제로 값을 덮어쓰는 건 C# 이므로, 여기서는 **기준점을 읽기만** 한다.
    """
    assert path.startswith("$."), path
    node = doc

    for part in _split(path[2:]):
        if part.startswith("?("):
            key, value = part[len("?(@."):-1].split("==", 1)
            value = value.strip().strip("'\"")
            node = next(x for x in node if str(x.get(key)) == value)
        elif part.isdigit():
            node = node[int(part)]
        else:
            node = node[part]

    return node


def _split(path: str) -> list[str]:
    """`characters[?(@.id=='C1')].hp` → `['characters', "?(@.id=='C1')", 'hp']`"""
    parts: list[str] = []
    buf = ""
    depth = 0

    for ch in path:
        if ch == "[":
            depth += 1
            if depth == 1:
                if buf:
                    parts.append(buf)
                buf = ""
                continue
        elif ch == "]":
            depth -= 1
            if depth == 0:
                parts.append(buf)
                buf = ""
                continue
        elif ch == "." and depth == 0:
            if buf:
                parts.append(buf)
            buf = ""
            continue
        buf += ch

    if buf:
        parts.append(buf)
    return parts


def baseline(params: list[Param]) -> dict[str, float]:
    """파라미터별 현재값."""
    docs: dict[str, dict] = {}
    out: dict[str, float] = {}

    for p in params:
        if p.file not in docs:
            docs[p.file] = _load(p.file)
        out[p.name] = _lookup(docs[p.file], p.path)

    return out


def bounds(params: list[Param]) -> dict[str, tuple[float, float]]:
    """파라미터별 (하한, 상한). 현재값 ± span."""
    base = baseline(params)
    out: dict[str, tuple[float, float]] = {}

    for p in params:
        centre = base[p.name]
        lo = centre * (1.0 - p.span)
        hi = centre * (1.0 + p.span)

        if p.lo_abs is not None:
            lo = max(lo, p.lo_abs)
        if hi <= lo:
            hi = lo + 1.0

        out[p.name] = (lo, hi)

    return out


def to_overrides(params: list[Param], values: dict[str, float]) -> dict:
    """`sim` 의 `params.json` 이 받는 `overrides` 절로 바꾼다."""
    by_name = {p.name: p for p in params}
    result: dict[str, dict[str, float | int]] = {}

    for name, value in values.items():
        p = by_name[name]
        result.setdefault(p.file, {})[p.path] = int(round(value)) if p.integer else float(value)

    return result
