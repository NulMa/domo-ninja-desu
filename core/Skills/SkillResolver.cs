// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System.Collections.Generic;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using Newtonsoft.Json.Linq;

namespace DomoNinja.Core.Skills
{
    /// <summary>
    /// 선택한 스킬을 <b>전투 시작 시점의 스탯</b>으로 푼다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 스킬 12개를 각각 구현하지 않는 것이 `D-30` 채택의 전제였다 (`08` §3).
    /// 여기서 다루는 건 <b>템플릿뿐</b>이고, 스킬 하나하나에 대한 분기는 이 파일에 없다.
    /// 새 스킬이 생겨도 이 코드는 바뀌지 않는다 — 그게 데이터로 밸런스를 돌리는 조건이다.
    /// </para>
    /// <para>
    /// ⚠️ <b>이 클래스는 「전투 시작 전에 확정되는 것」만 푼다.</b>
    /// <c>conditional</c>(트리거)·<c>aoe</c>·팀 대상 <c>status</c> 는 전투 중 사건이라 여기 없다.
    /// 섞으면 "언제 계산된 값인가"가 흐려지고, 그건 결정론을 되짚을 때 가장 먼저 막히는 지점이다.
    /// </para>
    /// </remarks>
    public static class SkillResolver
    {
        /// <summary>
        /// 캐릭터 기본 스탯에 액티브 1개와 보조 최대 2개를 반영한다.
        /// </summary>
        /// <param name="active">활성화한 액티브 스킬. 없으면 기본 스탯 그대로다.</param>
        /// <param name="supports">구매한 보조 스킬. 캐릭터당 최대 2개.</param>
        public static UnitStats BuildStats(CharacterDef character, SkillDef? active,
                                           IReadOnlyList<SkillDef>? supports = null)
        {
            int skillPower = ResolveSkillPower(supports);

            var hp = new ModifierSum();
            var attack = new ModifierSum();
            var attackInterval = new ModifierSum();
            var moveInterval = new ModifierSum();
            var damageTaken = new ModifierSum();

            int range = character.Range;
            int rangeBonus = 0;

            void Accumulate(SkillDef skill, bool scaledBySkillPower)
            {
                foreach (var token in skill.Effects)
                {
                    if (!(token is JObject e)) continue;

                    switch ((string?)e["template"])
                    {
                        case "stat_mult":
                        {
                            // 자기 자신에게 거는 것만 여기서 푼다. 아군·적 대상은 전투 중 사건이다.
                            if ((string?)e["target"] != "self") continue;

                            var stat = StatKeys.Parse((string?)e["stat"]);
                            int delta = Permille.DeltaFromMultiplier((double?)e["value"] ?? 1d);

                            // ★ skillPower 는 이득 방향의 증감분에만 곱한다.
                            //   대가까지 키우면 skillPower 0.85(보조의 대가로 쓰인다)가
                            //   메인의 페널티를 줄여주는 이득이 되어 부호가 뒤집힌다.
                            if (scaledBySkillPower && StatKeys.IsGain(stat, delta))
                                delta = Permille.Apply(delta, skillPower);

                            switch (stat)
                            {
                                case StatKey.Hp: hp.AddDeltaPermille(delta); break;
                                case StatKey.Attack: attack.AddDeltaPermille(delta); break;
                                case StatKey.AttackInterval: attackInterval.AddDeltaPermille(delta); break;
                                case StatKey.MoveInterval: moveInterval.AddDeltaPermille(delta); break;
                                case StatKey.DamageTaken: damageTaken.AddDeltaPermille(delta); break;
                            }
                            break;
                        }

                        case "targeting":
                        {
                            // setRange(절대 지정)와 rangeBonus(가산)는 다르다 (`_schema` §3).
                            // 보조는 가산을 쓰므로 메인이 사거리를 바꿔도 그 위에 더해진다.
                            if (e["setRange"] != null) range = (int?)e["setRange"] ?? range;
                            if (e["rangeBonus"] != null) rangeBonus += (int?)e["rangeBonus"] ?? 0;
                            break;
                        }
                    }
                }
            }

            if (active != null) Accumulate(active, scaledBySkillPower: true);

            if (supports != null)
            {
                // 보조 자신의 효과는 skillPower 를 타지 않는다 — 자기가 자기를 키우면 제곱이 된다.
                foreach (var s in supports) Accumulate(s, scaledBySkillPower: false);
            }

            return new UnitStats(
                hp.ApplyTo(character.Hp),
                attack.ApplyTo(character.Attack),
                attackInterval.ApplyTo(character.AttackInterval),
                range + rangeBonus,
                moveInterval.ApplyTo(character.MoveInterval),
                damageTaken.DeltaPermille);
        }

        /// <summary>
        /// 보조들이 만든 <c>skillPower</c> 배율(천분율). 보조가 없으면 1000(= 1.0).
        /// </summary>
        /// <remarks>
        /// 여러 보조의 <c>skillPower</c> 도 합연산이다. 곱하면 1.5 × 1.6 = 2.4 로 튀는데,
        /// 보조는 캐릭터당 2개까지라 곱연산이면 조합에 따라 위력이 폭발한다 (`_schema` §8).
        /// </remarks>
        public static int ResolveSkillPower(IReadOnlyList<SkillDef>? supports)
        {
            var sum = new ModifierSum();
            if (supports == null) return Permille.One;

            foreach (var s in supports)
            {
                foreach (var token in s.Effects)
                {
                    if (!(token is JObject e)) continue;
                    if ((string?)e["template"] != "stat_mult") continue;
                    if ((string?)e["target"] != "mainSkill") continue;
                    if (StatKeys.Parse((string?)e["stat"]) != StatKey.SkillPower) continue;

                    sum.AddMultiplier((double?)e["value"] ?? 1d);
                }
            }

            return Permille.One + sum.DeltaPermille;
        }
    }
}
