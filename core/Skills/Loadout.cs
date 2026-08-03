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
    /// 한 번 걸고 끝나지 않는 보호막. <b>주기 재충전</b> 또는 <b>제자리 유지</b> 둘 중 하나다.
    /// </summary>
    /// <remarks>
    /// <see cref="EveryTicks"/> 와 <see cref="MaxPermille"/> 을 <see cref="Effect"/> 에서 다시 읽지 않고
    /// 여기 꺼내 둔다 — 전투 루프가 <b>매 틱 유닛마다</b> 보는 값이라 JSON 순회 비용이
    /// 유닛 × 틱 × 런 만큼 곱해진다. <see cref="Loadout"/> 전체가 존재하는 이유와 같다.
    /// </remarks>
    public readonly struct SustainedShield
    {
        public readonly JObject Effect;
        public readonly int SkillPowerPermille;

        /// <summary>재충전 주기(틱). <b>0 이면 주기가 없다</b> — 제자리 유지형이다.</summary>
        public readonly int EveryTicks;

        /// <summary>이 효과가 책임지는 보호막 상한(대상 최대 체력의 천분율).</summary>
        public readonly int MaxPermille;

        public SustainedShield(JObject effect, int skillPowerPermille, int everyTicks, int maxPermille)
        {
            Effect = effect;
            SkillPowerPermille = skillPowerPermille;
            EveryTicks = everyTicks;
            MaxPermille = maxPermille;
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
        private static readonly SustainedShield[] NoSustained = new SustainedShield[0];

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

        /// <summary>
        /// <c>refreshEveryTicks</c> 가 붙은 보호막. <b>시작 1회는 <see cref="StartEffects"/> 가 이미 걸었고,</b>
        /// 여기 있는 건 그 <b>다음부터</b>다.
        /// </summary>
        /// <remarks>
        /// 이게 없으면 <c>C3-P2</c> 잔영 · <c>C5-P2</c> 결계 · <c>C6-P3</c> 광명이
        /// <b>"10초마다" 라고 적힌 채 한 번만 주는 스킬</b>이 된다. 컴파일도 되고 테스트도 통과한다 —
        /// 텍스트를 읽어보기 전까지 아무도 모른다.
        /// </remarks>
        public IReadOnlyList<SustainedShield> RefreshingShields { get; }

        /// <summary>
        /// <c>whileStationary</c> 보호막 (<c>C4-P2</c> 참호). <b>없으면 <c>null</c>.</b>
        /// </summary>
        /// <remarks>
        /// <para>
        /// ★ <b>이건 <see cref="StartEffects"/> 에 들어가지 않는다.</b> 시작 시점에 거는 게 아니라
        /// <b>"제자리인가"를 매 틱 물어 켜고 끄는</b> 것이라, 시작 효과로도 걸어두면
        /// 켠 사실이 기록되지 않은 보호막이 생겨 <b>이동해도 안 풀린다.</b>
        /// 전투 시작 시점은 <see cref="Domain.Unit.LastMoveTick"/> 이 −1 이라 첫 틱에 자연히 켜진다.
        /// </para>
        /// <para>
        /// 하나만 든다. 데이터상 <c>생존</c> 슬롯 보조는 캐릭터당 하나뿐이라 겹칠 수 없고,
        /// 겹치는 순간 "이동 시 되돌릴 양"이 출처별로 갈라져 단일 보호막 풀로는 표현되지 않는다.
        /// </para>
        /// </remarks>
        public SustainedShield? StationaryShield { get; }

        private Loadout(TriggerSet triggers, JObject? aoe, int skillPower,
                        IReadOnlyList<StartEffect> start, IReadOnlyList<StartEffect> onAttack,
                        IReadOnlyList<SustainedShield> refreshing, SustainedShield? stationary)
        {
            Triggers = triggers;
            Aoe = aoe;
            SkillPowerPermille = skillPower;
            StartEffects = start;
            OnAttackEffects = onAttack;
            RefreshingShields = refreshing;
            StationaryShield = stationary;
        }

        /// <summary>액티브 1개와 보조 최대 2개를 분류한다.</summary>
        public static Loadout Build(SkillDef? active, IReadOnlyList<SkillDef>? supports = null)
        {
            int skillPower = SkillResolver.ResolveSkillPower(supports);
            JObject? aoe = null;
            List<StartEffect>? start = null;
            List<StartEffect>? onAttack = null;
            List<SustainedShield>? refreshing = null;
            SustainedShield? stationary = null;

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
                            bool isShield = (string?)e["kind"] == "shield";
                            int maxPermille = (int?)e["maxPermille"] ?? 0;

                            // 제자리 유지형은 시작 효과로 걸지 않는다 — 이유는 StationaryShield 주석.
                            if (isShield && ((bool?)e["whileStationary"] ?? false))
                            {
                                stationary ??= new SustainedShield(e, power, 0, maxPermille);
                                break;
                            }

                            // "enemy" 는 지금 때리는 상대라 시작 시점에 가리킬 대상이 없다.
                            if ((string?)e["target"] == "enemy")
                                (onAttack ??= new List<StartEffect>()).Add(new StartEffect(e, power));
                            else
                                (start ??= new List<StartEffect>()).Add(new StartEffect(e, power));

                            int every = isShield ? ((int?)e["refreshEveryTicks"] ?? 0) : 0;
                            if (every > 0)
                                (refreshing ??= new List<SustainedShield>())
                                    .Add(new SustainedShield(e, power, every, maxPermille));
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
                (IReadOnlyList<StartEffect>?)onAttack ?? NoStart,
                (IReadOnlyList<SustainedShield>?)refreshing ?? NoSustained,
                stationary);
        }

        /// <summary>스킬이 하나도 없는 유닛(적 전부). <b>적은 스킬을 갖지 않는다</b> (`D-39`).</summary>
        public static readonly Loadout Empty =
            new Loadout(TriggerSet.Compile(null), null, Permille.One, NoStart, NoStart, NoSustained, null);
    }
}
