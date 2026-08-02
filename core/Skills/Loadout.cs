// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System.Collections.Generic;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using Newtonsoft.Json.Linq;

namespace DomoNinja.Core.Skills
{
    /// <summary>전투 시작 시 한 번 실행되는 효과와, 그때 쓸 위력 배율.</summary>
    public readonly struct StartEffect
    {
        public readonly JObject Effect;
        public readonly int SkillPowerPermille;

        public StartEffect(JObject effect, int skillPowerPermille)
        {
            Effect = effect;
            SkillPowerPermille = skillPowerPermille;
        }
    }

    /// <summary>
    /// 유닛 하나가 전투에 들고 들어가는 스킬 전부를 <b>실행 가능한 형태로</b> 미리 풀어둔 것.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 전투 루프가 매 틱 <see cref="SkillDef.Effects"/> 를 훑으면
    /// <b>JSON 순회 비용이 유닛 × 틱 × 런 만큼 곱해진다.</b> 스킬은 전투 중에 바뀌지 않으므로
    /// 시작 전에 한 번만 분류한다. <see cref="Combat.CombatConfig"/> 를 뽑아둔 것과 같은 이유다.
    /// </para>
    /// <para>
    /// 분류가 곧 <b>"언제 일어나는가"</b> 다 —
    /// <see cref="StartEffects"/>(시작 1회) · <see cref="Triggers"/>(사건·주기) · <see cref="Aoe"/>(공격할 때마다).
    /// <c>stat_mult</c>·<c>targeting</c> 은 여기 없다. 그건 이미 <see cref="SkillResolver.BuildStats"/> 가
    /// 스탯으로 접어 넣었고, 전투 중에 다시 볼 이유가 없다.
    /// </para>
    /// </remarks>
    public sealed class Loadout
    {
        private static readonly StartEffect[] NoStart = new StartEffect[0];

        /// <summary>사건·주기 트리거.</summary>
        public TriggerSet Triggers { get; }

        /// <summary>
        /// 최상위 <c>aoe</c>. 있으면 <b>평타가 광역이 된다</b>.
        /// </summary>
        /// <remarks>
        /// 세 스킬만 갖는다 — <c>C1-B</c> 연격(인접 파급) · <c>C2-B</c> 파동(전체) · <c>C4-B</c> 난사(다중 표적).
        /// </remarks>
        public JObject? Aoe { get; }

        /// <summary>보조가 만든 메인 스킬 위력 배율(천분율).</summary>
        public int SkillPowerPermille { get; }

        /// <summary>전투 시작 시 한 번 거는 것들. <c>self</c>·<c>allies</c>·<c>all_enemies</c> 대상이다.</summary>
        public IReadOnlyList<StartEffect> StartEffects { get; }

        /// <summary>
        /// 공격할 때마다 <b>맞은 표적에게</b> 거는 것들. <c>target: "enemy"</c> 인 <c>status</c> 다.
        /// </summary>
        /// <remarks>
        /// ★ 시작 효과와 갈라놓은 이유 — <c>enemy</c> 는 <b>"지금 때리는 상대"</b>라
        /// 전투 시작 시점에는 가리킬 대상이 없다. 같이 묶어 두면 대상을 못 찾아
        /// <b>조용히 아무 일도 일어나지 않는다.</b>
        /// <c>C5-A</c> 각인이 실제로 그 상태였고, 통합 테스트가 그걸 잡았다.
        /// </remarks>
        public IReadOnlyList<StartEffect> OnAttackEffects { get; }

        private Loadout(TriggerSet triggers, JObject? aoe, int skillPower,
                        IReadOnlyList<StartEffect> start, IReadOnlyList<StartEffect> onAttack)
        {
            Triggers = triggers;
            Aoe = aoe;
            SkillPowerPermille = skillPower;
            StartEffects = start;
            OnAttackEffects = onAttack;
        }

        /// <summary>액티브 1개와 보조 최대 2개를 분류한다.</summary>
        public static Loadout Build(SkillDef? active, IReadOnlyList<SkillDef>? supports = null)
        {
            int skillPower = SkillResolver.ResolveSkillPower(supports);
            JObject? aoe = null;
            List<StartEffect>? start = null;
            List<StartEffect>? onAttack = null;

            void Scan(SkillDef skill, int power)
            {
                foreach (var token in skill.Effects)
                {
                    if (!(token is JObject e)) continue;

                    switch ((string?)e["template"])
                    {
                        case "aoe":
                            // 두 개가 겹치는 경우는 데이터에 없다. 생기면 먼저 것을 쓴다 —
                            // 조용히 덮어쓰면 어느 쪽이 적용됐는지 로그로도 알 수 없다.
                            aoe ??= e;
                            break;

                        case "status":
                            // "enemy" 는 지금 때리는 상대라 시작 시점에 가리킬 대상이 없다.
                            if ((string?)e["target"] == "enemy")
                                (onAttack ??= new List<StartEffect>()).Add(new StartEffect(e, power));
                            else
                                (start ??= new List<StartEffect>()).Add(new StartEffect(e, power));
                            break;
                    }
                }
            }

            if (active != null) Scan(active, skillPower);

            // 보조 자신의 효과는 위력 배율을 타지 않는다 — 자기가 자기를 키우면 제곱이 된다.
            if (supports != null)
                foreach (var s in supports) Scan(s, Permille.One);

            return new Loadout(
                TriggerSet.Compile(active, supports),
                aoe,
                skillPower,
                (IReadOnlyList<StartEffect>?)start ?? NoStart,
                (IReadOnlyList<StartEffect>?)onAttack ?? NoStart);
        }

        /// <summary>스킬이 하나도 없는 유닛(적 전부). <b>적은 스킬을 갖지 않는다</b> (`D-39`).</summary>
        public static readonly Loadout Empty =
            new Loadout(TriggerSet.Compile(null), null, Permille.One, NoStart, NoStart);
    }
}
