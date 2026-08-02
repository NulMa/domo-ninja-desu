// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System.Collections.Generic;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Events;

namespace DomoNinja.Core.Combat
{
    /// <summary>전투 결과. 무승부는 없다 — 양측 동시 전멸은 <see cref="AllyLoss"/> 다 (`D-54`).</summary>
    public enum BattleOutcome
    {
        AllyWin = 1,
        AllyLoss = 0,
    }

    /// <summary>한 전투의 결과와 그동안 쌓인 로그.</summary>
    public sealed class BattleResult
    {
        public BattleOutcome Outcome { get; }

        /// <summary>전투가 끝난 틱. 20틱 = 1초.</summary>
        public int Ticks { get; }

        /// <summary>재생용 로그. <see cref="NullEventSink"/> 로 돌렸으면 <c>null</c> 이다.</summary>
        public BattleLog? Log { get; }

        public BattleResult(BattleOutcome outcome, int ticks, BattleLog? log)
        {
            Outcome = outcome;
            Ticks = ticks;
            Log = log;
        }

        public override string ToString() => $"{Outcome} @{Ticks}틱";
    }

    /// <summary>
    /// 전투 1회를 끝까지 돌린다. <b>이 클래스가 결정론의 마지막 관문이다.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 매 틱 <b>슬롯 인덱스 오름차순</b>으로 고정 순회하며 유닛마다 <b>이동 → 공격</b> 순으로 처리한다
    /// (`_schema` §7.1). 이 두 가지 순서가 이 파일에서 지켜야 할 전부다 —
    /// 순회 순서를 바꾸거나 이동/공격을 뒤집으면 같은 시드가 다른 결과를 낸다.
    /// </para>
    /// <para>
    /// ★ <b>난수를 쓰지 않는다.</b> 전투에는 확률 판정이 없다 — 회피도 치명타도 없고
    /// 첫 피격 무효(<c>invulnerable_first_hit</c>)조차 확정 효과다.
    /// 그래서 전투 스트림(<see cref="Rng.RngStream.Combat"/>)은 아직 소비되지 않는다.
    /// 나중에 확률이 들어오면 <b>반드시 그 스트림에서 뽑는다</b> — 상점 스트림을 건드리면
    /// 전투에서 난수 소비 횟수가 한 번만 달라져도 이후 상점이 통째로 밀린다.
    /// </para>
    /// </remarks>
    public sealed class BattleSimulator
    {
        private readonly CombatConfig _config;
        private readonly List<StatusKind> _expiredScratch = new List<StatusKind>();
        private readonly List<Unit> _allies = new List<Unit>();
        private readonly List<Unit> _enemies = new List<Unit>();

        public BattleSimulator(CombatConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// 전투를 끝까지 돌린다.
        /// </summary>
        /// <param name="units">아군·적 전부. <b>슬롯 인덱스(Id) 오름차순으로 들어와야 한다.</b></param>
        /// <param name="sink">
        /// 이벤트를 받을 곳. <c>sim</c> 은 <see cref="NullEventSink"/> 를 넘겨 로그를 끈다 —
        /// 그게 처리량의 전제다(`19` §2.5).
        /// </param>
        /// <param name="collectLog">
        /// 로그를 <see cref="BattleLog"/> 로 묶어 돌려줄지. 재생이 필요할 때만 켠다.
        /// </param>
        public BattleResult Run(IReadOnlyList<Unit> units, IEventSink sink,
                                int stage = 1, int round = 1, ulong seed = 0, bool collectLog = false)
        {
            var recorder = collectLog ? new ListEventSink() : null;
            IEventSink target = recorder == null ? sink : new TeeEventSink(sink, recorder);

            var header = collectLog ? new List<UnitSpec>(units.Count) : null;

            _allies.Clear();
            _enemies.Clear();
            var board = new Board();

            foreach (var u in units)
            {
                (u.Team == Team.Ally ? _allies : _enemies).Add(u);
                board.TryPlace(u.Id, u.At);
                header?.Add(new UnitSpec(u.Id, (int)u.Team, u.TypeId, u.MaxHp, u.At.OrderKey));
            }

            int tick = 0;
            int suddenDeathAt = -1;
            BattleOutcome outcome;

            while (true)
            {
                // 서든데스 진입은 전투당 한 번이다.
                if (suddenDeathAt < 0 && tick > _config.TimeoutTicks)
                {
                    suddenDeathAt = tick;
                    target.Emit(new GameEvent(EventKind.SuddenDeath, tick, -1, -1, 0));
                }

                if (suddenDeathAt >= 0) ApplySuddenDeath(units, tick, suddenDeathAt, target);

                // 서든데스가 전멸을 만들 수 있으므로 여기서 한 번 본다.
                if (TryDecide(out outcome)) break;

                StepUnits(units, board, tick, target);

                if (TryDecide(out outcome)) break;

                tick++;

                // 서든데스는 최대 체력 비율 피해라 반드시 누군가 죽는다. 그래도 상한을 둔다 —
                // 무한 루프는 sim 수만 런 중 하나만 걸려도 CI 가 통째로 멈춘다.
                if (tick > _config.TimeoutTicks + _config.RampTicks * 4)
                {
                    outcome = BattleOutcome.AllyLoss;
                    break;
                }
            }

            target.Emit(new GameEvent(EventKind.RoundEnd, tick, -1, -1, (int)outcome));

            BattleLog? log = recorder == null || header == null
                ? null
                : new BattleLog(stage, round, seed, header, recorder.Events);

            return new BattleResult(outcome, tick, log);
        }

        /// <summary>매 틱의 본체. <b>슬롯 인덱스 오름차순, 유닛별 이동 → 공격.</b></summary>
        private void StepUnits(IReadOnlyList<Unit> units, Board board, int tick, IEventSink sink)
        {
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (!u.IsAlive) continue;

                _expiredScratch.Clear();
                u.Status.ExpireAt(tick, _expiredScratch);
                foreach (var kind in _expiredScratch)
                    sink.Emit(new GameEvent(EventKind.StatusExpire, tick, -1, u.Id, (int)kind));

                var foes = u.Team == Team.Ally ? _enemies : _allies;
                var targetUnit = Targeting.SelectTarget(u, foes);
                if (targetUnit == null) continue;

                bool inRange = u.InRangeOf(targetUnit);

                if (!inRange) TryStep(u, targetUnit, board, tick, sink);

                // 이동한 뒤 사거리에 들어왔을 수 있다. 같은 틱에 이동과 공격이 모두 일어난다.
                if (u.InRangeOf(targetUnit)) TryAttack(u, targetUnit, tick, sink);
                else if (u.AttackCooldown > 0) u.AttackCooldown--;
            }
        }

        /// <summary>
        /// 1칸 이동. 예외는 둘뿐이다 — <c>root</c> 상태와 <c>immobile</c> 적 (`_schema` §7.1).
        /// </summary>
        private void TryStep(Unit u, Unit target, Board board, int tick, IEventSink sink)
        {
            if (u.Immobile) return;
            if (!u.Status.CanMove) return;

            if (u.MoveCooldown > 0)
            {
                u.MoveCooldown--;
                return;
            }

            var next = Board.StepToward(u.At, target.At);
            if (next.Equals(u.At)) return;

            // 목표 칸이 차 있으면 그 틱은 정지한다. 쿨다운도 소비하지 않는다 —
            // 소비하면 막혀 있는 동안 이동 기회가 계속 사라져 벽 뒤 유닛이 영영 못 붙는다.
            if (!board.TryMove(u.Id, u.At, next)) return;

            var from = u.At;
            u.At = next;

            // slow 는 간격을 늘린다. 합연산으로 더한 뒤 한 번만 적용한다.
            var interval = new ModifierSum();
            interval.AddDeltaPermille(u.Status.MoveIntervalDeltaPermille);
            int cooldown = interval.ApplyTo(u.MoveInterval);
            u.MoveCooldown = cooldown < 1 ? 1 : cooldown;

            sink.Emit(new GameEvent(EventKind.Move, tick, u.Id, -1, next.OrderKey, from.OrderKey));
        }

        private void TryAttack(Unit u, Unit target, int tick, IEventSink sink)
        {
            if (u.AttackCooldown > 0)
            {
                u.AttackCooldown--;
                return;
            }

            sink.Emit(new GameEvent(EventKind.Attack, tick, u.Id, target.Id, 0));

            var attack = new ModifierSum();
            attack.AddDeltaPermille(u.Status.AttackDeltaPermille);
            DamageResolver.ApplyDamage(target, attack.ApplyTo(u.Attack), u.Id, tick, sink);

            u.AttackCooldown = u.AttackInterval < 1 ? 1 : u.AttackInterval;
        }

        /// <summary>
        /// 서든데스 — <b>매 초, 최대 체력의 천분율만큼 고정 피해</b> (`08` §5.3).
        /// </summary>
        /// <remarks>
        /// 최대 체력 비율이라 탱커도 같은 속도로 죽는다. <b>서든데스가 빌드에 중립적</b>이라는 뜻이고,
        /// 그게 이 설계의 좋은 성질이다 — 특정 빌드만 타임아웃에 강하면 `M3b` 가 깨진다.
        ///
        /// 기본값은 아군 한정이다(`A1`). 적이 아군보다 약하게 구성되므로 양측에 걸면
        /// 적이 먼저 죽어 "반드시 클리어되는" 상황이 생기고, 크랙플레이 차단이라는 목적이 무너진다.
        /// </remarks>
        private void ApplySuddenDeath(IReadOnlyList<Unit> units, int tick, int startTick, IEventSink sink)
        {
            int elapsed = tick - startTick;
            if (elapsed % _config.ApplyEveryTicks != 0) return;

            int permille = _config.RampPermille(
                elapsed, _config.FixedDamageStartPermille, _config.FixedDamageMaxPermille);

            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (!u.IsAlive) continue;
                if (_config.SuddenDeathAllyOnly && u.Team != Team.Ally) continue;

                // 정수 절삭 때문에 최대 체력이 아주 작으면 0 이 나온다 (상한 300‰ 에서도 hp 3 이하).
                // 그런 유닛은 서든데스로 죽지 않게 되므로 최소 1 을 보장한다.
                // ⚠️ 이 절삭이 "서든데스는 빌드 중립적"을 정확히는 성립하지 않게 만든다 —
                //    체력이 클수록 잃는 비율이 작아 조금 빨리 죽는다. 실측 편차 1초(테스트에 기록).
                int damage = Permille.Apply(u.MaxHp, permille);
                if (damage <= 0) damage = 1;

                // 보호막을 거치지 않는다. 고정 피해라 방어 수단으로 미룰 수 없어야
                // "20초 내 종료"가 성립한다.
                int applied = damage < u.Hp ? damage : u.Hp;
                u.Hp -= applied;
                sink.Emit(new GameEvent(EventKind.Damage, tick, -1, u.Id, applied, u.Hp));

                if (!u.IsAlive) sink.Emit(new GameEvent(EventKind.Death, tick, -1, u.Id, 0));
            }
        }

        /// <summary>
        /// 승패 판정. <b>양측 동시 전멸은 플레이어 패배다</b> (`D-54`).
        /// </summary>
        /// <remarks>
        /// 무승부를 만들면 `M1`(클리어율)·`M2` 집계에 세 번째 상태가 생겨 지표가 지저분해진다.
        /// 그리고 거기까지 간 시점에 이미 플레이어가 이기지 못한 것이다.
        /// </remarks>
        private bool TryDecide(out BattleOutcome outcome)
        {
            bool allyAlive = AnyAlive(_allies);
            bool enemyAlive = AnyAlive(_enemies);

            if (allyAlive && enemyAlive)
            {
                outcome = BattleOutcome.AllyLoss;
                return false;
            }

            outcome = allyAlive ? BattleOutcome.AllyWin : BattleOutcome.AllyLoss;
            return true;
        }

        private static bool AnyAlive(List<Unit> units)
        {
            for (int i = 0; i < units.Count; i++)
                if (units[i].IsAlive) return true;
            return false;
        }
    }

    /// <summary>두 싱크에 같은 이벤트를 흘린다. 로그를 모으면서 바깥 싱크도 살려둘 때 쓴다.</summary>
    internal sealed class TeeEventSink : IEventSink
    {
        private readonly IEventSink _a, _b;

        public TeeEventSink(IEventSink a, IEventSink b)
        {
            _a = a;
            _b = b;
        }

        public void Emit(in GameEvent e)
        {
            _a.Emit(e);
            _b.Emit(e);
        }
    }
}
