// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using DomoNinja.Core.Domain;
using DomoNinja.Core.Events;

namespace DomoNinja.Core.Combat
{
    /// <summary>
    /// 피해·회복·보호막을 적용한다. <b>순서가 규칙이다</b> (`_schema` §3 shield).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 계약은 세 줄이고 순서를 바꾸면 결과가 달라진다:
    /// <code>
    /// 피해 발생 → ① 보호막에서 먼저 차감 → ② 남은 피해를 HP 에서 차감
    /// 회복 발생 → HP 만 회복한다. 보호막은 회복 대상이 아니다
    /// overflowToHp → 초과분을 HP 에 더하되 최대 체력을 넘지 않는다
    /// </code>
    /// </para>
    /// <para>
    /// ★ <b>보호막은 <c>damageTaken</c> 배율을 적용한 뒤의 피해를 흡수한다.</b>
    /// 순서를 뒤집어 원본 피해를 먼저 흡수시키면 방어 스킬이 있는 유닛일수록
    /// 보호막이 더 오래 버티게 되어, 두 방어 수단이 곱해진다. 그건 합연산 규칙(§8)이 막으려던 것이다.
    /// </para>
    /// <para>
    /// ★ <b>모든 상태 변화가 이벤트로 나간다.</b> unity 는 여기서 나온 값을 그대로 그리고
    /// 자기가 빼거나 더하지 않는다 — 그러지 않으면 피해 규칙이 View 에 복제된다(`23` §2.1).
    /// </para>
    /// </remarks>
    public static class DamageResolver
    {
        /// <summary>
        /// 피해를 적용한다. <b>실제로 깎인 총량</b>(보호막 + HP)을 돌려준다.
        /// </summary>
        /// <remarks>
        /// 반환값이 필요한 이유 — 흡혈(<c>fromDamagePermille</c>)이 "방금 발생한 피해"의 천분율이라
        /// 계산이 끝난 실제 값을 알아야 한다. 요청한 피해량으로 계산하면
        /// 보호막에 막혔거나 절삭된 만큼 흡혈이 부풀려진다.
        /// </remarks>
        public static int ApplyDamage(Unit target, int rawDamage, int actorId, int tick, IEventSink sink)
        {
            if (!target.IsAlive || rawDamage <= 0) return 0;

            // 첫 피격 무효 (C3-A 그림자). 소모되고 사라진다.
            if (target.Status.Has(StatusKind.Invulnerable))
            {
                target.Status.Remove(StatusKind.Invulnerable);
                sink.Emit(new GameEvent(EventKind.StatusExpire, tick, -1, target.Id, (int)StatusKind.Invulnerable));

                // ⚠️ 로그 포맷 v1 에 "회피" 이벤트가 없어서 피해 0 으로 표현한다.
                //    View 는 Damage.Value == 0 을 보고 무효 연출을 낼 수 있다.
                //    → D+4 포맷 리뷰(19 §4.2)에 올릴 후보다. 동결 전이라 지금은 포맷을 건드리지 않는다.
                sink.Emit(new GameEvent(EventKind.Damage, tick, actorId, target.Id, 0, target.Hp));
                return 0;
            }

            // 받는 피해 보정 — 스킬·아이템(고정)과 weaken(가변)을 전부 더한 뒤 한 번만 적용한다.
            var taken = new ModifierSum();
            taken.AddDeltaPermille(target.DamageTakenDeltaPermille);
            taken.AddDeltaPermille(target.Status.DamageTakenDeltaPermille);

            int damage = taken.ApplyTo(rawDamage);
            if (damage <= 0) return 0;

            int dealt = 0;

            // ① 보호막에서 먼저
            if (target.Shield > 0)
            {
                int absorbed = damage < target.Shield ? damage : target.Shield;
                target.Shield -= absorbed;
                damage -= absorbed;
                dealt += absorbed;

                sink.Emit(new GameEvent(EventKind.Shield, tick, actorId, target.Id, -absorbed, target.Shield));

                if (target.Shield == 0 && target.Status.Remove(StatusKind.Shield))
                    sink.Emit(new GameEvent(EventKind.StatusExpire, tick, -1, target.Id, (int)StatusKind.Shield));
            }

            // ② 남은 피해를 HP 에서
            if (damage > 0)
            {
                int applied = damage < target.Hp ? damage : target.Hp;
                target.Hp -= applied;
                dealt += applied;
            }

            sink.Emit(new GameEvent(EventKind.Damage, tick, actorId, target.Id, dealt, target.Hp));

            if (!target.IsAlive)
                sink.Emit(new GameEvent(EventKind.Death, tick, actorId, target.Id, 0));

            return dealt;
        }

        /// <summary>
        /// 회복. <b>HP 만 회복한다</b> — 보호막은 회복 대상이 아니다. 실제 회복량을 돌려준다.
        /// </summary>
        /// <remarks>
        /// 사망 유닛에는 적용되지 않는다 (`A6` — 부활 없음. 상점 부활 아이템도 없다).
        /// 여기서 막지 않으면 광역 회복이 시체를 일으키는 회복 스킬이 된다.
        /// </remarks>
        public static int ApplyHeal(Unit target, int amount, int actorId, int tick, IEventSink sink)
        {
            if (!target.IsAlive || amount <= 0) return 0;

            int room = target.MaxHp - target.Hp;
            if (room <= 0) return 0;

            int healed = amount < room ? amount : room;
            target.Hp += healed;

            sink.Emit(new GameEvent(EventKind.Heal, tick, actorId, target.Id, healed, target.Hp));
            return healed;
        }

        /// <summary>
        /// 보호막 부여. 상한을 넘으면 <paramref name="overflowToHp"/> 에 따라 HP 로 가거나 버려진다.
        /// </summary>
        /// <param name="maxShield">상한(절대값). 천분율은 호출부에서 이미 풀어서 넘긴다.</param>
        public static void GrantShield(Unit target, int amount, int maxShield, bool overflowToHp,
                                       int actorId, int tick, IEventSink sink)
        {
            if (!target.IsAlive || amount <= 0) return;

            int before = target.Shield;
            int after = before + amount;
            int overflow = 0;

            if (after > maxShield)
            {
                overflow = after - maxShield;
                after = maxShield;
            }

            if (after != before)
            {
                target.Shield = after;
                sink.Emit(new GameEvent(EventKind.Shield, tick, actorId, target.Id, after - before, after));

                target.Status.Apply(new StatusEffect(
                    StatusKind.Shield, StatusEffect.Never, valueA: maxShield, sourceUnitId: actorId));
                sink.Emit(new GameEvent(EventKind.StatusApply, tick, actorId, target.Id,
                    (int)StatusKind.Shield, StatusEffect.Never));
            }

            // 초과분을 체력으로 (C3-A 그림자 · C5-P2 결계). 최대 체력은 넘지 않는다.
            if (overflow > 0 && overflowToHp)
                ApplyHeal(target, overflow, actorId, tick, sink);
        }

        /// <summary>
        /// 라운드 종료. <b>보호막은 사라지고 HP 만 누적된다</b> (`A-6`).
        /// </summary>
        /// <remarks>
        /// 보호막이 라운드를 넘어가면 "안 쓰고 아끼는" 전략이 생기는데,
        /// 이 게임의 개입 지점은 배치와 상점뿐이라(A-8) 플레이어가 그걸 조절할 수단이 없다.
        /// 조절할 수 없는 자원이 누적되면 결과가 운으로 갈린다.
        /// </remarks>
        public static void ClearShield(Unit target, int tick, IEventSink sink)
        {
            if (target.Shield > 0)
            {
                sink.Emit(new GameEvent(EventKind.Shield, tick, -1, target.Id, -target.Shield, 0));
                target.Shield = 0;
            }

            if (target.Status.Remove(StatusKind.Shield))
                sink.Emit(new GameEvent(EventKind.StatusExpire, tick, -1, target.Id, (int)StatusKind.Shield));
        }
    }
}
