using System.Linq;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Economy;
using DomoNinja.Core.Rng;
using NUnit.Framework;

namespace DomoNinja.Core.Tests
{
    /// <summary>상점 — 자리 고정 · 배타 규칙 · 판매 (`08` §2.2 · §4.2 · `D-69` · `D-70`).</summary>
    [TestFixture]
    public class ShopTests
    {
        private static GameData _data = null!;

        [OneTimeSetUp]
        public void Load() => _data = GameDataLoader.Load(
            RepoData.Characters, RepoData.Skills, RepoData.Encounters, RepoData.Economy, RepoData.Meta);

        private static readonly string[] Trio = { "C1", "C2", "C4" };

        private static RunState NewRun()
        {
            var engine = new RunEngine(_data, CombatConfig.From(_data.Economy, 20));
            return engine.StartRun("S1", Trio, new MetaProgress(_data.Meta));
        }

        private static Shop NewShop() => new Shop(_data);
        private static DeterministicRandom Rng(ulong seed = 1) =>
            new DeterministicRandom(seed).Fork(RngStream.Shop);

        // ────────────────────────────── 자리 고정

        [Test]
        public void 스킬_3칸과_아이템_2칸이_나온다()
        {
            // ★ D-69 의 요점. 한 풀에서 뽑으면 "이번 라운드엔 스킬이 하나도 안 나옴"이 생기고
            //   M6(첫 활성화 라운드)이 그 노이즈를 그대로 먹는다.
            var run = NewRun();
            var shop = NewShop();
            shop.Restock(run, Rng(), round: 1);

            Assert.That(shop.Offers.Count(o => o.Kind == OfferKind.ActiveSkill), Is.EqualTo(3));
            Assert.That(shop.Offers.Count(o => o.Kind == OfferKind.Item), Is.EqualTo(2));
        }

        [Test]
        public void 스킬_자리에_같은_품목이_두_번_나오지_않는다()
        {
            var run = NewRun();
            var shop = NewShop();

            for (ulong seed = 1; seed <= 20; seed++)
            {
                shop.Restock(run, Rng(seed), round: 1);
                var ids = shop.Offers.Select(o => o.Id).ToList();
                Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count), $"seed {seed}");
            }
        }

        [Test]
        public void 벤치_캐릭터의_스킬은_나오지_않는다()
        {
            // 로스터 선택이 곧 상점 풀을 결정한다 — 첫 선택의 무게가 여기서 생긴다 (08 §2.2).
            var run = NewRun();
            var shop = NewShop();
            shop.Restock(run, Rng(), round: 1);

            foreach (var offer in shop.Offers.Where(o => o.Kind == OfferKind.ActiveSkill))
                Assert.That(Trio, Does.Contain(offer.CharacterId), offer.Id);
        }

        [Test]
        public void 죽은_캐릭터의_품목은_빠진다()
        {
            var run = NewRun();
            run.Deployed[0].Hp = 0;

            var shop = NewShop();
            shop.Restock(run, Rng(), round: 1);

            Assert.That(shop.Offers.Any(o => o.CharacterId == "C1"), Is.False,
                "살 이유가 없는 품목이 자리를 먹으면 살아 있는 캐릭터의 기회가 줄어든다");
        }

        // ────────────────────────────── 배타 규칙 ★

        [Test]
        public void 하나를_사면_같은_캐릭터의_다른_하나가_사라진다()
        {
            // ★ 이 게임의 정체성이다. 배타가 없으면 빌드 공간이 무너진다.
            var run = NewRun();
            run.Currency = 100;

            var shop = NewShop();
            shop.Restock(run, Rng(), round: 1);

            int index = shop.Offers.ToList().FindIndex(o => o.Id == "C1-A");
            if (index < 0)
            {
                // 추첨이라 C1-A 가 안 나올 수 있다. 나올 때까지 리롤한다.
                for (ulong seed = 2; index < 0 && seed <= 30; seed++)
                {
                    shop.Restock(run, Rng(seed), round: 1);
                    index = shop.Offers.ToList().FindIndex(o => o.Id == "C1-A");
                }
            }

            Assert.That(index, Is.GreaterThanOrEqualTo(0), "C1-A 가 30번 안에 한 번도 안 나왔다");
            Assert.That(shop.TryBuy(run, index), Is.True);

            Assert.That(run.Deployed[0].ActiveSkillId, Is.EqualTo("C1-A"));
            Assert.That(run.RemovedSkillIds, Does.Contain("C1-B"));
        }

        [Test]
        public void 제거된_스킬은_런_내내_다시_나오지_않는다()
        {
            var run = NewRun();
            run.RemovedSkillIds.Add("C1-B");

            var shop = NewShop();
            for (ulong seed = 1; seed <= 30; seed++)
            {
                shop.Restock(run, Rng(seed), round: 1);
                Assert.That(shop.Offers.Any(o => o.Id == "C1-B"), Is.False, $"seed {seed}");
            }
        }

        [Test]
        public void 이미_활성화한_캐릭터의_액티브는_더_나오지_않는다()
        {
            var run = NewRun();
            run.Deployed[0].ActiveSkillId = "C1-A";
            run.RemovedSkillIds.Add("C1-B");

            var shop = NewShop();
            shop.Restock(run, Rng(), round: 1);

            Assert.That(shop.Offers.Any(o => o.CharacterId == "C1" && o.Kind == OfferKind.ActiveSkill),
                        Is.False);
        }

        [Test]
        public void 재화가_모자라면_사지_못하고_배타도_발동하지_않는다()
        {
            var run = NewRun();
            run.Currency = 1;   // 스킬 활성화는 5

            var shop = NewShop();
            shop.Restock(run, Rng(), round: 1);

            int index = shop.Offers.ToList().FindIndex(o => o.Kind == OfferKind.ActiveSkill);

            Assert.That(shop.TryBuy(run, index), Is.False);
            Assert.That(run.RemovedSkillIds, Is.Empty, "실패한 구매가 배타를 발동시키면 스킬이 그냥 증발한다");
            Assert.That(run.Currency, Is.EqualTo(1));
        }

        // ────────────────────────────── 보조

        [Test]
        public void 보조는_지정된_라운드에만_나온다()
        {
            var run = NewRun();
            foreach (var e in run.Deployed) e.ActiveSkillId = _data.FindCharacter(e.CharacterId)!.SkillIds[0];

            var shop = NewShop();

            shop.Restock(run, Rng(), round: 3);
            Assert.That(shop.Offers.Any(o => o.Kind == OfferKind.SupportSkill), Is.False, "3라운드");

            shop.Restock(run, Rng(), round: 4);
            Assert.That(shop.Offers.Any(o => o.Kind == OfferKind.SupportSkill), Is.True, "4라운드");
        }

        [Test]
        public void 메인을_활성화하지_않으면_보조가_나오지_않는다()
        {
            // 강화할 대상이 없으면 살 수 없다 (economy.shop.supportSkill.requiresMainSkillActive).
            var run = NewRun();
            var shop = NewShop();
            shop.Restock(run, Rng(), round: 2);

            Assert.That(shop.Offers.Any(o => o.Kind == OfferKind.SupportSkill), Is.False);
        }

        [Test]
        public void 보조는_캐릭터당_두_개까지다()
        {
            var run = NewRun();
            var entry = run.Deployed[0];
            entry.ActiveSkillId = "C1-A";
            entry.SupportSkillIds.Add("C1-P1");
            entry.SupportSkillIds.Add("C1-P2");

            var shop = NewShop();
            shop.Restock(run, Rng(), round: 2);

            Assert.That(shop.Offers.Any(o => o.Kind == OfferKind.SupportSkill && o.CharacterId == "C1"),
                        Is.False);
        }

        [Test]
        public void 보조는_두_번째가_더_비싸다()
        {
            // prices.supportSkill = [3, 5]
            // C1 만 남겨 풀을 C1 보조 3종으로 좁힌다 — 자리가 3칸이라 반드시 전부 나온다.
            // 추첨에 기대면 이 테스트가 시드에 따라 흔들린다.
            var run = NewRun();
            run.Deployed[1].Hp = 0;
            run.Deployed[2].Hp = 0;

            var entry = run.Deployed[0];
            entry.ActiveSkillId = "C1-A";

            var shop = NewShop();
            shop.Restock(run, Rng(), round: 2);
            int first = shop.Offers.First(o => o.CharacterId == "C1" && o.Kind == OfferKind.SupportSkill).Price;

            entry.SupportSkillIds.Add("C1-P1");
            shop.Restock(run, Rng(), round: 2);
            int second = shop.Offers.First(o => o.CharacterId == "C1" && o.Kind == OfferKind.SupportSkill).Price;

            Assert.That(first, Is.EqualTo(3));
            Assert.That(second, Is.EqualTo(5));
        }

        // ────────────────────────────── 리롤 · 판매

        [Test]
        public void 리롤은_재화를_쓰고_재고를_바꾼다()
        {
            var run = NewRun();
            run.Currency = 10;

            var shop = NewShop();
            shop.Restock(run, Rng(), round: 1);

            Assert.That(shop.TryReroll(run, Rng(99), round: 1), Is.True);
            Assert.That(run.Currency, Is.EqualTo(8), "리롤 비용 2");
        }

        [Test]
        public void 재화가_모자라면_리롤하지_못한다()
        {
            var run = NewRun();
            run.Currency = 1;

            var shop = NewShop();
            shop.Restock(run, Rng(), round: 1);

            Assert.That(shop.TryReroll(run, Rng(2), round: 1), Is.False);
            Assert.That(run.Currency, Is.EqualTo(1));
        }

        [Test]
        public void 아이템은_구매가의_절반을_돌려준다()
        {
            // 손해를 남겨야 "일단 사고 되팔기"가 정답이 되지 않는다 (D-70).
            var run = NewRun();
            run.Deployed[0].Items.Add("statBoost");   // 가격 3
            run.Currency = 0;

            Assert.That(NewShop().TrySellItem(run, "C1", "statBoost"), Is.True);
            Assert.That(run.Currency, Is.EqualTo(1), "3 의 50% = 1.5 → 1 (정수 절삭)");
            Assert.That(run.Deployed[0].Items, Is.Empty);
        }

        [Test]
        public void 없는_아이템은_팔_수_없다()
        {
            var run = NewRun();
            run.Currency = 5;

            Assert.That(NewShop().TrySellItem(run, "C1", "statBoost"), Is.False);
            Assert.That(run.Currency, Is.EqualTo(5));
        }

        [Test]
        public void 스킬을_파는_경로가_없다()
        {
            // ★ 팔 수 있으면 배타 규칙이 무너지고 빌드 공간 전수 탐색의 전제가 통째로 날아간다.
            //   "구현하지 않았다"를 테스트로 못박아 둔다 — 나중에 편의로 추가되는 걸 막는다.
            var methods = typeof(Shop).GetMethods().Select(m => m.Name).ToList();

            Assert.That(methods, Does.Not.Contain("TrySellSkill"));
            Assert.That(methods.Count(m => m.StartsWith("TrySell")), Is.EqualTo(2),
                "아이템·팀아이템 판매 둘뿐이어야 한다");
        }

        // ────────────────────────────── 결정론

        [Test]
        public void 같은_시드는_같은_재고를_준다()
        {
            var run = NewRun();

            string Stock(ulong seed)
            {
                var shop = NewShop();
                shop.Restock(run, Rng(seed), round: 2);
                return string.Join(",", shop.Offers.Select(o => o.Id));
            }

            Assert.That(Stock(7), Is.EqualTo(Stock(7)));
            Assert.That(Stock(7), Is.Not.EqualTo(Stock(8)), "시드가 다르면 재고도 달라야 한다");
        }
    }
}
