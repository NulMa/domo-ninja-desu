using System.Collections.Generic;
using System.Linq;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Events;
using NUnit.Framework;

namespace DomoNinja.Core.Tests
{
    /// <summary>틱 루프 · 이동 · 서든데스 (`_schema` §7.1 · `08` §5.3).</summary>
    [TestFixture]
    public class BattleSimulatorTests
    {
        private static CombatConfig Config()
        {
            // ★ 테스트가 실제 economy.json 을 읽는다. 상수를 테스트에 박으면
            //   최적화기가 값을 바꿨을 때 코드와 데이터가 갈라져도 초록으로 남는다.
            var data = GameDataLoader.Load(RepoData.Characters, RepoData.Skills,
                                           RepoData.Encounters, RepoData.Economy, RepoData.Meta);
            return CombatConfig.From(data.Economy, tickRate: 20);
        }

        private static Unit Ally(int id, int x, int y, int hp = 100, int atk = 10,
                                 int atkInterval = 20, int range = 1, int moveInterval = 5) =>
            new Unit(id, Team.Ally, "C1", hp, atk, atkInterval, range, moveInterval, new Coord(x, y));

        private static Unit Enemy(int id, int x, int y, int hp = 100, int atk = 10,
                                  int atkInterval = 20, int range = 1, int moveInterval = 5,
                                  bool immobile = false) =>
            new Unit(id, Team.Enemy, "slime", hp, atk, atkInterval, range, moveInterval,
                     new Coord(x, y), immobile);

        [Test]
        public void 적이_없으면_즉시_승리다()
        {
            var sim = new BattleSimulator(Config());
            var result = sim.Run(new[] { Ally(0, 1, 2) }, NullEventSink.Instance);

            Assert.That(result.Outcome, Is.EqualTo(BattleOutcome.AllyWin));
            Assert.That(result.Ticks, Is.EqualTo(0));
        }

        [Test]
        public void 접근해서_때리고_이긴다()
        {
            var sim = new BattleSimulator(Config());
            var sink = new ListEventSink();

            var result = sim.Run(new[] { Ally(0, 0, 2, hp: 500, atk: 30), Enemy(1, 6, 2, hp: 60, atk: 1) },
                                 sink);

            Assert.That(result.Outcome, Is.EqualTo(BattleOutcome.AllyWin));
            Assert.That(sink.Events.Any(e => e.Kind == EventKind.Move), Is.True, "붙으려면 움직여야 한다");
            Assert.That(sink.Events.Any(e => e.Kind == EventKind.Death && e.TargetId == 1), Is.True);
        }

        [Test]
        public void 사거리_안에_표적이_있으면_이동하지_않는다()
        {
            // _schema §7.1 — "붙어야만 때리는 게 아니다".
            var sim = new BattleSimulator(Config());
            var sink = new ListEventSink();

            var archer = Ally(0, 0, 2, hp: 500, atk: 30, range: 7);
            sim.Run(new[] { archer, Enemy(1, 6, 2, hp: 60, atk: 0, immobile: true) }, sink);

            Assert.That(sink.Events.Any(e => e.Kind == EventKind.Move && e.ActorId == 0), Is.False);
        }

        [Test]
        public void root_가_걸린_유닛은_이동하지_않는다()
        {
            var sim = new BattleSimulator(Config());
            var sink = new ListEventSink();

            var rooted = Ally(0, 0, 2, hp: 500);
            rooted.Status.Apply(new StatusEffect(StatusKind.Root, StatusEffect.Never));

            sim.Run(new[] { rooted, Enemy(1, 6, 2, hp: 40, atk: 0, immobile: true) }, sink);

            Assert.That(sink.Events.Any(e => e.Kind == EventKind.Move && e.ActorId == 0), Is.False);
            Assert.That(rooted.At, Is.EqualTo(new Coord(0, 2)));
        }

        [Test]
        public void immobile_인_적은_제자리에서만_싸운다()
        {
            // A5 고정포대. 접근을 강제하는 관문 장치다.
            var sim = new BattleSimulator(Config());
            var sink = new ListEventSink();

            var totem = Enemy(1, 6, 2, hp: 90, atk: 5, range: 6, immobile: true);
            sim.Run(new[] { Ally(0, 0, 2, hp: 500, atk: 30), totem }, sink);

            Assert.That(sink.Events.Any(e => e.Kind == EventKind.Move && e.ActorId == 1), Is.False);
            Assert.That(totem.At, Is.EqualTo(new Coord(6, 2)));
        }

        [Test]
        public void 막힌_칸_뒤의_유닛도_결국_붙는다()
        {
            // 이동에 실패해도 쿨다운을 소비하지 않는 이유. 소비하면 막혀 있는 동안
            // 이동 기회가 계속 사라져 벽 뒤 유닛이 영영 못 붙는다.
            var sim = new BattleSimulator(Config());

            var front = Ally(0, 2, 2, hp: 500, atk: 20);
            var behind = Ally(1, 1, 2, hp: 500, atk: 20);
            var result = sim.Run(new[] { front, behind, Enemy(2, 5, 2, hp: 400, atk: 1) },
                                 NullEventSink.Instance);

            Assert.That(result.Outcome, Is.EqualTo(BattleOutcome.AllyWin));
            Assert.That(behind.At.X, Is.GreaterThan(1), "뒤에 선 유닛도 전진했어야 한다");
        }

        // ────────────────────────────── 서든데스

        [Test]
        public void 서든데스는_전투당_한_번만_진입한다()
        {
            var sim = new BattleSimulator(Config());
            var sink = new ListEventSink();

            // 서로 닿지도 때리지도 못하는 구성 → 반드시 타임아웃까지 간다.
            sim.Run(new[] { Ally(0, 0, 0, hp: 100, atk: 0, range: 0),
                            Enemy(1, 7, 5, hp: 100, atk: 0, range: 0, immobile: true) },
                    sink);

            Assert.That(sink.Events.Count(e => e.Kind == EventKind.SuddenDeath), Is.EqualTo(1));
        }

        [Test]
        public void 만피_유닛은_서든데스_후_약_13초에_죽는다()
        {
            // 08 §5.3 이 적분으로 약 13.4초를 계산해뒀다. 그 계산이 정수 나눗셈 구현과
            // 실제로 맞는지 보는 테스트다. 어긋나면 "20초 내 종료"라는 의도가 깨진다.
            var config = Config();
            var sim = new BattleSimulator(config);
            var sink = new ListEventSink();

            var ally = Ally(0, 0, 0, hp: 100, atk: 0, range: 0);
            var result = sim.Run(new[] { ally, Enemy(1, 7, 5, hp: 100, atk: 0, range: 0, immobile: true) },
                                 sink);

            double seconds = (result.Ticks - config.TimeoutTicks) / (double)config.TickRate;

            Assert.That(seconds, Is.InRange(12.0, 15.0),
                $"서든데스 발동 후 {seconds:F1}초에 죽었다 — 08 §5.3 의 13.4초 예측과 어긋난다");
        }

        [Test]
        public void 서든데스는_최대체력이_달라도_거의_같은_속도로_죽인다()
        {
            // 08 §5.3 의 주장: "고정 데미지가 최대체력 비율이라 탱커도 같은 속도로 죽는다
            // — 서든데스가 빌드에 중립적이다". 특정 빌드만 타임아웃에 강하면 M3b 가 깨진다.
            //
            // ★ 실측 결과 정확히 같지는 않다. 정수 절삭 때문이다.
            //   maxHp * permille / 1000 에서 체력이 클수록 절삭으로 잃는 비율이 작아
            //   실효 피해가 조금 더 크다. 측정값(2026-08-02):
            //     hp 40~260 → 1181틱 / hp 500·620 → 1161틱   (편차 20틱 = 1초)
            //   서든데스 지속이 ~14초이므로 7% 이고, 방향이 "체력이 클수록 조금 빨리"라
            //   탱커에게 유리하지 않다. 스펙의 의도는 상하지 않으므로 1초를 허용 오차로 둔다.
            //   이걸 0 으로 만들려면 유닛마다 잔여 피해 누적 상태를 들어야 하는데,
            //   7% 를 없애려고 결정론 상태를 늘리는 건 남는 장사가 아니다.
            var config = Config();
            var sim = new BattleSimulator(config);

            int TicksToDie(int maxHp)
            {
                var u = Ally(0, 0, 0, hp: maxHp, atk: 0, range: 0);
                return sim.Run(new[] { u, Enemy(1, 7, 5, hp: 100, atk: 0, range: 0, immobile: true) },
                               NullEventSink.Instance).Ticks;
            }

            // 40 = 가장 약한 적(slime), 620 = 가장 강한 보스(giantFrog). 실제 사용 범위 전체다.
            int[] hps = { 40, 80, 100, 120, 180, 260, 500, 620 };
            var ticks = hps.Select(TicksToDie).ToArray();
            int spread = ticks.Max() - ticks.Min();

            Assert.That(spread, Is.LessThanOrEqualTo(config.TickRate),
                "체력에 따른 서든데스 사망 시각 편차가 1초를 넘었다 — 빌드 중립성이 깨진다: "
                + string.Join(" / ", hps.Zip(ticks, (h, t) => $"hp{h}:{t}")));
        }

        [Test]
        public void 서든데스_램프는_상한에서_멈춘다()
        {
            var config = Config();

            int atStart = config.RampPermille(0, 10, 300);
            int atRamp = config.RampPermille(config.RampTicks, 10, 300);
            int beyond = config.RampPermille(config.RampTicks * 5, 10, 300);

            Assert.That(atStart, Is.EqualTo(10));
            Assert.That(atRamp, Is.EqualTo(300));
            Assert.That(beyond, Is.EqualTo(300), "상한을 넘어 계속 오르면 안 된다");
        }

        // ────────────────────────────── 결정론 · 로그

        [Test]
        public void 같은_입력을_두_번_돌리면_이벤트가_완전히_같다()
        {
            // 🔴 CI 필수 항목. 이게 깨지면 밸런스 수치 전체가 의미를 잃는다.
            var config = Config();

            IReadOnlyList<GameEvent> RunOnce()
            {
                var sink = new ListEventSink();
                new BattleSimulator(config).Run(new[]
                {
                    Ally(0, 1, 1, hp: 120, atk: 20),
                    Ally(1, 0, 3, hp: 90, atk: 14, range: 4, moveInterval: 3),
                    Enemy(2, 5, 1, hp: 75, atk: 12),
                    Enemy(3, 6, 4, hp: 45, atk: 13, range: 4, moveInterval: 10),
                }, sink);
                return sink.Events;
            }

            var a = RunOnce();
            var b = RunOnce();

            Assert.That(b.Count, Is.EqualTo(a.Count));
            for (int i = 0; i < a.Count; i++)
                Assert.That(b[i].ToString(), Is.EqualTo(a[i].ToString()), $"{i} 번째 이벤트가 갈렸다");
        }

        [Test]
        public void 로그는_23_의_계약을_지킨다()
        {
            var sim = new BattleSimulator(Config());
            var result = sim.Run(new[] { Ally(0, 0, 2, hp: 300, atk: 40), Enemy(1, 5, 2, hp: 50, atk: 5) },
                                 NullEventSink.Instance, stage: 1, round: 3, seed: 77, collectLog: true);

            var log = result.Log!;
            Assert.That(log.Version, Is.EqualTo(BattleLog.FormatVersion));
            Assert.That(log.Units.Count, Is.EqualTo(2));
            Assert.That(log.Events[log.Events.Count - 1].Kind, Is.EqualTo(EventKind.RoundEnd),
                "마지막은 항상 RoundEnd 다 — View 의 재생 종료 신호가 이것뿐이다");

            for (int i = 1; i < log.Events.Count; i++)
                Assert.That(log.Events[i].Tick, Is.GreaterThanOrEqualTo(log.Events[i - 1].Tick));
        }

        [Test]
        public void 로그를_끄면_아무것도_쌓이지_않는다()
        {
            // sim 처리량의 전제다 (19 §2.5).
            var sim = new BattleSimulator(Config());
            var result = sim.Run(new[] { Ally(0, 0, 2, hp: 300, atk: 40), Enemy(1, 5, 2, hp: 50, atk: 5) },
                                 NullEventSink.Instance);

            Assert.That(result.Log, Is.Null);
        }

        [Test]
        public void 양측_동시_전멸은_플레이어_패배다()
        {
            // D-54. 무승부를 만들면 M1·M2 집계에 세 번째 상태가 생겨 지표가 지저분해진다.
            var sim = new BattleSimulator(Config());
            var result = sim.Run(new[] { Ally(0, 0, 0, hp: 0, atk: 0), Enemy(1, 7, 5, hp: 0, atk: 0) },
                                 NullEventSink.Instance);

            Assert.That(result.Outcome, Is.EqualTo(BattleOutcome.AllyLoss));
        }
    }
}
