// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System;

namespace DomoNinja.Core.Skills
{
    /// <summary>스탯 이름. `_schema` §2 의 전역 목록이다.</summary>
    public enum StatKey
    {
        None = 0,
        Hp,
        Attack,
        AttackInterval,
        Range,
        MoveInterval,
        DamageTaken,

        /// <summary>보조 스킬 전용 가상 스탯. <b>실제 유닛 스탯이 아니다</b> — 메인 스킬의 위력을 키운다.</summary>
        SkillPower,
    }

    public static class StatKeys
    {
        public static StatKey Parse(string? name)
        {
            switch (name)
            {
                case "hp": return StatKey.Hp;
                case "attack": return StatKey.Attack;
                case "attackInterval": return StatKey.AttackInterval;
                case "range": return StatKey.Range;
                case "moveInterval": return StatKey.MoveInterval;
                case "damageTaken": return StatKey.DamageTaken;
                case "skillPower": return StatKey.SkillPower;
                default: return StatKey.None;
            }
        }

        /// <summary>
        /// 값이 <b>클수록</b> 유리한 스탯인가.
        /// </summary>
        /// <remarks>
        /// ★ 이 한 줄이 `skillPower` 의 의미를 결정한다.
        /// `attackInterval`·`moveInterval`·`damageTaken` 은 <b>낮을수록 좋다</b> (`_schema` §2).
        /// 부호만 보고 이득/대가를 판단하면 이 셋에서 정반대로 읽는다.
        /// </remarks>
        public static bool HigherIsBetter(StatKey stat)
        {
            switch (stat)
            {
                case StatKey.Hp:
                case StatKey.Attack:
                case StatKey.Range:
                case StatKey.SkillPower:
                    return true;
                default:
                    return false;   // attackInterval · moveInterval · damageTaken
            }
        }

        /// <summary>이 증감분이 <b>이득 방향</b>인가.</summary>
        public static bool IsGain(StatKey stat, int deltaPermille) =>
            HigherIsBetter(stat) ? deltaPermille > 0 : deltaPermille < 0;
    }

    /// <summary>
    /// 스킬·보조·아이템을 전부 반영한 <b>전투 시작 시점의 스탯</b>.
    /// </summary>
    /// <remarks>
    /// 유닛을 만든 뒤 고치지 않고 <b>먼저 계산해서 넘기는</b> 이유 —
    /// <c>hp</c> 배율(C3-A 그림자 −20%)이 최대 체력 자체를 바꾸는데,
    /// 유닛을 만든 뒤 최대 체력을 내리면 현재 HP 를 어떻게 할지가 애매해진다.
    /// 계산을 앞으로 빼면 그 질문이 아예 생기지 않는다.
    /// </remarks>
    public readonly struct UnitStats
    {
        public readonly int Hp;
        public readonly int Attack;
        public readonly int AttackInterval;
        public readonly int Range;
        public readonly int MoveInterval;

        /// <summary>받는 피해 증감분(천분율). 배율이 아니라 증감분이다 (`_schema` §8).</summary>
        public readonly int DamageTakenDeltaPermille;

        public UnitStats(int hp, int attack, int attackInterval, int range, int moveInterval,
                         int damageTakenDeltaPermille)
        {
            // 간격이 0 이면 매 틱 행동하게 되어 사실상 무한 공격·무한 이동이 된다.
            // 검증 규칙 R19 가 데이터에서 막지만, 배율을 곱한 결과도 같은 함정에 빠질 수 있다.
            Hp = Math.Max(1, hp);
            Attack = Math.Max(0, attack);
            AttackInterval = Math.Max(1, attackInterval);
            Range = Math.Max(0, range);
            MoveInterval = Math.Max(1, moveInterval);
            DamageTakenDeltaPermille = damageTakenDeltaPermille;
        }

        public override string ToString() =>
            $"hp{Hp} atk{Attack} ai{AttackInterval} r{Range} mi{MoveInterval} dt{DamageTakenDeltaPermille:+#;-#;0}‰";
    }
}
