// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

namespace DomoNinja.Core.Combat
{
    /// <summary>
    /// 배율을 <b>천분율 정수</b>로 다룬다. 전투 안에 부동소수점을 들이지 않기 위한 타입이다.
    /// </summary>
    /// <remarks>
    /// JSON 에는 배율이 <c>value: 1.5</c> 같은 실수로 적혀 있다(사람이 읽어야 하니까).
    /// 그 실수를 <b>로드 시점에 한 번만</b> 천분율 정수로 바꾸고, 그 뒤로는 정수만 쓴다.
    /// 매 틱 double 을 곱하면 누산 오차가 쌓여 sim 과 Unity 가 다른 결과를 낼 수 있다 (`_schema` §7).
    /// </remarks>
    public static class Permille
    {
        /// <summary>배율 1.0.</summary>
        public const int One = 1000;

        /// <summary>1.5 → 1500. <b>로드 경계에서만 부른다.</b></summary>
        public static int FromMultiplier(double multiplier)
        {
            // Math.Round 대신 명시적 반올림을 쓴다 — 은행가 반올림(중간값을 짝수로)은
            // 0.5 를 어느 쪽으로 보낼지가 값마다 달라서, 데이터를 읽고 "왜 이 값이 됐지"를
            // 되짚을 때 규칙이 한눈에 안 보인다. 여기선 예측 가능한 쪽을 택한다.
            double scaled = multiplier * One;
            return (int)(scaled >= 0 ? scaled + 0.5 : scaled - 0.5);
        }

        /// <summary>1.5 → +500, 0.6 → -400. <b>합연산에 더할 증감분이다.</b></summary>
        public static int DeltaFromMultiplier(double multiplier) => FromMultiplier(multiplier) - One;

        /// <summary>
        /// 천분율 적용. <b>정수 나눗셈이라 절삭된다.</b>
        /// </summary>
        /// <remarks>
        /// 절삭이 일어나는 지점이 여기 하나뿐인 것이 합연산 규칙(`_schema` §8)의 실질이다.
        /// 곱을 여러 번 하면 절삭도 여러 번 일어나고, 그러면 <b>적용 순서에 따라 결과가 달라진다.</b>
        ///
        /// ⚠️ 중간 계산을 <c>long</c> 으로 올리는 것만으로는 부족해서 결과도 자른다.
        /// <c>int</c> 범위를 넘긴 결과를 그대로 캐스팅하면 <b>감싸면서 음수가 된다</b> —
        /// 피해 계산이라면 그 자리에서 회복이 되는 셈이다. 게임 수치로는 도달하기 어려운
        /// 크기지만, <b>조용히 틀리는 종류</b>라 값을 자르는 쪽을 택했다.
        /// </remarks>
        public static int Apply(int value, int permille)
        {
            long result = (long)value * permille / One;
            if (result > int.MaxValue) return int.MaxValue;
            if (result < int.MinValue) return int.MinValue;
            return (int)result;
        }
    }

    /// <summary>
    /// 합연산 누산기. <b>전부 더한 뒤 마지막에 한 번만 적용한다</b> (`A-10` · `_schema` §8).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 곱연산(<c>attack * 1.5 * 1.2 * 0.9</c>)을 쓰지 않는 이유는 셋이다.
    /// <b>결정론</b> — 절삭 지점이 하나뿐이라 누산 순서 의존이 사라진다.
    /// <b>예측가능성</b> — 플레이어가 상점에서 암산할 수 있다.
    /// <b>밸런스</b> — 곱연산은 버프를 겹칠수록 폭발해 `M4` 지배 빌드를 만든다.
    /// </para>
    /// <para>
    /// ★ struct 인 이유 — 스탯 하나를 계산할 때마다 만들어지고 버려진다.
    /// class 면 매 틱 유닛 수만큼 할당이 생기고, 그건 sim 처리량에 그대로 실린다.
    /// </para>
    /// </remarks>
    public struct ModifierSum
    {
        /// <summary>1000 기준 증감분의 합. 0 이면 아무 보정도 없다.</summary>
        public int DeltaPermille { get; private set; }

        /// <summary>JSON 의 배율(<c>1.5</c>)을 더한다.</summary>
        public void AddMultiplier(double multiplier) =>
            DeltaPermille += Permille.DeltaFromMultiplier(multiplier);

        /// <summary>이미 천분율로 정규화된 증감분을 더한다.</summary>
        public void AddDeltaPermille(int deltaPermille) => DeltaPermille += deltaPermille;

        /// <summary>
        /// 기본값에 <b>한 번만</b> 적용한다.
        /// </summary>
        /// <remarks>
        /// ⚠️ 총합이 -100% 아래로 내려가도 <b>0 밑으로는 가지 않는다.</b>
        /// 감소 계열(`damageTaken`)이 여럿 겹치면 음수 배율이 나올 수 있는데,
        /// 그대로 두면 <b>피해가 회복이 된다.</b> 그건 밸런스가 아니라 고장이라 여기서 막는다.
        /// </remarks>
        public int ApplyTo(int baseValue)
        {
            int total = Permille.One + DeltaPermille;
            if (total < 0) total = 0;
            return Permille.Apply(baseValue, total);
        }

        public override string ToString() =>
            $"{(DeltaPermille >= 0 ? "+" : "")}{DeltaPermille}‰";
    }
}
