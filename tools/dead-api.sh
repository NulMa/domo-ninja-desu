#!/usr/bin/env bash
#
# core/ 의 public 멤버 중 "선언한 파일 밖에서 아무도 안 부르는" 것을 찾는다.
#
# ── 왜 있나 ────────────────────────────────────────────────────────────
# `Board.Remove` 가 정확히 그 상태였다. 구현돼 있고, XML 주석에 왜 필요한지까지
# 적혀 있고, "참조를 양쪽에 두면 죽은 유닛이 보드에 남는 버그가 생긴다"고
# 스스로 경고까지 해두고, **호출부가 0개**였다. 결과는 죽은 유닛이 칸을 영구 점유해
# 그 뒤 유닛이 영영 못 지나가는 버그였고, 21,600런과 테스트 359개를 그대로 통과했다.
#   - 컴파일러: public 이라 미사용 경고가 안 난다
#   - 테스트:   아무도 그 메서드를 의심하지 않아 커버가 0이었다
#   - 지표:     M7(타임아웃) 1.67% 가 유일한 흔적인데 목표 <5% 안이라 '합격'으로 읽혔다
#
# ── 무엇이 아닌가 ──────────────────────────────────────────────────────
# ⚠️ **CI 게이트가 아니다. 만들지 말 것.** 이름만 보는 휴리스틱이라 오탐이 많다 —
#    첫 실측에서 10건 중 7건이 오탐이었다. 대부분 **같은 파일 안에서만 쓰이는 멤버**다
#    (`StatKey.HigherIsBetter` 는 바로 아래 `IsGain` 이 부르고, `Loadout.StartEffect` 는
#    아예 타입 이름이다). 사람이 목록을 읽고 판단하는 1회용 감사 도구다.
#    초록/빨강으로 쓰면 쓸데없는 테스트를 짜게 만들거나 무시하는 습관이 든다. 둘 다 손해다.
#
#    2026-08-05 감사 결과: 진짜 미사용은 둘.
#      - `Targeting.HasThreat`  — 어그로 판정이 `SelectTarget` 안에 인라인돼 있어 중복. 무해
#      - `Defs.DeclaredTags`    — 생성자에서 넣기만 하고 읽는 곳이 없다
#
#    ⚠️ **처음에 둘 다 "강제되지 않은 규칙은 아니라서 버그 아님"으로 적었는데 틀렸다.**
#    `DeclaredTags` 는 `_schema` §5 와 `Defs.cs` 주석이 **둘 다 "검증용 기대치"라고
#    선언해둔 값**이다. 검증이 없으니 정확히 `Board.Remove` 와 같은 형태다.
#    코드만 보고 "안 쓰이네 → 무해"로 넘겼고, **스키마와 대조하지 않은 게 원인이다.**
#
#    → 대조해보니 더 깊었다. 30개 전부 불일치이고 어휘 자체가 갈라져 있다.
#      검증 규칙이 빠진 게 아니라 **표와 데이터가 서로 다른 설계로 자란 것**이다. `06` `D-76`.
#
#    ★ 교훈: 이 스크립트가 내놓는 건 "안 쓰이는 이름"이지 "무해한 이름"이 아니다.
#      각 항목을 **선언된 계약(스키마·주석)과 대조하기 전에는 판정하지 말 것.**
#
#    상시 강제는 `BattleInvariants` 쪽이 한다 — 그건 "항상 참인 것"을 적고
#    이미 돌고 있는 런이 전수로 두들기므로 오탐이 없다.
#
# ── 쓰는 법 ────────────────────────────────────────────────────────────
#   bash tools/dead-api.sh
#
# 나온 이름마다 물을 것: **이건 그냥 안 쓰는 편의 함수인가, 아니면
# 지켜져야 하는데 아무도 안 부르는 규칙인가?** 후자만 버그다.
#
set -u
cd "$(git rev-parse --show-toplevel)"

SCAN="core sim sim.tests core.tests unity"

echo "토큰 인덱스 만드는 중..."

# (파일, 식별자) 쌍을 한 번에 뽑는다. 이름마다 트리를 훑으면 O(이름 × 파일) 이라
# 10분이 걸렸다 — 한 번만 훑고 awk 로 집계한다.
grep -roE '\b[A-Za-z_][A-Za-z0-9_]*\b' --include='*.cs' $SCAN 2>/dev/null \
  | sort -u > /tmp/dead-api-tokens.txt

echo "core/ public 멤버 훑는 중..."

# public 메서드/프로퍼티 이름. 타입 선언·상수·생성자는 뺀다.
for f in $(find core -name '*.cs' | sort); do
  base=$(basename "$f" .cs)

  grep -hE '^[[:space:]]+public[[:space:]]' "$f" \
    | grep -vE '\b(class|struct|enum|interface|delegate|const)\b' \
    | sed -E 's|//.*||' \
    | sed -E 's/^[[:space:]]*public[[:space:]]+//' \
    | sed -E 's/^(static|readonly|sealed|virtual|override|abstract|async|new|partial|unsafe|ref)[[:space:]]+//g' \
    | sed -E 's/[[:space:]]*(\(|\{|=>|=[^=]).*$//' \
    | sed -E 's/.*[[:space:]]//' \
    | grep -E '^[A-Z][A-Za-z0-9_]*$' \
    | sort -u \
    | while read -r name; do
        # 타입 이름과 같으면 생성자다.
        [ "$name" = "$base" ] && continue

        # 선언 파일을 뺀 나머지에서 이 이름이 나오는 파일 수.
        others=$(grep -E ":${name}\$" /tmp/dead-api-tokens.txt | grep -vc "^${f}:")

        [ "$others" -eq 0 ] && echo "  $f :: $name"
      done
done

echo
echo "위 목록은 '의심'이지 '버그'가 아니다. 같은 파일 안에서만 쓰이는 멤버도 걸린다."
