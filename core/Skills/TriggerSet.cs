// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System.Collections.Generic;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using Newtonsoft.Json.Linq;

namespace DomoNinja.Core.Skills
{
    /// <summary>트리거 6종 (`_schema` §3 <c>conditional</c>).</summary>
    public enum TriggerType
    {
        None = 0,

        /// <summary>적을 처치한 순간.</summary>
        OnKill,

        /// <summary>피해를 받은 순간.</summary>
        OnDamaged,

        /// <summary>피격을 무효화한 순간. <c>invulnerable_first_hit</c> 가 먹었을 때다.</summary>
        OnDodge,

        /// <summary>자신의 공격이 <b>적중한</b> 순간. <c>OnDamaged</c>(피격당함)와 반대 방향이다.</summary>
        OnHit,

        /// <summary>HP 가 임계 <b>아래로 내려간</b> 순간.</summary>
        HpBelow,

        /// <summary>N 틱마다.</summary>
        EveryNTick,
    }

    /// <summary>
    /// 발동 조건 하나와 그때 실행할 효과.
    /// </summary>
    /// <remarks>
    /// 상태(다음 발동 틱, 임계 통과 여부)를 들고 있어서 <b>유닛마다 따로 만들어진다.</b>
    /// 두 유닛이 같은 스킬을 써도 주기가 각자 돌아야 한다.
    /// </remarks>
    public sealed class CompiledTrigger
    {
        public TriggerType Type { get; }

        /// <summary><c>HpBelow</c> 는 천분율, <c>EveryNTick</c> 은 틱. 나머지는 0.</summary>
        public int Threshold { get; }

        /// <summary>실행할 효과. 템플릿 5종이거나 원자 동작 5종이다.</summary>
        public JObject Effect { get; }

        /// <summary>
        /// 이 트리거가 <b>메인 스킬</b>에서 왔으면 보조가 만든 위력 배율(천분율), 아니면 1000.
        /// </summary>
        /// <remarks>
        /// 보조 자신의 트리거는 배율을 타지 않는다 — 자기가 자기를 키우면 제곱이 된다.
        /// <see cref="SkillResolver.BuildStats"/> 의 규칙과 같다.
        /// </remarks>
        public int SkillPowerPermille { get; }

        /// <summary>스킬 id. 로그와 디버깅에 쓴다.</summary>
        public string SourceSkillId { get; }

        internal int NextFireTick;
        internal bool ThresholdArmed = true;

        public CompiledTrigger(TriggerType type, int threshold, JObject effect,
                               int skillPowerPermille, string sourceSkillId)
        {
            Type = type;
            Threshold = threshold;
            Effect = effect;
            SkillPowerPermille = skillPowerPermille;
            SourceSkillId = sourceSkillId;
            NextFireTick = threshold;   // every_n_tick 은 0 틱이 아니라 N 틱에 처음 터진다
        }

        public override string ToString() => $"{SourceSkillId}:{Type}({Threshold})";
    }

    /// <summary>
    /// 한 유닛이 가진 트리거 전부. <b>언제 터지는지만 안다 — 무엇이 일어나는지는 모른다.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 발동 조건과 효과 실행을 나눈 이유 — 조건은 순수 계산이라 단독으로 검증할 수 있지만,
    /// 효과 실행은 유닛·보드·이벤트 싱크를 전부 건드린다. 붙여 두면
    /// "주기가 맞는가"를 확인하려고 전투를 통째로 돌려야 한다.
    /// </para>
    /// <para>
    /// ⚠️ 현재 데이터가 쓰는 트리거는 <c>on_hit</c>·<c>on_kill</c>·<c>on_damaged</c>·
    /// <c>every_n_tick</c>·<c>on_dodge</c> 5종이다. <b><c>hp_below</c> 는 스킬에서 한 번도 안 쓴다</b>
    /// (아이템 <c>conditionalBoost</c> 에만 있다). 그래서 아래 「경계 통과 시 1회」 해석은
    /// <b>실사용으로 검증된 것이 아니다</b> — 스킬이 실제로 쓰기 시작하면 다시 봐야 한다.
    /// </para>
    /// </remarks>
    public sealed class TriggerSet
    {
        private static readonly IReadOnlyList<CompiledTrigger> Empty = new CompiledTrigger[0];

        private readonly List<CompiledTrigger> _all = new List<CompiledTrigger>();

        public IReadOnlyList<CompiledTrigger> All => _all;

        public int Count => _all.Count;

        /// <summary>액티브 1개와 보조 최대 2개에서 트리거를 뽑는다.</summary>
        public static TriggerSet Compile(SkillDef? active, IReadOnlyList<SkillDef>? supports = null)
        {
            var set = new TriggerSet();
            int skillPower = SkillResolver.ResolveSkillPower(supports);

            if (active != null) set.CompileSkill(active, skillPower);

            if (supports != null)
                foreach (var s in supports) set.CompileSkill(s, Permille.One);

            return set;
        }

        private void CompileSkill(SkillDef skill, int skillPowerPermille)
        {
            foreach (var token in skill.Effects)
            {
                if (!(token is JObject e)) continue;
                if ((string?)e["template"] != "conditional") continue;

                var trigger = e["trigger"] as JObject;
                var effect = e["effect"] as JObject;
                if (trigger == null || effect == null) continue;

                var type = ParseType((string?)trigger["type"]);
                if (type == TriggerType.None) continue;

                _all.Add(new CompiledTrigger(type, ParseThreshold(type, trigger),
                                             effect, skillPowerPermille, skill.Id));
            }
        }

        private static TriggerType ParseType(string? name)
        {
            switch (name)
            {
                case "on_kill": return TriggerType.OnKill;
                case "on_damaged": return TriggerType.OnDamaged;
                case "on_dodge": return TriggerType.OnDodge;
                case "on_hit": return TriggerType.OnHit;
                case "hp_below": return TriggerType.HpBelow;
                case "every_n_tick": return TriggerType.EveryNTick;
                default: return TriggerType.None;
            }
        }

        /// <remarks>
        /// <c>hp_below</c> 의 <c>value</c> 는 비율(<c>0.5</c>)이라 천분율로 바꾼다.
        /// <c>every_n_tick</c> 은 <b>이미 틱 정수</b>다 — 초 단위 실수 누적 금지(`_schema` §3 · `A-7`).
        /// </remarks>
        private static int ParseThreshold(TriggerType type, JObject trigger)
        {
            switch (type)
            {
                case TriggerType.HpBelow:
                    return Permille.FromMultiplier((double?)trigger["value"] ?? 0d);
                case TriggerType.EveryNTick:
                    int ticks = (int?)trigger["value"] ?? 0;
                    return ticks < 1 ? 1 : ticks;   // 0 이면 매 틱 터져 무한 발동이 된다
                default:
                    return 0;
            }
        }

        /// <summary>사건형 트리거를 모은다. <c>EveryNTick</c>·<c>HpBelow</c> 는 여기로 오지 않는다.</summary>
        public IReadOnlyList<CompiledTrigger> Matching(TriggerType type)
        {
            List<CompiledTrigger>? hits = null;
            for (int i = 0; i < _all.Count; i++)
            {
                if (_all[i].Type != type) continue;
                (hits ??= new List<CompiledTrigger>()).Add(_all[i]);
            }
            return (IReadOnlyList<CompiledTrigger>?)hits ?? Empty;
        }

        /// <summary>
        /// 이번 틱에 터질 주기 트리거를 <paramref name="into"/> 에 담는다. <b>담으면서 다음 발동 시각을 민다.</b>
        /// </summary>
        /// <remarks>
        /// 절대 틱을 누적하지 않고 <c>NextFireTick += Threshold</c> 로 미는 이유 —
        /// 틱을 건너뛰어도(전투가 한 틱에 여러 사건을 처리해도) 주기가 밀리지 않는다.
        /// </remarks>
        public void CollectPeriodic(int tick, List<CompiledTrigger> into)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                var t = _all[i];
                if (t.Type != TriggerType.EveryNTick) continue;
                if (tick < t.NextFireTick) continue;

                into.Add(t);
                t.NextFireTick += t.Threshold;
            }
        }

        /// <summary>
        /// HP 가 임계 아래로 <b>내려간 순간</b> 한 번 터진다. 임계 위로 돌아오면 다시 장전된다.
        /// </summary>
        /// <remarks>
        /// ⚠️ 스킬이 아직 이 트리거를 쓰지 않는다. 「경계 통과 시 1회」와 「임계 이하인 동안 계속」 중
        /// 전자로 해석했는데, 후자로 쓰는 스킬(예: <c>hp_below</c> + <c>stat_mult</c> = 저체력 시 공격력 증가)이
        /// 생기면 이 해석으로는 표현되지 않는다. <b>그때 다시 정한다.</b>
        /// </remarks>
        public void CollectHpBelow(int hpPermille, List<CompiledTrigger> into)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                var t = _all[i];
                if (t.Type != TriggerType.HpBelow) continue;

                if (hpPermille >= t.Threshold)
                {
                    t.ThresholdArmed = true;
                    continue;
                }

                if (!t.ThresholdArmed) continue;

                into.Add(t);
                t.ThresholdArmed = false;
            }
        }
    }
}
