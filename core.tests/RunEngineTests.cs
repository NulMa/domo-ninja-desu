using System.Collections.Generic;
using System.Linq;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using DomoNinja.Core.Economy;
using DomoNinja.Core.Events;
using DomoNinja.Core.Rng;
using NUnit.Framework;

namespace DomoNinja.Core.Tests
{
    /// <summary>런 루프 — 라운드 진행 · 생명 · HP 누적 · 회복 · 재화 (`19` §6.4).</summary>
    [TestFixture]
    public class RunEngineTests
    {
        private static GameData _data = null!;

        [OneTimeSetUp]
        public void Load() => _data = GameDataLoader.Load(
            RepoData.Characters, RepoData.Skills, RepoData.Encounters, RepoData.Economy, RepoData.Meta);

        private static RunEngine Engine() => new RunEngine(_data, CombatConfig.From(_data.Economy, 20));
        private static MetaProgress Meta() => new MetaProgress(_data.Meta);

        private static readonly string[] Trio = { "C1", "C2", "C4" };

        [Test]
        public void 런은_생명_3과_라운드_1로_시작한다()
        {
            var run = Engine().StartRun("S1", Trio, Meta());

            Assert.That(run.Lives, Is.EqualTo(3));
            Assert.That(run.Round, Is.EqualTo(1));
            Assert.That(run.Currency, Is.EqualTo(0));
            Assert.That(run.Deployed.Count, Is.EqualTo(3));
            Assert.That(run.Deployed.All(r => r.IsAlive), Is.True);
        }

        [Test]
        public void 출전_순서가_그대로_슬롯_인덱스가_된다()
        {
            // 이 순서가 매 틱 순회 순서이자 모든 동률 판정의 최종 기준이다.
            var run = Engine().StartRun("S1", new[] { "C4", "C1", "C6" }, Meta());

            Assert.That(run.Deployed.Select(r => r.CharacterId), Is.EqualTo(new[] { "C4", "C1", "C6" }));
        }

        [Test]
        public void 메타_체력_강화가_시작_체력에_반영된다()
        {
            var meta = Meta();
            meta.SetLevel("M-HP", 5);   // +25%

            var plain = Engine().StartRun("S1", new[] { "C1" }, Meta());
            var buffed = Engine().StartRun("S1", new[] { "C1" }, meta);

            // ★ 기대값을 박지 않고 강화 없는 쪽에서 유도한다 — [BAL] 커밋이 C1 체력을
            //   바꾸면 박아둔 숫자가 그때마다 깨진다. 재는 것은 "메타가 +25% 를 한 번만 곱하는가" 다.
            int baseHp = plain.Deployed[0].MaxHp;
            Assert.That(buffed.Deployed[0].MaxHp, Is.EqualTo(baseHp * 125 / 100), "M-HP 5레벨 = +25%");
        }

        [Test]
        public void 라운드를_치르면_라운드_번호가_오른다()
        {
            var engine = Engine();
            var run = engine.StartRun("S1", Trio, Meta());

            var outcome = engine.PlayRound(run, Meta(), new DeterministicRandom(1), NullEventSink.Instance);

            Assert.That(outcome.Round, Is.EqualTo(1));
            Assert.That(run.Round, Is.EqualTo(2));
            Assert.That(outcome.Ticks, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void 온보딩_라운드는_한_틱에_끝난다()
        {
            // ★ 처음엔 이걸 버그로 의심했는데 규칙대로다.
            //   모든 유닛이 쿨다운 0 으로 시작하므로 첫 틱에 동시에 때리고,
            //   1라운드는 슬라임(40) 둘이라 C1(45)·C2(22)·C4(30) 앞에서 즉사한다.
            //
            //   ⚠️ 다만 M5(1런 3~5분)에 영향이 있다 — 초반 라운드가 0틱이면
            //      런이 의도보다 훨씬 빨리 끝난다. 값의 문제이므로 밸런스 루프가 볼 대상이고,
            //      "전투 시작 전 한 박자"를 넣을지는 연출·감각 판단이라 여기서 정하지 않는다.
            var engine = Engine();
            var run = engine.StartRun("S1", Trio, Meta());

            var outcome = engine.PlayRound(run, Meta(), new DeterministicRandom(1), NullEventSink.Instance);

            Assert.That(outcome.Won, Is.True);
            Assert.That(outcome.Ticks, Is.EqualTo(0));
        }

        [Test]
        public void 이기면_재화를_더_받는다()
        {
            // 승리 +6 / 패배 +3 (economy.currency).
            Assert.That(_data.Economy.CurrencyOnWin, Is.EqualTo(6));
            Assert.That(_data.Economy.CurrencyOnLose, Is.EqualTo(3));

            var engine = Engine();
            var run = engine.StartRun("S1", Trio, Meta());
            var outcome = engine.PlayRound(run, Meta(), new DeterministicRandom(1), NullEventSink.Instance);

            Assert.That(run.Currency, Is.EqualTo(outcome.Won ? 6 : 3));
        }

        [Test]
        public void 재화_메타_강화가_승리_보상에_얹힌다()
        {
            var meta = Meta();
            meta.SetLevel("M-GOLD", 3);   // +3

            var engine = Engine();
            var run = engine.StartRun("S1", Trio, meta);
            var outcome = engine.PlayRound(run, meta, new DeterministicRandom(1), NullEventSink.Instance);

            if (outcome.Won) Assert.That(run.Currency, Is.EqualTo(9));
        }

        [Test]
        public void HP_는_라운드를_넘어_누적된다()
        {
            // A-6. 전투가 끝나도 체력이 회복되지 않고 이어진다 — 그게 생명 3 의 긴장을 만든다.
            var engine = Engine();
            var meta = Meta();
            var run = engine.StartRun("S1", Trio, meta);

            // 1~2라운드는 온보딩이라 아군이 한 대도 안 맞고 끝난다. 실제로 맞는 라운드에서 본다.
            run.Round = 6;
            int before = run.Deployed.Sum(r => r.Hp);
            engine.PlayRound(run, meta, new DeterministicRandom(1), NullEventSink.Instance);
            int after = run.Deployed.Sum(r => r.Hp);

            Assert.That(after, Is.LessThan(before), "라운드를 치르고도 체력이 그대로면 누적이 안 되는 것이다");
        }

        [Test]
        public void 이기면_회복하고_지면_생명을_잃는다()
        {
            var engine = Engine();
            var meta = Meta();
            var run = engine.StartRun("S1", Trio, meta);

            // 1라운드는 slime 둘이라 정상 로스터면 진다고 보기 어렵다.
            var outcome = engine.PlayRound(run, meta, new DeterministicRandom(1), NullEventSink.Instance);

            if (outcome.Won) Assert.That(run.Lives, Is.EqualTo(3));
            else Assert.That(run.Lives, Is.EqualTo(2));
        }

        [Test]
        public void 승리_회복은_최대_체력을_넘지_않는다()
        {
            var engine = Engine();
            var meta = Meta();
            var run = engine.StartRun("S1", Trio, meta);

            engine.PlayRun(run, meta, new DeterministicRandom(7), NullEventSink.Instance);

            foreach (var entry in run.Deployed)
                Assert.That(entry.Hp, Is.LessThanOrEqualTo(entry.MaxHp), entry.CharacterId);
        }

        [Test]
        public void 죽은_캐릭터는_런_내내_돌아오지_않는다()
        {
            // A6 — 부활 없음. 회복도 대상이 아니다.
            var engine = Engine();
            var meta = Meta();
            var run = engine.StartRun("S1", Trio, meta);

            run.Deployed[0].Hp = 0;
            engine.PlayRun(run, meta, new DeterministicRandom(3), NullEventSink.Instance);

            Assert.That(run.Deployed[0].IsAlive, Is.False);
            Assert.That(run.Deployed[0].Hp, Is.EqualTo(0));
        }

        // ────────────────────────────── 런 전체

        [Test]
        public void 런은_8라운드까지_돈다()
        {
            var engine = Engine();
            var meta = Meta();
            var run = engine.StartRun("S1", Trio, meta);

            var summary = engine.PlayRun(run, meta, new DeterministicRandom(42), NullEventSink.Instance);

            Assert.That(summary.RoundsReached, Is.LessThanOrEqualTo(8));
            Assert.That(summary.Rounds.Select(r => r.Round),
                        Is.EqualTo(Enumerable.Range(1, summary.RoundsReached)));
        }

        [Test]
        public void 생명이_0이_되면_즉시_끝낸다()
        {
            // 남은 라운드를 마저 도는 건 결과에 영향을 주지 않으면서 시뮬 시간만 먹는다.
            var engine = Engine();
            var meta = Meta();
            var run = engine.StartRun("S1", new[] { "C3" }, meta);   // 혼자서는 버티기 어렵다

            var summary = engine.PlayRun(run, meta, new DeterministicRandom(5), NullEventSink.Instance);

            if (summary.LivesLeft <= 0)
                Assert.That(summary.RoundsReached, Is.LessThan(8), "생명이 다했는데 계속 돌면 안 된다");
        }

        [Test]
        public void 같은_시드로_두_번_돌리면_런_결과가_같다()
        {
            // 🔴 밸런스 수치 전체가 이 성질 위에 서 있다.
            string Once()
            {
                var engine = Engine();
                var meta = Meta();
                var run = engine.StartRun("S1", Trio, meta);
                var summary = engine.PlayRun(run, meta, new DeterministicRandom(20260802), NullEventSink.Instance);

                return string.Join("|", summary.Rounds.Select(r => $"{r.Round}:{r.VariantId}:{r.Won}:{r.Ticks}"))
                       + $"#{summary.LivesLeft}#{run.Currency}";
            }

            Assert.That(Once(), Is.EqualTo(Once()));
        }

        [Test]
        public void 시드가_다르면_관문_변형이_갈린다()
        {
            // 3라운드부터 변형 2종이 있다. 변형 추첨이 시드를 안 타면 M2 층화가 의미를 잃는다.
            var seen = new HashSet<string>();

            for (ulong seed = 1; seed <= 12; seed++)
            {
                var engine = Engine();
                var meta = Meta();
                var run = engine.StartRun("S1", Trio, meta);
                var summary = engine.PlayRun(run, meta, new DeterministicRandom(seed), NullEventSink.Instance);

                foreach (var r in summary.Rounds.Where(r => r.Round >= 3))
                    seen.Add(r.VariantId);
            }

            Assert.That(seen.Any(v => v.EndsWith("a")), Is.True);
            Assert.That(seen.Any(v => v.EndsWith("b")), Is.True, "한쪽 변형만 나오면 추첨이 죽은 것이다");
        }

        [Test]
        public void 로그를_켜면_라운드마다_재생용_로그가_나온다()
        {
            var engine = Engine();
            var meta = Meta();
            var run = engine.StartRun("S1", Trio, meta);

            var outcome = engine.PlayRound(run, meta, new DeterministicRandom(1),
                                           NullEventSink.Instance, collectLog: true);

            Assert.That(outcome.Log, Is.Not.Null);
            Assert.That(outcome.Log!.Round, Is.EqualTo(1));
            Assert.That(outcome.Log.Units.Count, Is.GreaterThan(3), "아군 3 + 적");
            Assert.That(outcome.Log.Events[outcome.Log.Events.Count - 1].Kind, Is.EqualTo(EventKind.RoundEnd));
        }
    }
}
