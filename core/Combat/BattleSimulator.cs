// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System.Collections.Generic;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Events;
using DomoNinja.Core.Skills;
using Newtonsoft.Json.Linq;

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
        /// <summary>
        /// 한 번의 공격이 파생 공격을 일으킬 수 있는 최대 횟수.
        /// </summary>
        /// <remarks>
        /// <c>recast</c>(처치 시 다음 대상 재발동)와 <c>extra_attack</c>(처치 시 즉시 재공격)이
        /// 서로를 부르면 이론상 끝나지 않는다. 데이터의 <c>maxChain</c> 은 3 이지만
        /// <b>상한을 데이터에만 맡기지 않는다</b> — 값 하나 잘못 넣으면 sim 수만 런 중 하나가 멈추고,
        /// 그러면 CI 가 통째로 걸린다.
        /// </remarks>
        private const int MaxAttackChain = 8;

        private readonly CombatConfig _config;
        private readonly List<StatusKind> _expiredScratch = new List<StatusKind>();
        private readonly List<CompiledTrigger> _triggerScratch = new List<CompiledTrigger>();
        private readonly List<Unit> _aoeScratch = new List<Unit>();
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

            bool anyConditional = false;

            foreach (var u in units)
            {
                (u.Team == Team.Ally ? _allies : _enemies).Add(u);
                board.TryPlace(u.Id, u.At);
                header?.Add(new UnitSpec(u.Id, (int)u.Team, u.TypeId, u.MaxHp, u.At.OrderKey));

                // 조건부 강화를 아무도 안 들었으면 매 틱 도는 패스를 통째로 건너뛴다.
                // 4,320 빌드 × 5시드를 도는 sim 에서 유닛 순회 한 겹은 그대로 처리량이다.
                anyConditional |= u.ConditionalBoosts.Count > 0;
            }

            ApplyStartEffects(units, target);

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

                // 유닛이 움직이기 전에 전부 갱신한다. StepUnits 안에서 유닛마다 재면
                // 슬롯 0 번이 적을 죽인 순간 슬롯 1 번의 조건이 같은 틱에 이미 바뀐다.
                if (anyConditional) RefreshConditionalBoosts(units);

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
            RemoveCorpses(units, board);

            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (!u.IsAlive) continue;

                _expiredScratch.Clear();
                u.Status.ExpireAt(tick, _expiredScratch);
                foreach (var kind in _expiredScratch)
                    sink.Emit(new GameEvent(EventKind.StatusExpire, tick, -1, u.Id, (int)kind));

                TickStatuses(u, tick, sink);
                FirePeriodic(u, tick, sink);
                if (!u.IsAlive) continue;   // 주기 효과가 자기 체력을 태워 죽을 수 있다 (C2-B 파동)

                var foes = u.Team == Team.Ally ? _enemies : _allies;
                var targetUnit = Targeting.SelectTarget(u, foes);
                if (targetUnit == null) continue;

                bool inRange = u.InRangeOf(targetUnit);

                if (!inRange) TryStep(u, targetUnit, board, tick, sink);

                // ★ 이동 판정 **뒤**다. "제자리인가" 는 이번 틱에 움직였는지까지 포함한 질문이라,
                //   앞에 두면 걸어갈 유닛에게 보호막을 줬다가 같은 틱에 도로 걷어가게 된다.
                //   공격보다는 앞이므로 이번 틱에 맞을 피해는 그대로 막는다.
                SustainShields(u, tick, sink);

                // 이동한 뒤 사거리에 들어왔을 수 있다. 같은 틱에 이동과 공격이 모두 일어난다.
                if (u.InRangeOf(targetUnit)) TryAttack(u, targetUnit, tick, sink);
                else if (u.AttackCooldown > 0) u.AttackCooldown--;
            }
        }

        /// <summary>
        /// 죽은 유닛이 점유하던 칸을 비운다. <b>규칙: 틱 시작 시점에 죽어 있으면 보드에 없다.</b>
        /// </summary>
        /// <remarks>
        /// <para>
        /// ★ <b>이게 없으면 시체가 벽이 된다.</b> <see cref="Board.StepToward"/> 는 경로 탐색이 아니라
        /// 그리디 1스텝이고, 막히면 <see cref="TryStep"/> 이 쿨다운도 안 쓰고 그 틱을 포기한다.
        /// 자기 상태가 하나도 안 바뀌므로 <b>다음 틱에 똑같이 실패한다</b> — 스스로는 못 빠져나온다.
        /// 게다가 진영이 x축으로 갈려 있어(<see cref="Coord.AllyMaxX"/>) 초반엔 거의 항상
        /// <c>|dx| ≥ |dy|</c> 다. 즉 <b>첫 사망자들의 시체가 접전선에 세로 벽으로 쌓이고</b>,
        /// 벽 뒤 유닛은 비켜갈 조건(<c>|dy| &gt; |dx|</c>)을 만들려면 dx 를 줄여야 하는데
        /// 그 dx 를 못 줄여서 막힌 상태다. 표적이 먼저 움직이지 않는 한 영구다.
        /// </para>
        /// <para>
        /// ⚠️ <b>죽은 그 순간이 아니라 다음 틱 시작에 치워진다.</b> 슬롯 0 이 죽인 시체는
        /// 그 틱 동안 슬롯 1~N 을 막는다. 즉시 치우려면 <see cref="DamageResolver"/> 가
        /// 보드를 알아야 하는데, 그러면 피해 계산이 배치를 참조하게 된다 — 경계를 그쪽으로 넘기지 않는다.
        /// <b>1틱(0.05초) 지연은 결정론적이므로 규칙으로 못박는다.</b>
        /// </para>
        /// <para>
        /// 여기 한 곳에 모아둔 이유는 사망 경로가 넷이기 때문이다 — 피격·주기 효과 자멸(C2-B)·
        /// 지속 피해·서든데스. <c>continue</c> 자리마다 흩어놓으면 새 경로가 생겼을 때 빠진다.
        /// <see cref="ApplySuddenDeath"/> 가 이 함수보다 먼저 도는 것도 그래서 괜찮다.
        /// </para>
        /// </remarks>
        private static void RemoveCorpses(IReadOnlyList<Unit> units, Board board)
        {
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u.IsAlive) continue;

                // 소유 확인은 Board.Remove 안에 있다 — 죽은 유닛의 At 는 죽은 자리를 계속
                // 가리키고, 그 칸엔 이미 산 유닛이 들어와 있을 수 있다.
                board.Remove(u.Id, u.At);
            }
        }

        /// <summary>전투 시작 시 한 번 거는 것들. 팀·적 대상 <c>status</c> 가 여기서 걸린다.</summary>
        /// <remarks>
        /// 유닛을 전부 배치한 <b>뒤에</b> 부른다. <c>allies</c>·<c>all_enemies</c> 가 대상을 찾으려면
        /// 양쪽 목록이 이미 채워져 있어야 하기 때문이다.
        /// </remarks>
        private void ApplyStartEffects(IReadOnlyList<Unit> units, IEventSink sink)
        {
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                var start = u.Loadout.StartEffects;
                if (start.Count == 0) continue;

                var ctx = MakeContext(u, null, 0, 0, sink);
                for (int k = 0; k < start.Count; k++)
                    EffectExecutor.Execute(start[k].Effect, ctx, start[k].SkillPowerPermille);
            }
        }

        /// <summary>
        /// 시간이 지나면서 스스로 일하는 상태이상 — <c>regen</c> 과 <c>dot_ramping</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 이 둘은 <b>걸리는 것만으로는 아무 일도 안 한다.</b> 걸어두고 여기서 돌리지 않으면
        /// <c>C6-B</c> 가호와 <c>C5-A</c> 각인이 <b>상태 아이콘만 뜨고 효과가 없는</b> 스킬이 된다.
        /// </para>
        /// <para>
        /// ⚠️ <c>dot_ramping</c> 의 수치 해석은 확정된 게 아니다.
        /// <c>baseValue: 6, rampPerTick: 0.4, duration: 200</c> 에서 「매 초 적용, 피해 = base + ramp × 경과틱」
        /// 으로 읽었는데, 그러면 10초 동안 누적 약 460 이라 적 체력(40~260)에 비해 크다.
        /// 매 틱 적용으로 읽으면 20배가 되어 확실히 과하다. 둘 중 덜 나간 쪽을 택했고,
        /// <b>수치는 밸런스 루프가 조정할 대상</b>이다 — 구조가 아니라 값의 문제다.
        /// </para>
        /// </remarks>
        private void TickStatuses(Unit u, int tick, IEventSink sink)
        {
            if (u.Status.Count == 0) return;

            if (u.Status.TryGet(StatusKind.Regen, out var regen) && regen.ValueB > 0
                && tick > 0 && tick % regen.ValueB == 0)
            {
                // healPermille 은 대상 최대 체력의 천분율이다 (`_schema` §3).
                int amount = Permille.Apply(u.MaxHp, regen.ValueA);
                DamageResolver.ApplyHeal(u, amount, regen.SourceUnitId, tick, sink);
            }

            if (u.Status.TryGet(StatusKind.DotRamping, out var dot)
                && tick > 0 && tick % _config.ApplyEveryTicks == 0)
            {
                int elapsed = tick - dot.AppliedTick;
                int amount = dot.ValueA + Permille.Apply(elapsed, dot.ValueB);
                if (amount > 0)
                    DamageResolver.ApplyDamage(u, amount, dot.SourceUnitId, tick, sink);
            }
        }

        /// <summary>
        /// 조건부 강화(아이템 <c>conditionalBoost</c>)를 <b>틱 시작에 한 번</b> 다시 잰다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 세 조건이 전부 전투 중에 뒤집힌다 — 체력은 깎이고, 적은 죽고, 아군도 죽는다.
        /// 그래서 다른 아이템처럼 시작 스탯에 접을 수 없다.
        /// </para>
        /// <para>
        /// ★ <b>쓰는 자리가 아니라 여기서 잰다.</b> 공격 계산·피해 계산 시점에 그때그때 평가하면
        /// 같은 틱 안에서도 <b>행동 순서에 따라 값이 갈린다</b> — 슬롯 0 번이 마지막 적을 죽이면
        /// 슬롯 1 번의 <c>enemies_above</c> 가 그 틱에 이미 꺼져 있다.
        /// 데이터에 안 적혀 있고 로그로도 안 보이는 규칙이 생기는 자리다.
        /// </para>
        /// </remarks>
        private void RefreshConditionalBoosts(IReadOnlyList<Unit> units)
        {
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                var boosts = u.ConditionalBoosts;
                if (boosts.Count == 0) continue;

                bool ally = u.Team == Team.Ally;
                var mine = ally ? _allies : _enemies;
                var theirs = ally ? _enemies : _allies;

                int attack = 0, taken = 0;

                for (int k = 0; k < boosts.Count; k++)
                {
                    var b = boosts[k];
                    if (!b.IsActive(u, mine, theirs)) continue;

                    // 합연산 통에 들어간다 (`_schema` §8). 조건부만 따로 곱하면
                    // 조건이 겹칠수록 폭발해 M4 지배 빌드가 생긴다.
                    if (b.Stat == Skills.StatKey.Attack) attack += b.DeltaPermille;
                    else if (b.Stat == Skills.StatKey.DamageTaken) taken += b.DeltaPermille;
                }

                u.ConditionalAttackDeltaPermille = attack;
                u.ConditionalDamageTakenDeltaPermille = taken;
            }
        }

        /// <summary>
        /// 한 번 걸고 끝나지 않는 보호막 — <c>refreshEveryTicks</c> 와 <c>whileStationary</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>regen</c>·<c>dot_ramping</c> 과 같은 계열의 함정이다. <b>걸리기는 하는데 그 뒤가 없었다</b> —
        /// 전투 시작에 한 번 주고 끝나서 <c>C3-P2</c> 잔영(*"10초마다"*)이 <b>1회용 보호막</b>이었다.
        /// 화면에도 로그에도 "보호막이 걸렸다"고 나오기 때문에 <b>텍스트를 읽어보기 전에는 안 드러난다.</b>
        /// </para>
        /// <para>
        /// ★ <b>둘의 리듬이 다르다.</b> 주기형은 <i>얼마마다 채우는가</i>이고 제자리형은 <i>켜져 있는가</i>다.
        /// 제자리형을 주기형처럼 매 틱 채우면 <b>0.05초마다 상한까지 회복</b>이라
        /// 사실상 무적이 된다 — 같은 코드로 접으면 안 되는 이유가 이것이다.
        /// </para>
        /// </remarks>
        private void SustainShields(Unit u, int tick, IEventSink sink)
        {
            var lo = u.Loadout;
            var refreshing = lo.RefreshingShields;
            if (refreshing.Count == 0 && lo.StationaryShield == null) return;

            var ctx = MakeContext(u, null, 0, tick, sink);

            // ① 주기 재충전. 시작 1회는 ApplyStartEffects 가 이미 걸었으므로 tick > 0 이다.
            for (int i = 0; i < refreshing.Count; i++)
            {
                var s = refreshing[i];
                if (tick > 0 && tick % s.EveryTicks == 0)
                    EffectExecutor.Execute(s.Effect, ctx, s.SkillPowerPermille);
            }

            // ② 제자리 유지. 켜는 것만 여기서 하고, 끄는 건 실제로 움직인 자리(TryStep)에서 한다.
            if (lo.StationaryShield is SustainedShield st && !u.StationaryShieldOn && IsStationary(u, tick))
            {
                EffectExecutor.Execute(st.Effect, ctx, st.SkillPowerPermille);
                u.StationaryShieldOn = true;
            }
        }

        /// <summary>
        /// 제자리에 있는가 — <b>움직일 수 있게 된 뒤에도 안 갔는가</b>로 본다.
        /// </summary>
        /// <remarks>
        /// ★ *"직전 틱에 안 움직였다"* 로 읽으면 안 된다. 이동 간격이 12틱이라
        /// <b>전진 중인 유닛도 11/12 틱은 정지 상태</b>여서 걸어가는 내내 보호막이 켜져 있게 된다.
        /// 그러면 <c>C4-P2</c> 참호의 대가(이동 속도 −40%)만 내고 조건은 사실상 없는 스킬이 된다.
        /// <b>움직일 차례가 왔는데 안 갔다</b>는 것이 제자리다 — 그 시점이
        /// <see cref="Unit.MoveReadyTick"/> 에 적혀 있다.
        /// </remarks>
        private static bool IsStationary(Unit u, int tick)
            => u.MoveReadyTick < 0 || tick > u.MoveReadyTick;

        /// <summary>주기 트리거(<c>every_n_tick</c>).</summary>
        private void FirePeriodic(Unit u, int tick, IEventSink sink)
        {
            if (u.Loadout.Triggers.Count == 0) return;

            _triggerScratch.Clear();
            u.Loadout.Triggers.CollectPeriodic(tick, _triggerScratch);
            if (_triggerScratch.Count == 0) return;

            var ctx = MakeContext(u, null, 0, tick, sink);
            for (int i = 0; i < _triggerScratch.Count; i++)
            {
                var t = _triggerScratch[i];
                EffectExecutor.Execute(t.Effect, ctx, t.SkillPowerPermille);
            }
        }

        /// <summary>사건 트리거를 터뜨린다. 추가 행동 요청이 있으면 합쳐서 돌려준다.</summary>
        private EffectOutcome FireEvent(Unit u, TriggerType type, Unit? target, int lastDamage,
                                        int tick, IEventSink sink)
        {
            var triggers = u.Loadout.Triggers.Matching(type);
            if (triggers.Count == 0) return EffectOutcome.None;

            var ctx = MakeContext(u, target, lastDamage, tick, sink);
            int extra = 0, chain = 0;

            for (int i = 0; i < triggers.Count; i++)
            {
                var outcome = EffectExecutor.Execute(triggers[i].Effect, ctx, triggers[i].SkillPowerPermille);
                extra += outcome.ExtraAttacks;
                if (outcome.RecastChain > chain) chain = outcome.RecastChain;
            }

            return new EffectOutcome(extra, chain);
        }

        private EffectContext MakeContext(Unit u, Unit? target, int lastDamage, int tick, IEventSink sink)
        {
            bool ally = u.Team == Team.Ally;
            return new EffectContext(u, target, lastDamage, tick,
                                     ally ? _allies : _enemies,
                                     ally ? _enemies : _allies,
                                     sink);
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

            // 제자리 조건이 여기서 깨진다. 자기가 준 양만큼만 거둔다 — 아군에게 받은 몫은 남는다.
            if (u.StationaryShieldOn && u.Loadout.StationaryShield is SustainedShield st)
            {
                u.StationaryShieldOn = false;
                DamageResolver.RevokeShield(u, Permille.Apply(u.MaxHp, st.MaxPermille), tick, sink);
            }

            // slow 는 간격을 늘린다. 합연산으로 더한 뒤 한 번만 적용한다.
            var interval = new ModifierSum();
            interval.AddDeltaPermille(u.Status.MoveIntervalDeltaPermille);
            int cooldown = interval.ApplyTo(u.MoveInterval);
            u.MoveCooldown = cooldown < 1 ? 1 : cooldown;

            // 쿨다운은 검사하는 틱마다 1 씩 줄어드므로 실제로 다시 걸을 수 있는 건 그 다음 틱이다.
            u.MoveReadyTick = tick + u.MoveCooldown + 1;

            sink.Emit(new GameEvent(EventKind.Move, tick, u.Id, -1, next.OrderKey, from.OrderKey));
        }

        private void TryAttack(Unit u, Unit target, int tick, IEventSink sink)
        {
            if (u.AttackCooldown > 0)
            {
                u.AttackCooldown--;
                return;
            }

            PerformAttack(u, target, tick, sink, depth: 0);

            u.AttackCooldown = u.AttackInterval < 1 ? 1 : u.AttackInterval;
        }

        /// <summary>
        /// 공격 1회. <b>파생 공격(<c>extra_attack</c>·<c>recast</c>)까지 여기서 끝난다.</b>
        /// </summary>
        /// <remarks>
        /// 재귀를 쓰되 <paramref name="depth"/> 로 상한을 건다.
        /// <see cref="EffectExecutor"/> 가 실행 대신 요청만 돌려주게 만든 이유가 이것이다 —
        /// <b>멈추는 지점이 이 함수 하나에만 있다.</b> 효과 쪽에서 직접 공격을 부르면
        /// 상한이 데이터에 흩어지고, 값 하나 잘못 넣으면 sim 이 조용히 멈춘다.
        /// </remarks>
        private void PerformAttack(Unit u, Unit target, int tick, IEventSink sink, int depth)
        {
            if (depth >= MaxAttackChain || !u.IsAlive || !target.IsAlive) return;

            sink.Emit(new GameEvent(EventKind.Attack, tick, u.Id, target.Id, 0));

            var attack = new ModifierSum();
            attack.AddDeltaPermille(u.Status.AttackDeltaPermille);
            attack.AddDeltaPermille(u.ConditionalAttackDeltaPermille);
            int baseDamage = attack.ApplyTo(u.Attack);

            var aoe = u.Loadout.Aoe;
            int totalDealt = 0;
            bool anyKill = false;
            bool anyHit = false;

            if (aoe == null)
            {
                var r = Strike(u, target, baseDamage, tick, sink);
                totalDealt += r.Dealt;
                anyKill |= r.Killed;
                anyHit |= r.Dealt > 0;
            }
            else
            {
                _aoeScratch.Clear();
                int splashDamage = CollectAoeTargets(u, target, aoe, baseDamage, _aoeScratch);

                for (int i = 0; i < _aoeScratch.Count; i++)
                {
                    var victim = _aoeScratch[i];

                    // 주 표적은 온전히 맞고, 파급 대상만 배율이 걸린다.
                    int amount = ReferenceEquals(victim, target) ? baseDamage : splashDamage;
                    var r = Strike(u, victim, amount, tick, sink);

                    totalDealt += r.Dealt;
                    anyKill |= r.Killed;
                    anyHit |= r.Dealt > 0;
                }
            }

            // target: "enemy" 인 상태이상 — 때린 상대에게 건다 (C5-A 각인).
            var onAttack = u.Loadout.OnAttackEffects;
            if (anyHit && onAttack.Count > 0)
            {
                var ctx = MakeContext(u, target, totalDealt, tick, sink);
                for (int i = 0; i < onAttack.Count; i++)
                    EffectExecutor.Execute(onAttack[i].Effect, ctx, onAttack[i].SkillPowerPermille);
            }

            // on_hit — 자신의 공격이 적중한 순간. 흡혈·표식·둔화가 여기 걸린다.
            var outcome = anyHit
                ? FireEvent(u, TriggerType.OnHit, target, totalDealt, tick, sink)
                : EffectOutcome.None;

            if (anyKill)
            {
                var onKill = FireEvent(u, TriggerType.OnKill, target, totalDealt, tick, sink);
                outcome = new EffectOutcome(outcome.ExtraAttacks + onKill.ExtraAttacks,
                                            outcome.RecastChain > onKill.RecastChain
                                                ? outcome.RecastChain : onKill.RecastChain);
            }

            if (outcome.IsNone) return;

            var foes = u.Team == Team.Ally ? _enemies : _allies;

            // extra_attack — 즉시 한 번 더.
            // ★ 표적이 죽었으면 새로 고른다. C1-A 일격의 트리거가 on_kill 이라
            //   재공격 시점에는 그 표적이 이미 죽어 있다 — 시체를 다시 치게 두면
            //   "처치 시 즉시 재공격"이 아무 일도 안 하는 스킬이 된다.
            for (int i = 0; i < outcome.ExtraAttacks; i++)
            {
                var victim = target.IsAlive ? target : Targeting.SelectTarget(u, foes);
                if (victim == null) break;
                PerformAttack(u, victim, tick, sink, depth + 1);
            }

            // recast — 다음 표적으로 옮겨간다. 처치했을 때만 의미가 있다 (C5-B 연쇄).
            if (outcome.RecastChain > 0 && anyKill)
            {
                var next = Targeting.SelectTarget(u, foes);
                if (next != null && !ReferenceEquals(next, target))
                    PerformAttack(u, next, tick, sink, depth + 1);
            }
        }

        /// <summary>한 대상에게 피해를 넣고, 그쪽 트리거(<c>on_damaged</c>·<c>on_dodge</c>)를 터뜨린다.</summary>
        private DamageResult Strike(Unit attacker, Unit victim, int amount, int tick, IEventSink sink)
        {
            var result = DamageResolver.ApplyDamage(victim, amount, attacker.Id, tick, sink);

            if (result.Dodged)
                FireEvent(victim, TriggerType.OnDodge, attacker, 0, tick, sink);
            else if (result.Dealt > 0)
                FireEvent(victim, TriggerType.OnDamaged, attacker, result.Dealt, tick, sink);

            return result;
        }

        /// <summary>
        /// 광역 대상을 모으고, <b>파급 대상에 쓸 피해량</b>을 돌려준다 (`_schema` §3 <c>aoe</c>).
        /// </summary>
        /// <remarks>
        /// <c>damageSource: maxHp</c> 면 공격력이 아니라 <b>시전자 최대 체력</b>에서 나온다 (C2-B 파동).
        /// 그 경우 주 표적도 같은 값을 맞으므로 파급 배율이 곧 전체 피해다.
        /// </remarks>
        private int CollectAoeTargets(Unit u, Unit primary, JObject aoe, int baseDamage, List<Unit> into)
        {
            var foes = u.Team == Team.Ally ? _enemies : _allies;
            string? scope = (string?)aoe["scope"];
            double value = (double?)aoe["value"] ?? 0d;

            bool fromMaxHp = (string?)aoe["damageSource"] == "maxHp";
            int splash = fromMaxHp
                ? Permille.Apply(u.MaxHp, Permille.FromMultiplier(value))
                : Permille.Apply(baseDamage, Permille.FromMultiplier(value));

            switch (scope)
            {
                case "all_enemies":
                    for (int i = 0; i < foes.Count; i++)
                        if (foes[i].IsAlive) into.Add(foes[i]);

                    // 전체 광역은 주 표적도 같은 값을 맞는다 — 혼자만 다른 피해를 받을 이유가 없다.
                    return splash;

                case "multi_target":
                {
                    // 가까운 순으로 targetCount 명. 동률은 슬롯 인덱스로 끊는다.
                    int count = (int?)aoe["targetCount"] ?? 1;
                    into.Add(primary);
                    AddNearest(u, foes, primary, count - 1, into);

                    // 표적당 위력 감소는 stat_mult 로 이미 걸려 있다 (C4-B 난사 -50%).
                    return baseDamage;
                }

                case "radius":
                {
                    int sqr = (int)(value * value);
                    into.Add(primary);
                    for (int i = 0; i < foes.Count; i++)
                    {
                        var f = foes[i];
                        if (!f.IsAlive || ReferenceEquals(f, primary)) continue;
                        if (primary.At.SqrDistanceTo(f.At) <= sqr) into.Add(f);
                    }
                    return splash;
                }

                default:   // adjacent — 주 표적에 인접한 8칸
                {
                    into.Add(primary);
                    for (int i = 0; i < foes.Count; i++)
                    {
                        var f = foes[i];
                        if (!f.IsAlive || ReferenceEquals(f, primary)) continue;
                        if (primary.At.SqrDistanceTo(f.At) <= 2) into.Add(f);
                    }
                    return splash;
                }
            }
        }

        /// <summary>주 표적을 뺀 나머지 중 가까운 순으로 <paramref name="count"/> 명.</summary>
        private static void AddNearest(Unit u, IReadOnlyList<Unit> foes, Unit primary, int count, List<Unit> into)
        {
            for (int picked = 0; picked < count; picked++)
            {
                Unit? best = null;
                for (int i = 0; i < foes.Count; i++)
                {
                    var f = foes[i];
                    if (!f.IsAlive || ReferenceEquals(f, primary) || into.Contains(f)) continue;

                    if (best == null
                        || u.At.SqrDistanceTo(f.At) < u.At.SqrDistanceTo(best.At)
                        || (u.At.SqrDistanceTo(f.At) == u.At.SqrDistanceTo(best.At) && f.Id < best.Id))
                    {
                        best = f;
                    }
                }

                if (best == null) return;
                into.Add(best);
            }
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
