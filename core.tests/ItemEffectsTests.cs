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

        // ────────────────────────────── 조건부 강화

        [Test]
        public void 조건부_강화는_시작_스탯에_접히지_않는다()
        {
            // ★ 세 조건이 전부 전투 중에 뒤집힌다. 시작 스탯에 넣으면
            //   "조건부인데 항상 켜진 아이템" 이 되어 밸런스 지표가 실제보다 세게 나온다.
            //   ⚠️ 특히 `value` 는 임계값이다 — 거르지 않으면 0.5 를 강화값으로 읽어 상시 +50% 가 된다.
            var run = NewRun();
            run.Deployed[0].Items.Add(new OwnedItem("conditionalBoost", 0));   // hp_below 0.5 → attack +40%

            Assert.That(ItemEffects.DeltaPermilleFor(_data, run, run.Deployed[0], StatKey.Attack),
                        Is.EqualTo(0), "조건부가 고정 증감분으로 새어 들어갔다");
        }

        [Test]
        public void 조건부_강화는_임계값과_강화값을_갈라_읽는다()
        {
            var run = NewRun();
            run.Deployed[0].Items.Add(new OwnedItem("conditionalBoost", 0));

            var boosts = ItemEffects.CollectConditional(_data, run, run.Deployed[0]);

            Assert.That(boosts.Count, Is.EqualTo(1));
            Assert.That(boosts[0].Condition, Is.EqualTo(BoostCondition.HpBelow));
            Assert.That(boosts[0].Threshold, Is.EqualTo(500), "`value` 0.5 는 임계 50% 다");
            Assert.That(boosts[0].Stat, Is.EqualTo(StatKey.Attack));
            Assert.That(boosts[0].DeltaPermille, Is.EqualTo(400), "`mult` 0.4 가 강화값이다");
        }

        [Test]
        public void 개수_조건은_천분율로_바꾸지_않는다()
        {
            // enemies_above 의 `value` 3 은 "적 3체 초과" 지 "0.3%" 가 아니다.
            var run = NewRun();
            run.Deployed[0].Items.Add(new OwnedItem("conditionalBoost", 1));

            var boosts = ItemEffects.CollectConditional(_data, run, run.Deployed[0]);

            Assert.That(boosts[0].Condition, Is.EqualTo(BoostCondition.EnemiesAbove));
            Assert.That(boosts[0].Threshold, Is.EqualTo(3));
        }

        [Test]
        public void 조건은_전투_상황에_따라_켜지고_꺼진다()
        {
            var self = new Unit(0, Team.Ally, "C1", 100, 10, 20, 1, 5, new Coord(0, 0));
            var mate = new Unit(1, Team.Ally, "C2", 100, 10, 20, 1, 5, new Coord(0, 1));
            var foes = new[]
            {
                new Unit(2, Team.Enemy, "slime", 40, 8, 26, 1, 8, new Coord(4, 0)),
                new Unit(3, Team.Enemy, "slime", 40, 8, 26, 1, 8, new Coord(4, 1)),
            };
            var allies = new[] { self, mate };

            var hpBelow = new ConditionalBoost(BoostCondition.HpBelow, 500, StatKey.Attack, 400);
            Assert.That(hpBelow.IsActive(self, allies, foes), Is.False, "만피에서는 꺼져 있다");
            self.Hp = 40;
            Assert.That(hpBelow.IsActive(self, allies, foes), Is.True);

            var enemiesAbove = new ConditionalBoost(BoostCondition.EnemiesAbove, 1, StatKey.Attack, 300);
            Assert.That(enemiesAbove.IsActive(self, allies, foes), Is.True, "2체 > 1");
            foes[1].Hp = 0;
            Assert.That(enemiesAbove.IsActive(self, allies, foes), Is.False, "죽은 적은 안 센다");

            var lastAlive = new ConditionalBoost(BoostCondition.IsLastAlive, 0, StatKey.DamageTaken, -350);
            Assert.That(lastAlive.IsActive(self, allies, foes), Is.False);
            mate.Hp = 0;
            Assert.That(lastAlive.IsActive(self, allies, foes), Is.True);
        }

        [Test]
        public void 미구현_아이템_목록은_남겨둔다()
        {
            // 지금은 비어 있다. 목록 자체를 지우면 최적화기가 economy.json 에 새 아이템을 넣었을 때
            // "이름은 있는데 아무 일도 안 한다" 를 드러낼 자리가 없어진다.
            Assert.That(ItemEffects.IsPending("statBoost"), Is.False);
            Assert.That(ItemEffects.IsPending("conditionalBoost"), Is.False,
                        "D+4 에 구현됐다 — Pending 에 남아 있으면 조용히 꺼진다");
        }
    }
}
