// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System;
using System.Collections.Generic;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Events;
using Newtonsoft.Json.Linq;

namespace DomoNinja.Core.Skills
{
    /// <summary>효과 하나를 실행하는 데 필요한 주변 상황.</summary>
    /// <remarks>
    /// 유닛 목록을 받아 두는 이유 — 대상 지정(<c>allies</c>·<c>lowestHpAlly</c>·<c>all_enemies</c>)이
    /// 팀 전체를 봐야 풀린다. <b>둘 다 슬롯 인덱스 오름차순으로 들어와야 한다</b>(동률 타이브레이커).
    /// </remarks>
    public readonly struct EffectContext
    {
        public readonly Unit Self;

        /// <summary><c>target: "enemy"</c> 가 가리키는 현재 표적. 없을 수 있다.</summary>
        public readonly Unit? Target;

        /// <summary>방금 발생한 피해량. <c>fromDamagePermille</c> 이 이 값을 쓴다.</summary>
        public readonly int LastDamage;

        public readonly int Tick;
        public readonly IReadOnlyList<Unit> Team;
        public readonly IReadOnlyList<Unit> Foes;
        public readonly IEventSink Sink;

        public EffectContext(Unit self, Unit? target, int lastDamage, int tick,
                             IReadOnlyList<Unit> team, IReadOnlyList<Unit> foes, IEventSink sink)
        {
            Self = self; Target = target; LastDamage = lastDamage; Tick = tick;
            Team = team; Foes = foes; Sink = sink;
        }
    }

    /// <summary>
    /// 효과가 요구한 <b>추가 행동</b>. 실행은 전투 루프가 한다.
    /// </summary>
    /// <remarks>
    /// ★ 여기서 직접 공격을 부르지 않는 이유가 결정론이다.
    /// <c>extra_attack</c>·<c>recast</c> 는 공격을 다시 일으키고, 그 공격이 또 처치를 만들면
    /// <c>on_kill</c> 이 다시 터진다. 실행을 이 안에서 하면 <b>재귀 깊이가 데이터에 따라 정해지고</b>
    /// 어디서 멈추는지가 코드에 안 보인다.
    /// 요청만 돌려주고 전투 루프가 명시적 루프로 처리하면 상한이 한 군데에 있다.
    /// </remarks>
    public readonly struct EffectOutcome
    {
        /// <summary>같은 표적에 추가 공격 횟수.</summary>
        public readonly int ExtraAttacks;

        /// <summary><b>다음 표적</b>에 재발동할 최대 연쇄 수. 0 이면 없다.</summary>
        public readonly int RecastChain;

        public EffectOutcome(int extraAttacks, int recastChain)
        {
            ExtraAttacks = extraAttacks;
            RecastChain = recastChain;
        }

        public static readonly EffectOutcome None = new EffectOutcome(0, 0);

        public bool IsNone => ExtraAttacks == 0 && RecastChain == 0;
    }

    /// <summary>
    /// 트리거가 터졌을 때 실제로 일어나는 일.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 현재 데이터가 중첩해서 쓰는 효과는 <b>5종뿐</b>이다 —
    /// <c>heal</c> 4 · <c>status</c> 5 · <c>self_damage</c> 2 · <c>extra_attack</c> 1 · <c>recast</c> 1.
    /// <c>counter</c> 와 중첩 <c>stat_mult</c>·<c>aoe</c> 는 한 번도 안 쓴다.
    /// </para>
    /// <para>
    /// ⚠️ <b>모르는 효과를 조용히 넘기지 않는다.</b> <see cref="IsSupported"/> 로 열어두고
    /// 테스트가 실제 데이터 전부를 대조한다. 데이터가 코드보다 앞서가면
    /// 그 스킬은 <b>아무 일도 안 하면서 밸런스 지표에는 잡히는</b> 상태가 되는데,
    /// 그건 시뮬 결과 전체를 조용히 오염시킨다.
    /// </para>
    /// </remarks>
    public static class EffectExecutor
    {
        /// <summary>이 실행기가 다룰 수 있는 효과인가.</summary>
        public static bool IsSupported(string? template)
        {
            switch (template)
            {
                case "heal":
                case "status":
                case "self_damage":
                case "extra_attack":
                case "recast":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 효과를 실행한다. <paramref name="skillPowerPermille"/> 는 <b>이득 크기에만</b> 곱한다.
        /// </summary>
        /// <remarks>
        /// <see cref="SkillResolver.BuildStats"/> 와 같은 규칙이다 —
        /// <c>self_damage</c> 는 대가라서 배율을 타지 않는다.
        /// 타게 하면 위력을 키울수록 대가가 줄어드는 게 아니라 늘어야 하는데,
        /// 그건 보조의 <c>skillPower</c> 가 1 미만일 때 부호가 뒤집힌다.
        /// </remarks>
        public static EffectOutcome Execute(JObject effect, in EffectContext ctx, int skillPowerPermille)
        {
            switch ((string?)effect["template"])
            {
                case "heal": return Heal(effect, ctx, skillPowerPermille);
                case "status": return ApplyStatus(effect, ctx, skillPowerPermille);
                case "self_damage": return SelfDamage(effect, ctx);
                case "extra_attack": return new EffectOutcome((int?)effect["count"] ?? 1, 0);
                case "recast": return new EffectOutcome(0, (int?)effect["maxChain"] ?? 1);
                default: return EffectOutcome.None;
            }
        }

        private static EffectOutcome Heal(JObject e, in EffectContext ctx, int skillPower)
        {
            var targets = Resolve((string?)e["target"], ctx);
            if (targets.Count == 0) return EffectOutcome.None;

            bool fromDamage = e["fromDamagePermille"] != null;
            int permille = (int?)(fromDamage ? e["fromDamagePermille"] : e["permille"]) ?? 0;
            permille = Permille.Apply(permille, skillPower);

            foreach (var t in targets)
            {
                // permille — 대상 최대 체력의 천분율 / fromDamagePermille — 방금 발생한 피해의 천분율
                int amount = fromDamage
                    ? Permille.Apply(ctx.LastDamage, permille)
                    : Permille.Apply(t.MaxHp, permille);

                DamageResolver.ApplyHeal(t, amount, ctx.Self.Id, ctx.Tick, ctx.Sink);
            }

            return EffectOutcome.None;
        }

        private static EffectOutcome SelfDamage(JObject e, in EffectContext ctx)
        {
            // 자신의 최대 체력 비율만큼 깎는다. 대가이므로 skillPower 를 타지 않는다.
            int permille = Permille.FromMultiplier((double?)e["value"] ?? 0d);
            int amount = Permille.Apply(ctx.Self.MaxHp, permille);
            if (amount <= 0) return EffectOutcome.None;

            var u = ctx.Self;
            int applied = amount < u.Hp ? amount : u.Hp;
            u.Hp -= applied;

            // 보호막을 거치지 않는다. 자기 체력을 태우는 게 이 효과의 값이라
            // 방어 수단으로 막히면 대가가 사라진다.
            ctx.Sink.Emit(new GameEvent(EventKind.Damage, ctx.Tick, u.Id, u.Id, applied, u.Hp));
            if (!u.IsAlive) ctx.Sink.Emit(new GameEvent(EventKind.Death, ctx.Tick, u.Id, u.Id, 0));

            return EffectOutcome.None;
        }

        private static EffectOutcome ApplyStatus(JObject e, in EffectContext ctx, int skillPower)
        {
            var kind = ParseStatus((string?)e["kind"]);
            if (kind == StatusKind.None) return EffectOutcome.None;

            var targets = Resolve((string?)e["target"], ctx);
            if (targets.Count == 0) return EffectOutcome.None;

            int duration = (int?)e["duration"] ?? 0;
            int expire = duration > 0 ? ctx.Tick + duration : StatusEffect.Never;

            foreach (var t in targets)
            {
                if (!t.IsAlive) continue;

                if (kind == StatusKind.Shield)
                {
                    // 보호막만 전용 경로로 간다 — 상한·초과분 처리가 붙어 있다.
                    int gain = Permille.Apply(t.MaxHp,
                        Permille.Apply((int?)e["gainPermille"] ?? 0, skillPower));
                    int max = Permille.Apply(t.MaxHp, (int?)e["maxPermille"] ?? 0);

                    DamageResolver.GrantShield(t, gain, max, (bool?)e["overflowToHp"] ?? false,
                                               ctx.Self.Id, ctx.Tick, ctx.Sink);
                    continue;
                }

                t.Status.Apply(new StatusEffect(kind, expire,
                    ValueA(kind, e, skillPower), ValueB(kind, e), ctx.Self.Id, ctx.Tick));

                ctx.Sink.Emit(new GameEvent(EventKind.StatusApply, ctx.Tick, ctx.Self.Id, t.Id,
                    (int)kind, expire));
            }

            return EffectOutcome.None;
        }

        /// <remarks>의미는 <see cref="StatusEffect.ValueA"/> 주석의 표와 같다.</remarks>
        private static int ValueA(StatusKind kind, JObject e, int skillPower)
        {
            switch (kind)
            {
                case StatusKind.Weaken:
                    return Permille.FromMultiplier((double?)e["attackMult"] ?? 1d);
                case StatusKind.Slow:
                    return Permille.FromMultiplier((double?)e["moveIntervalMult"] ?? 1d);
                case StatusKind.Regen:
                    return Permille.Apply((int?)e["healPermille"] ?? 0, skillPower);
                case StatusKind.DotRamping:
                    return Permille.Apply((int?)e["baseValue"] ?? 0, skillPower);
                case StatusKind.Taunt:
                    return Permille.FromMultiplier((double?)e["threatMult"] ?? 1d);
                default:
                    return 0;
            }
        }

        private static int ValueB(StatusKind kind, JObject e)
        {
            switch (kind)
            {
                case StatusKind.Weaken:
                    // damageTakenMult 가 없으면 0 을 둔다. StatusSet 이 0 을 "보정 없음"으로 읽는다.
                    return e["damageTakenMult"] != null
                        ? Permille.FromMultiplier((double)e["damageTakenMult"]!) : 0;
                case StatusKind.Regen:
                    return (int?)e["everyTicks"] ?? 0;
                case StatusKind.DotRamping:
                    return Permille.FromMultiplier((double?)e["rampPerTick"] ?? 0d);
                default:
                    return 0;
            }
        }

        private static StatusKind ParseStatus(string? name)
        {
            switch (name)
            {
                case "weaken": return StatusKind.Weaken;
                case "dot_ramping": return StatusKind.DotRamping;
                case "invulnerable_first_hit": return StatusKind.Invulnerable;
                case "regen": return StatusKind.Regen;
                case "slow": return StatusKind.Slow;
                case "root": return StatusKind.Root;
                case "shield": return StatusKind.Shield;
                case "taunt": return StatusKind.Taunt;
                default: return StatusKind.None;
            }
        }

        private static readonly Unit[] NoTargets = new Unit[0];

        /// <summary>
        /// 대상 지정을 실제 유닛 목록으로 푼다 (`_schema` §3).
        /// </summary>
        /// <remarks>
        /// <c>mainSkill</c> 은 여기서 처리하지 않는다 — 그건 유닛이 아니라
        /// <see cref="SkillResolver.ResolveSkillPower"/> 가 다루는 가상 대상이다.
        /// </remarks>
        public static IReadOnlyList<Unit> Resolve(string? target, in EffectContext ctx)
        {
            switch (target)
            {
                case null:
                case "self":
                    return new[] { ctx.Self };

                case "allies":
                    return Alive(ctx.Team, null);

                case "allies_except_self":
                    return Alive(ctx.Team, ctx.Self);

                case "lowestHpAlly":
                {
                    var pick = LowestHp(ctx.Team);
                    return pick == null ? NoTargets : new[] { pick };
                }

                case "enemy":
                    return ctx.Target != null && ctx.Target.IsAlive ? new[] { ctx.Target } : NoTargets;

                case "all_enemies":
                    return Alive(ctx.Foes, null);

                default:
                    return NoTargets;
            }
        }

        private static IReadOnlyList<Unit> Alive(IReadOnlyList<Unit> units, Unit? except)
        {
            List<Unit>? result = null;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (!u.IsAlive || ReferenceEquals(u, except)) continue;
                (result ??= new List<Unit>()).Add(u);
            }
            return (IReadOnlyList<Unit>?)result ?? NoTargets;
        }

        /// <summary>
        /// HP <b>비율</b>이 가장 낮은 아군. 동률이면 슬롯 인덱스 오름차순.
        /// </summary>
        /// <remarks>
        /// 절대값이 아니라 비율인 이유 — 체력 180 인 수도승과 80 인 주술사를 절대값으로 비교하면
        /// <b>항상 주술사만 회복된다</b> (`_schema` §3).
        /// </remarks>
        private static Unit? LowestHp(IReadOnlyList<Unit> units)
        {
            Unit? best = null;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (!u.IsAlive) continue;
                if (best == null || u.HpPermille < best.HpPermille
                    || (u.HpPermille == best.HpPermille && u.Id < best.Id))
                {
                    best = u;
                }
            }
            return best;
        }
    }
}
