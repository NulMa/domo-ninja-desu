// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System.Collections.Generic;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Events;
using DomoNinja.Core.Rng;
using Newtonsoft.Json.Linq;

namespace DomoNinja.Core.Economy
{
    /// <summary>라운드 1회의 결과.</summary>
    public sealed class RoundOutcome
    {
        public int Round { get; }
        public string VariantId { get; }
        public bool Won { get; }
        public int Ticks { get; }

        /// <summary>
        /// 틱 × 유닛 수. <b>처리량 실측의 분모다</b> (`17` §7.1 — 유닛-틱당 5µs 게이트).
        /// </summary>
        /// <remarks>
        /// 틱만 세면 유닛 3체 전투와 9체 전투가 같은 비용으로 잡힌다.
        /// 최적화 여부를 판단할 단위는 "한 유닛을 한 틱 처리하는 비용"이라 여기서 곱해 둔다.
        /// </remarks>
        public int UnitTicks { get; }

        /// <summary>이 라운드에서 얻은 <b>런 재화</b>. 영구 재화가 아니다.</summary>
        public int CurrencyGained { get; }

        /// <summary>
        /// 라운드 <b>진입 시점</b> 아군 HP 총합의 천분율. `M2`(진입 HP% 구간별 패배율)가 쓴다.
        /// </summary>
        /// <remarks>
        /// 종료 시점이 아니라 진입 시점이어야 한다 — `M2` 가 보려는 건
        /// <b>"체력이 얼마 남았을 때 이 라운드가 위험해지는가"</b> 이고, 그건 들어갈 때 정해진다.
        /// 끝나고 재면 진 라운드는 늘 0 에 가까워서 구간이 무너진다.
        /// </remarks>
        public int EntryHpPermille { get; }

        /// <summary>
        /// 서든데스까지 갔는가. `M7`(타임아웃 도달률, 5% 미만)이 센다.
        /// </summary>
        /// <remarks>
        /// 서든데스는 <b>예외로 남아야</b> 하는 장치다. 흔해지면 그건 전투가 안 끝난다는 뜻이고,
        /// `C5-A` 각인처럼 장기전을 노리는 빌드가 구조적으로 불리해진다(`D-61`).
        /// </remarks>
        public bool TimedOut { get; }

        /// <summary>재생용 로그. 켰을 때만 있다.</summary>
        public BattleLog? Log { get; }

        public RoundOutcome(int round, string variantId, bool won, int ticks, int unitTicks,
                            int currencyGained, int entryHpPermille, bool timedOut, BattleLog? log)
        {
            Round = round; VariantId = variantId; Won = won;
            Ticks = ticks; UnitTicks = unitTicks; CurrencyGained = currencyGained;
            EntryHpPermille = entryHpPermille; TimedOut = timedOut; Log = log;
        }

        public override string ToString() => $"R{Round}({VariantId}) {(Won ? "승" : "패")} {Ticks}틱";
    }

    /// <summary>런 1회의 결과. <c>sim</c> 이 지표를 뽑는 원자료다.</summary>
    public sealed class RunSummary
    {
        /// <summary>이긴 라운드 수. <b>클리어율(`M1`)의 분자가 아니라 진행도다.</b></summary>
        public int RoundsWon { get; }

        /// <summary>마지막으로 도달한 라운드. 패배해도 진행은 계속되므로 <see cref="RoundsWon"/> 과 다르다.</summary>
        public int RoundsReached { get; }

        /// <summary>8라운드를 전부 통과했는가. `M1` 이 세는 값이다.</summary>
        public bool Cleared { get; }

        public int LivesLeft { get; }

        /// <summary>전투에 쓴 총 틱. `M5`(1런 3~5분) 계산에 들어간다.</summary>
        public int TotalTicks { get; }

        /// <summary>틱 x 유닛 수의 합. 처리량 실측(`17` §7.1)이 쓴다.</summary>
        public int TotalUnitTicks { get; }

        /// <summary>
        /// 첫 액티브 스킬을 활성화한 라운드. 끝까지 하나도 못 샀으면 <b>0</b>. `M6` 이 쓴다.
        /// </summary>
        /// <remarks>
        /// `M6` 은 저축 전략(`08` §4.3)이 실제로 작동하는지를 본다 —
        /// 전부 같은 라운드에 사면 <b>"지금 살까 아껴둘까"라는 긴장이 없다는 뜻</b>이고,
        /// 그건 상점 랜덤성이 의사결정을 만들지 못하고 있다는 신호다.
        /// </remarks>
        public int FirstActivationRound { get; }

        public IReadOnlyList<RoundOutcome> Rounds { get; }

        public RunSummary(int roundsWon, int roundsReached, bool cleared, int livesLeft,
                          int totalTicks, int totalUnitTicks, int firstActivationRound,
                          IReadOnlyList<RoundOutcome> rounds)
        {
            RoundsWon = roundsWon; RoundsReached = roundsReached; Cleared = cleared;
            LivesLeft = livesLeft; TotalTicks = totalTicks; TotalUnitTicks = totalUnitTicks;
            FirstActivationRound = firstActivationRound; Rounds = rounds;
        }

        public override string ToString() =>
            $"{(Cleared ? "클리어" : "실패")} {RoundsWon}/{RoundsReached}승 생명{LivesLeft} {TotalTicks}틱";
    }

    /// <summary>
    /// 런 하나를 굴린다 — 라운드 진행 · 생명 · HP 누적 · 회복 · 재화.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>난수 스트림을 나눠 쓴다.</b> 관문 변형 추첨은 <see cref="RngStream.Encounter"/> 에서만 뽑는다.
    /// 상점과 같은 스트림을 쓰면 <b>전투에서 소비 횟수가 한 번만 달라져도 이후 상점이 통째로 밀린다.</b>
    /// 그러면 파라미터를 바꿨을 때 관측된 차이가 파라미터 때문인지 난수가 밀린 건지 구분할 수 없고,
    /// CRN(`17` §6)으로 분산을 줄이려는 설계가 그 지점에서 무너진다.
    /// </para>
    /// <para>
    /// 스테이지별 관문은 <see cref="GameData.RoundsFor"/> 가 고른다.
    /// `economy.stages` 는 `stage1`/`stage2` 를 가리키지만 스테이지 2 저작은 팀원 작업분(`19` §6.7)이라
    /// 아직 없다. 그래서 지금은 스테이지 id 가 결과에 영향을 주지 않는다 — 저작이 들어오면 여기서 갈라진다.
    /// </para>
    /// </remarks>
    public sealed class RunEngine
    {
        private readonly GameData _data;
        private readonly CombatConfig _config;

        public RunEngine(GameData data, CombatConfig config)
        {
            _data = data;
            _config = config;
        }

        /// <summary>출전 캐릭터를 정해 런을 시작한다.</summary>
        /// <param name="characterIds">
        /// 출전 순서. <b>이 순서가 슬롯 인덱스가 되고 모든 동률 판정의 최종 기준이 된다.</b>
        /// </param>
        public RunState StartRun(string stageId, IReadOnlyList<string> characterIds, MetaProgress meta)
        {
            var roster = new List<RosterEntry>(characterIds.Count);

            foreach (string id in characterIds)
            {
                var character = _data.FindCharacter(id);
                if (character == null) continue;

                // 시작 체력에 메타 강화를 반영한다. 라운드마다 다시 계산하지 않고
                // 여기서 한 번 정하는 이유 — 최대 체력이 도중에 바뀌면 "누적된 HP"의 기준이 흔들린다.
                var stats = Skills.SkillResolver.BuildStats(character, null);
                var sum = new ModifierSum();
                sum.AddDeltaPermille(meta.DeltaPermilleFor(Skills.StatKey.Hp));

                roster.Add(new RosterEntry(character.Id, sum.ApplyTo(stats.Hp)));
            }

            return new RunState(stageId, _data.Economy.Lives, StartingCurrency(), roster);
        }

        private int StartingCurrency()
        {
            var currency = _data.Economy.Raw["currency"] as JObject;
            return (int?)currency?["startingAmount"] ?? 0;
        }

        /// <summary>라운드 하나를 치른다. 결과는 <paramref name="run"/> 에 반영된다.</summary>
        public RoundOutcome PlayRound(RunState run, MetaProgress meta, DeterministicRandom rng,
                                      IEventSink sink, bool collectLog = false)
        {
            var round = FindRound(run.StageId, run.Round);
            var variant = PickVariant(round, rng);

            // 진입 시점에 재둔다. 전투가 끝난 뒤에는 이 값을 복원할 수 없다.
            int entryHp = EntryHpPermille(run);

            var units = BattleSetup.Build(_data, run, variant, meta);
            var result = new BattleSimulator(_config).Run(
                units, sink, StageNumber(run.StageId), run.Round, 0, collectLog);

            CarryHpBack(run, units);

            bool won = result.Outcome == BattleOutcome.AllyWin;
            int gained = won
                ? _data.Economy.CurrencyOnWin + meta.CurrencyOnWinBonus
                : _data.Economy.CurrencyOnLose;

            run.Currency += gained;

            if (won) HealOnWin(run, meta);
            else run.Lives--;

            run.Round++;

            // 서든데스 진입 여부. 전투가 타임아웃을 넘겼다는 것과 같은 말이다 —
            // BattleSimulator 가 그 시점에 SuddenDeath 를 내지만, 로그를 끈 sim 에서는
            // 이벤트를 볼 수 없으므로 틱으로 판정한다.
            bool timedOut = result.Ticks > _config.TimeoutTicks;

            return new RoundOutcome(round.Round, variant.Id, won, result.Ticks,
                                    result.Ticks * units.Count, gained, entryHp, timedOut, result.Log);
        }

        /// <summary>액티브를 하나라도 산 캐릭터가 있는가.</summary>
        private static bool AnyActiveBought(RunState run)
        {
            foreach (var entry in run.Deployed)
                if (entry.ActiveSkillId != null) return true;
            return false;
        }

        /// <summary>살아 있는 아군의 HP 총합 / 최대 HP 총합, 천분율.</summary>
        /// <remarks>
        /// 죽은 유닛의 최대 체력도 분모에 넣는다. 빼면 <b>전멸 직전인데 비율이 100% 로 보이는</b>
        /// 상황이 생긴다 — 한 명만 남아 만피면 그렇게 된다. `M2` 가 보려는 건 팀 전체의 여력이다.
        /// </remarks>
        private static int EntryHpPermille(RunState run)
        {
            int hp = 0, max = 0;
            foreach (var entry in run.Deployed)
            {
                hp += entry.Hp;
                max += entry.MaxHp;
            }

            return max <= 0 ? 0 : hp * 1000 / max;
        }

        /// <summary>런 하나를 끝까지 굴린다.</summary>
        /// <remarks>
        /// 생명이 0 이 되면 <b>즉시 끝낸다</b>. 남은 라운드를 마저 도는 건
        /// 결과에 영향을 주지 않으면서 시뮬 시간만 먹는다 — 4,320 빌드를 도는 설계에서 그건 그대로 비용이다.
        /// </remarks>
        /// <param name="build">
        /// 봇이 목표로 삼을 빌드. <c>null</c> 이면 상점에 들르지 않는다(스킬 없이 맨몸으로 돈다).
        /// </param>
        public RunSummary PlayRun(RunState run, MetaProgress meta, DeterministicRandom rng,
                                  IEventSink sink, bool collectLogs = false, BuildTarget? build = null)
        {
            var outcomes = new List<RoundOutcome>();
            int totalTicks = 0;
            int totalUnitTicks = 0;
            int won = 0;

            // ★ 스트림을 라운드 루프 밖에서 한 번만 가른다.
            //   안에서 매번 Fork 하면 같은 시드에서 같은 스트림이 반복돼
            //   매 라운드 같은 변형·같은 재고가 나온다.
            var encounterRng = rng.Fork(RngStream.Encounter);
            var shopRng = rng.Fork(RngStream.Shop);
            var shop = build == null ? null : new Shop(_data);
            int firstActivation = 0;

            while (run.Round <= _data.Economy.TotalRounds && !run.IsOver)
            {
                // 상점은 전투 "전"이다. 라운드 시작 전 배치·구매가 개입 지점이고(A-8),
                // 이번 라운드에 산 스킬이 이번 전투부터 걸려야 한다.
                if (shop != null) ShopBot.Visit(run, shop, build!, shopRng, run.Round);

                if (firstActivation == 0 && AnyActiveBought(run)) firstActivation = run.Round;

                var outcome = PlayRound(run, meta, encounterRng, sink, collectLogs);
                outcomes.Add(outcome);

                totalTicks += outcome.Ticks;
                totalUnitTicks += outcome.UnitTicks;
                if (outcome.Won) won++;
            }

            int reached = outcomes.Count;
            bool cleared = won == _data.Economy.TotalRounds;

            return new RunSummary(won, reached, cleared, run.Lives, totalTicks, totalUnitTicks,
                                  firstActivation, outcomes);
        }

        /// <summary>
        /// 라운드 승리 회복 — <b>1층</b>. 재화를 쓰지 않는 기본 안전망이다 (`_schema` §9).
        /// </summary>
        /// <remarks>
        /// 패배 시 회복이 없어야 연패가 실제 위험이 되고 생명 3 시스템이 유명무실해지지 않는다
        /// (`economy.healing.roundEnd._why`).
        /// </remarks>
        private void HealOnWin(RunState run, MetaProgress meta)
        {
            var healing = _data.Economy.Raw["healing"] as JObject;
            var roundEnd = healing?["roundEnd"] as JObject;
            int permille = ((int?)roundEnd?["healOnWinPermille"] ?? 0) + meta.RoundEndHealBonusPermille;
            if (permille <= 0) return;

            foreach (var entry in run.Deployed)
            {
                // 사망 유닛은 회복 대상이 아니다 (A6 — 부활 없음).
                if (!entry.IsAlive) continue;

                int healed = entry.Hp + Permille.Apply(entry.MaxHp, permille);
                entry.Hp = healed > entry.MaxHp ? entry.MaxHp : healed;
            }
        }

        /// <summary>전투 결과 HP 를 런 상태로 되돌린다. <b>이게 A-6(HP 누적)의 구현이다.</b></summary>
        private static void CarryHpBack(RunState run, IReadOnlyList<Unit> units)
        {
            foreach (var unit in units)
            {
                if (unit.Team != Team.Ally) continue;

                foreach (var entry in run.Deployed)
                {
                    if (entry.CharacterId != unit.TypeId) continue;
                    entry.Hp = unit.Hp;
                    break;
                }
            }
        }

        /// <summary>그 스테이지의 관문에서 라운드를 찾는다.</summary>
        private RoundDef FindRound(string stageId, int number)
        {
            var rounds = _data.RoundsFor(stageId);

            foreach (var r in rounds)
                if (r.Round == number) return r;

            // 검증 규칙 R12 가 1~8 을 빠짐없이 덮는지 이미 확인한다. 여기 도달하면 그건 코드 문제다.
            return rounds[rounds.Count - 1];
        }

        /// <summary>변형 추첨. <b>관문 스트림에서만 뽑는다.</b></summary>
        private static VariantDef PickVariant(RoundDef round, DeterministicRandom rng) =>
            round.Variants.Count == 1
                ? round.Variants[0]
                : round.Variants[rng.NextInt(round.Variants.Count)];

        private static int StageNumber(string stageId) =>
            stageId.Length > 1 && stageId[0] == 'S' && stageId[1] >= '1' && stageId[1] <= '9'
                ? stageId[1] - '0'
                : 1;
    }

}
