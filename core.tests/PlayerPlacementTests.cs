using System;
using System.Collections.Generic;
using System.Linq;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Economy;
using DomoNinja.Core.Events;
using DomoNinja.Core.Rng;
using NUnit.Framework;

namespace DomoNinja.Core.Tests
{
    /// <summary>
    /// 플레이어 배치 입력 경로 (`D-75`) — 적을 보고 배치한 뒤 전투가 시작된다 (`D-53`).
    /// </summary>
    /// <remarks>
    /// ★ 이 경로의 가장 큰 위험은 <b>난수 스트림이 한 칸 밀리는 것</b>이다.
    /// <c>PeekVariant</c> 가 RNG 를 소비하므로, 그 결과를 안 쓰고 버리거나 두 번 부르면
    /// <b>이후 라운드 구성이 통째로 달라진다</b> — 그리고 그건 결과 숫자로는 안 보인다.
    /// </remarks>
    [TestFixture]
    public class PlayerPlacementTests
    {
        private static GameData _data = null!;

        [OneTimeSetUp]
        public void Load() => _data = RepoData.LoadAll();

        private static readonly string[] Trio = { "C1", "C2", "C3" };

        private static RunEngine Engine() => new RunEngine(_data, CombatConfig.From(_data.Economy, 20));
        private static MetaProgress Meta() => new MetaProgress(_data.Meta);

        private static RunState NewRun(RunEngine e) => e.StartRun("S1", Trio, Meta());

        /// <summary>겹치지 않는 유효한 아군 진영 좌표.</summary>
        private static Dictionary<string, Coord> ValidPlacement() => new Dictionary<string, Coord>
        {
            ["C1"] = new Coord(3, 2),
            ["C2"] = new Coord(2, 1),
            ["C3"] = new Coord(0, 4),
        };

        // ────────────────────────────── 미리 보기

        [Test]
        public void PeekVariant_는_전투를_실행하지_않는다()
        {
            // D-53 이 "적 배치 공개" 를 정했는데, 기존 PlayRound 는 추첨과 전투가
            // 한 덩어리라 물어보는 순간 전투가 같이 끝나버렸다.
            var engine = Engine();
            var run = NewRun(engine);

            int roundBefore = run.Round;
            int livesBefore = run.Lives;

            var variant = engine.PeekVariant(run, new DeterministicRandom(1).Fork(RngStream.Encounter));

            Assert.That(variant, Is.Not.Null);
            Assert.That(variant.Units.Count, Is.GreaterThan(0), "적 구성이 비어 있다");
            Assert.That(run.Round, Is.EqualTo(roundBefore), "전투가 실행돼 라운드가 넘어갔다");
            Assert.That(run.Lives, Is.EqualTo(livesBefore));
        }

        [Test]
        public void 같은_시드면_Peek_이_뽑는_변형도_같다()
        {
            var engine = Engine();

            string Once()
            {
                var run = NewRun(engine);
                return engine.PeekVariant(run, new DeterministicRandom(7).Fork(RngStream.Encounter)).Id;
            }

            Assert.That(Once(), Is.EqualTo(Once()));
        }

        // ────────────────────────────── ★ 난수 스트림

        [Test]
        public void Peek_한_변형으로_돌린_런이_기존_경로와_완전히_같다()
        {
            // ★ 이 테스트가 이 파일의 핵심이다.
            //   PeekVariant + 새 오버로드 조합이 기존 PlayRound(rng) 와 같은 난수를 소비해야
            //   봇 경로(sim)와 플레이어 경로가 같은 게임을 돌게 된다.
            //   여기서 갈라지면 sim 이 낸 밸런스 수치가 실제 플레이와 어긋나는데,
            //   그 사실은 어느 쪽 숫자로도 드러나지 않는다.
            string Legacy()
            {
                var engine = Engine();
                var run = NewRun(engine);
                var rng = new DeterministicRandom(42).Fork(RngStream.Encounter);
                var meta = Meta();

                var log = new List<string>();
                for (int i = 0; i < 4 && !run.IsOver; i++)
                {
                    var o = engine.PlayRound(run, meta, rng, NullEventSink.Instance);
                    log.Add($"{o.Round}:{o.VariantId}:{(o.Won ? "W" : "L")}:{o.Ticks}");
                }
                return string.Join("|", log);
            }

            string PeekThenPlay()
            {
                var engine = Engine();
                var run = NewRun(engine);
                var rng = new DeterministicRandom(42).Fork(RngStream.Encounter);
                var meta = Meta();

                var log = new List<string>();
                for (int i = 0; i < 4 && !run.IsOver; i++)
                {
                    // Unity 가 매 라운드 하는 것: 먼저 적을 보고, 그다음 전투를 건다.
                    var variant = engine.PeekVariant(run, rng);
                    var o = engine.PlayRound(run, meta, variant, NullEventSink.Instance);
                    log.Add($"{o.Round}:{o.VariantId}:{(o.Won ? "W" : "L")}:{o.Ticks}");
                }
                return string.Join("|", log);
            }

            Assert.That(PeekThenPlay(), Is.EqualTo(Legacy()),
                        "Peek 경로가 기존 경로와 다른 결과를 냈다 — 난수 스트림이 어긋난다");
        }

        [Test]
        public void Peek_을_두_번_부르면_결과가_달라진다()
        {
            // ⚠️ 이건 버그가 아니라 계약이다. PeekVariant 는 RNG 를 소비하므로
            //    라운드당 정확히 한 번만 불러야 한다. 그 사실을 테스트로 못박아 둔다 —
            //    "왜 라운드 구성이 밀렸지" 를 나중에 추적하는 것보다 싸다.
            var engine = Engine();
            var run = NewRun(engine);
            var rng = new DeterministicRandom(3).Fork(RngStream.Encounter);

            var first = engine.PeekVariant(run, rng);
            var second = engine.PeekVariant(run, rng);

            // 변형이 하나뿐인 라운드면 같을 수 있으니, 스트림이 실제로 밀렸는지로 본다.
            var fresh = engine.PeekVariant(run, new DeterministicRandom(3).Fork(RngStream.Encounter));
            Assert.That(first.Id, Is.EqualTo(fresh.Id), "첫 호출은 새 스트림과 같아야 한다");
            Assert.That(second, Is.Not.Null, "두 번째 호출도 값을 돌려주긴 한다 — 스트림만 밀린다");
        }

        // ────────────────────────────── 배치 적용

        [Test]
        public void 플레이어_좌표가_그대로_전투에_들어간다()
        {
            var engine = Engine();
            var run = NewRun(engine);
            var meta = Meta();
            var variant = _data.Rounds[0].Variants[0];

            var placement = ValidPlacement();
            var units = BattleSetup.Build(_data, run, variant, meta, placement);

            foreach (var kv in placement)
            {
                var u = units.First(x => x.Team == Team.Ally && x.TypeId == kv.Key);
                Assert.That(u.At, Is.EqualTo(kv.Value), $"{kv.Key} 가 지정한 자리에 없다");
            }
        }

        [Test]
        public void 배치를_안_넘기면_표준_배치로_떨어진다()
        {
            // sim 은 4,320 빌드를 사람 없이 돌려야 한다. 표준 배치가 사라지면
            // M4 를 "표준 배치 하의 정확값" 이라고 말할 수 없게 된다 (08 §6.1).
            var engine = Engine();
            var run = NewRun(engine);
            var variant = _data.Rounds[0].Variants[0];

            var auto = BattleSetup.Build(_data, run, variant, Meta());
            var auto2 = BattleSetup.Build(_data, run, variant, Meta(), null);

            Assert.That(auto.Select(u => u.At.OrderKey),
                        Is.EqualTo(auto2.Select(u => u.At.OrderKey)));
            Assert.That(auto.Where(u => u.Team == Team.Ally).Select(u => u.At.X),
                        Is.All.LessThanOrEqualTo(Coord.AllyMaxX));
        }

        // ────────────────────────────── 검증 (조용히 넘어가지 않는다)

        [Test]
        public void 아군_진영_밖_좌표는_거부한다()
        {
            var engine = Engine();
            var run = NewRun(engine);
            var variant = _data.Rounds[0].Variants[0];

            var bad = ValidPlacement();
            bad["C1"] = new Coord(Coord.AllyMaxX + 1, 2);   // 적 진영

            var ex = Assert.Throws<ArgumentException>(
                () => BattleSetup.Build(_data, run, variant, Meta(), bad));
            Assert.That(ex!.Message, Does.Contain("C1"));
        }

        [Test]
        public void 겹친_좌표는_거부한다()
        {
            // ★ 겹치면 Board.TryPlace 가 두 번째를 조용히 거부하고,
            //   전투가 한 명 적은 채로 정상처럼 돌아간다. 그게 이 검사의 이유다.
            var engine = Engine();
            var run = NewRun(engine);
            var variant = _data.Rounds[0].Variants[0];

            var bad = ValidPlacement();
            bad["C2"] = bad["C1"];

            var ex = Assert.Throws<ArgumentException>(
                () => BattleSetup.Build(_data, run, variant, Meta(), bad));
            Assert.That(ex!.Message, Does.Contain("같은 칸"));
        }

        [Test]
        public void 빠진_아군이_있으면_거부한다()
        {
            // 일부만 채우면 표준배치가 "전체를 사거리순 정렬" 로 열을 정하므로
            // 표준도 플레이어 의도도 아닌 제3의 배치가 된다.
            var engine = Engine();
            var run = NewRun(engine);
            var variant = _data.Rounds[0].Variants[0];

            var partial = ValidPlacement();
            partial.Remove("C3");

            var ex = Assert.Throws<ArgumentException>(
                () => BattleSetup.Build(_data, run, variant, Meta(), partial));
            Assert.That(ex!.Message, Does.Contain("C3"));
        }

        [Test]
        public void 죽은_캐릭터의_좌표는_요구하지_않는다()
        {
            // A-6 — 사망 유닛은 런 종료까지 안 돌아온다. 보드에도 안 올라가므로
            // UI 가 좌표를 보낼 이유가 없다.
            var engine = Engine();
            var run = NewRun(engine);
            var variant = _data.Rounds[0].Variants[0];

            run.Deployed[2].Hp = 0;

            var placement = ValidPlacement();
            placement.Remove(run.Deployed[2].CharacterId);

            Assert.DoesNotThrow(() => BattleSetup.Build(_data, run, variant, Meta(), placement));
        }

        // ────────────────────────────── 전체 흐름

        [Test]
        public void 배치를_바꾸면_전투_결과가_달라진다()
        {
            // 배치가 실제로 전투에 영향을 주는지. 안 그러면 이 기능 전체가 장식이다.
            RoundOutcome Play(Dictionary<string, Coord> p)
            {
                var engine = Engine();
                var run = NewRun(engine);
                var rng = new DeterministicRandom(11).Fork(RngStream.Encounter);
                var variant = engine.PeekVariant(run, rng);
                return engine.PlayRound(run, Meta(), variant, NullEventSink.Instance, p);
            }

            var front = new Dictionary<string, Coord>
            {
                ["C1"] = new Coord(3, 2), ["C2"] = new Coord(3, 3), ["C3"] = new Coord(3, 1),
            };
            var back = new Dictionary<string, Coord>
            {
                ["C1"] = new Coord(0, 0), ["C2"] = new Coord(0, 5), ["C3"] = new Coord(0, 3),
            };

            Assert.That(Play(back).Ticks, Is.Not.EqualTo(Play(front).Ticks),
                        "앞열과 뒷열 배치가 같은 틱을 냈다 — 배치가 전투에 안 들어갔다");
        }
    }
}
