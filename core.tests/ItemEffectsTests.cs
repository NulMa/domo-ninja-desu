using System.Linq;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Economy;
using DomoNinja.Core.Rng;
using DomoNinja.Core.Skills;
using NUnit.Framework;

namespace DomoNinja.Core.Tests
{
    /// <summary>아이템 효과 (`economy.items` · `_schema` §8).</summary>
    [TestFixture]
    public class ItemEffectsTests
    {
        private static GameData _data = null!;

        [OneTimeSetUp]
        public void Load() => _data = RepoData.LoadAll();

        private static readonly string[] Trio = { "C1", "C2", "C4" };

        private static RunState NewRun() =>
            new RunEngine(_data, CombatConfig.From(_data.Economy, 20))
                .StartRun("S1", Trio, new MetaProgress(_data.Meta));

        [Test]
        public void 아이템이_없으면_보정이_0이다()
        {
            var run = NewRun();

            Assert.That(ItemEffects.DeltaPermilleFor(_data, run, run.Deployed[0], StatKey.Attack),
                        Is.EqualTo(0));
        }

        [Test]
        public void 캐릭터_지정_아이템은_그_캐릭터에게만_걸린다()
        {
            var run = NewRun();
            run.Deployed[0].Items.Add(new OwnedItem("statBoost", 0));   // attack +15%

            Assert.That(ItemEffects.DeltaPermilleFor(_data, run, run.Deployed[0], StatKey.Attack),
                        Is.EqualTo(150));
            Assert.That(ItemEffects.DeltaPermilleFor(_data, run, run.Deployed[1], StatKey.Attack),
                        Is.EqualTo(0));
        }

        [Test]
        public void 팀_아이템은_전원에게_걸린다()
        {
            // 가격이 2배인 이유가 여기 있다 — 전원에게 걸리므로 기회비용을 가격으로 만든다.
            var run = NewRun();
            run.TeamItems.Add(new OwnedItem("teamBoost", 0));   // attack +10%

            foreach (var entry in run.Deployed)
                Assert.That(ItemEffects.DeltaPermilleFor(_data, run, entry, StatKey.Attack),
                            Is.EqualTo(100), entry.CharacterId);
        }

        [Test]
        public void 같은_아이템을_겹치면_곱이_아니라_합이다()
        {
            // ★ 곱연산이면 겹칠수록 폭발해 M4 지배 빌드가 생긴다.
            //   1.15² = 1.3225 가 아니라 1 + 0.15 + 0.15 = 1.30 이어야 한다.
            var run = NewRun();
            run.Deployed[0].Items.Add(new OwnedItem("statBoost", 0));
            run.Deployed[0].Items.Add(new OwnedItem("statBoost", 0));

            Assert.That(ItemEffects.DeltaPermilleFor(_data, run, run.Deployed[0], StatKey.Attack),
                        Is.EqualTo(300));
        }

        [Test]
        public void 개인_아이템과_팀_아이템도_같은_통에서_더해진다()
        {
            var run = NewRun();
            run.Deployed[0].Items.Add(new OwnedItem("statBoost", 0));   // +15%
            run.TeamItems.Add(new OwnedItem("teamBoost", 0));           // +10%

            Assert.That(ItemEffects.DeltaPermilleFor(_data, run, run.Deployed[0], StatKey.Attack),
                        Is.EqualTo(250));
        }

        [Test]
        public void 공격_간격_아이템은_감소_방향으로_들어온다()
        {
            // economy 에 0.9 로 적혀 있다(곱해서 줄이는 형태).
            // 다른 스탯과 같은 증감분 규약(-10%)으로 들어와야 합연산 통에 섞인다.
            var run = NewRun();
            run.Deployed[0].Items.Add(new OwnedItem("statBoost", 2));

            Assert.That(ItemEffects.DeltaPermilleFor(_data, run, run.Deployed[0], StatKey.AttackInterval),
                        Is.EqualTo(-100));
        }

        [Test]
        public void 아이템이_실제_전투_스탯에_반영된다()
        {
            // 계산기만 맞고 전투에 안 들어가면 아무 일도 안 일어난다 —
            // P2 에서 스킬 셋이 그렇게 죽어 있었다.
            var run = NewRun();
            var meta = new MetaProgress(_data.Meta);
            var variant = _data.Rounds[0].Variants[0];

            int before = BattleSetup.Build(_data, run, variant, meta)
                .First(u => u.TypeId == "C1").Attack;

            run.Deployed[0].Items.Add(new OwnedItem("statBoost", 0));   // attack +15%

            int after = BattleSetup.Build(_data, run, variant, meta)
                .First(u => u.TypeId == "C1").Attack;

            Assert.That(after, Is.GreaterThan(before));
            Assert.That(after, Is.EqualTo(51), "45 * 1.15 = 51.75 → 51");
        }

        [Test]
        public void 회복_아이템은_소지품이_아니라_즉시_발동이다()
        {
            // 소지하게 두면 전투 중 사용이 생기고, 그건 A-8(개입 지점은 배치와 상점뿐)을 깬다.
            var run = NewRun();
            run.Currency = 100;
            run.Deployed[0].Hp = 10;

            var shop = new Shop(_data);
            for (ulong seed = 1; seed <= 40; seed++)
            {
                shop.Restock(run, new DeterministicRandom(seed).Fork(RngStream.Shop), round: 1);

                int index = shop.Offers.ToList().FindIndex(o => o.Id == "healItem");
                if (index < 0) continue;

                Assert.That(shop.TryBuy(run, index, "C1"), Is.True);
                Assert.That(run.Deployed[0].Hp, Is.GreaterThan(10), "구매 즉시 회복돼야 한다");
                Assert.That(run.Deployed[0].Items.Any(i => i.Key == "healItem"), Is.False,
                    "회복 아이템이 소지품으로 남으면 안 된다");
                return;
            }

            Assert.Fail("healItem 이 40번 안에 한 번도 안 나왔다");
        }

        [Test]
        public void 미구현_아이템을_이름으로_드러낸다()
        {
            // ★ conditionalBoost 는 조건이 전투 중에 바뀌어 시작 스탯으로 접을 수 없다.
            //   상시 적용으로 대충 넣으면 "조건부인데 항상 켜진 아이템"이 되어
            //   밸런스 지표가 실제보다 세게 나온다. 조용히 무시하지 않고 이름을 남긴다.
            Assert.That(ItemEffects.IsPending("conditionalBoost"), Is.True);
            Assert.That(ItemEffects.IsPending("statBoost"), Is.False);

            var run = NewRun();
            run.Deployed[0].Items.Add(new OwnedItem("conditionalBoost", 0));

            Assert.That(ItemEffects.DeltaPermilleFor(_data, run, run.Deployed[0], StatKey.Attack),
                        Is.EqualTo(0), "미구현인 동안에는 효과가 없어야 한다 — 반만 켜지면 지표가 더 나빠진다");
        }
    }
}
