// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System.Collections.Generic;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Skills;

namespace DomoNinja.Core.Combat
{
    /// <summary>조건부 강화가 켜지는 조건. <c>economy.items.conditionalBoost</c> 의 <c>condition</c> 값이다.</summary>
    public enum BoostCondition
    {
        None = 0,

        /// <summary>자기 체력이 임계 비율 <b>미만</b>일 때. 임계는 천분율이다.</summary>
        HpBelow,

        /// <summary>살아 있는 적이 임계 <b>초과</b>일 때.</summary>
        EnemiesAbove,

        /// <summary>자기 팀에서 <b>혼자 남았을</b> 때.</summary>
        IsLastAlive,
    }

    /// <summary>
    /// 조건이 맞는 동안만 켜지는 스탯 증감분 (아이템 <c>conditionalBoost</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>다른 아이템처럼 시작 스탯에 접을 수 없다.</b> 세 조건이 전부 전투 중에 뒤집힌다 —
    /// 체력은 깎이고, 적은 죽고, 아군도 죽는다. 접어 넣으면 <b>조건부인데 항상 켜진 아이템</b>이 되어
    /// 밸런스 지표가 실제보다 세게 나온다. 그래서 D+3 까지 <c>Pending</c> 으로 빼두고 있었다.
    /// </para>
    /// <para>
    /// ★ <b>상태이상으로 만들지 않았다.</b> <see cref="StatusKind"/> 는 8종으로 동결돼 있고
    /// 그 번호가 이벤트 로그 v1 계약(`23`)에 박혀 있다. 아이템 하나 때문에 9번째를 늘리면
    /// <b>동결한 포맷을 혼자 깨는 것</b>이 되고, 그 비용은 Unity 쪽에 붙는다.
    /// </para>
    /// <para>
    /// 대신 매 틱 <b>한 번만</b> 평가해 <see cref="Unit.ConditionalAttackDeltaPermille"/> 등에 적어둔다.
    /// 소비 지점(공격 계산 · 피해 계산)에서 그때그때 평가하면
    /// <b>같은 틱 안에서도 누가 먼저 때렸느냐에 따라 값이 갈린다.</b>
    /// </para>
    /// </remarks>
    public readonly struct ConditionalBoost
    {
        public readonly BoostCondition Condition;

        /// <summary>임계값. <see cref="BoostCondition.HpBelow"/> 는 천분율, 나머지는 개수다.</summary>
        public readonly int Threshold;

        public readonly StatKey Stat;

        /// <summary>조건이 맞을 때 더할 증감분(천분율). 음수도 있다 — <c>damageTaken</c> −35%.</summary>
        public readonly int DeltaPermille;

        public ConditionalBoost(BoostCondition condition, int threshold, StatKey stat, int deltaPermille)
        {
            Condition = condition;
            Threshold = threshold;
            Stat = stat;
            DeltaPermille = deltaPermille;
        }

        /// <summary>지금 켜져 있는가.</summary>
        /// <param name="allies">이 유닛과 <b>같은 편</b>. <c>is_last_alive</c> 가 본다.</param>
        /// <param name="foes">반대편. <c>enemies_above</c> 가 본다.</param>
        public bool IsActive(Unit self, IReadOnlyList<Unit> allies, IReadOnlyList<Unit> foes)
        {
            switch (Condition)
            {
                case BoostCondition.HpBelow:
                    return self.HpPermille < Threshold;

                case BoostCondition.EnemiesAbove:
                    return CountAlive(foes) > Threshold;

                case BoostCondition.IsLastAlive:
                    return CountAlive(allies) <= 1;

                default:
                    return false;
            }
        }

        private static int CountAlive(IReadOnlyList<Unit> units)
        {
            int n = 0;
            for (int i = 0; i < units.Count; i++)
                if (units[i].IsAlive) n++;
            return n;
        }

        public static BoostCondition ParseCondition(string? name)
        {
            switch (name)
            {
                case "hp_below": return BoostCondition.HpBelow;
                case "enemies_above": return BoostCondition.EnemiesAbove;
                case "is_last_alive": return BoostCondition.IsLastAlive;
                default: return BoostCondition.None;
            }
        }

        public override string ToString() => $"{Condition}({Threshold}) → {Stat} {DeltaPermille:+#;-#;0}‰";
    }
}
