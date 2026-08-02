using System.Linq;
using DomoNinja.Core.Data;
using DomoNinja.Core.Economy;
using DomoNinja.Core.Skills;
using NUnit.Framework;

namespace DomoNinja.Core.Tests
{
    /// <summary>메타 프로그레션 (`meta.json` · `08` §4.5).</summary>
    [TestFixture]
    public class MetaProgressTests
    {
        private static GameData _data = null!;

        [OneTimeSetUp]
        public void Load() => _data = GameDataLoader.Load(
            RepoData.Characters, RepoData.Skills, RepoData.Encounters, RepoData.Economy, RepoData.Meta);

        private static MetaProgress New() => new MetaProgress(_data.Meta);

        [Test]
        public void 강화가_없으면_스탯_보정이_0이다()
        {
            var meta = New();

            Assert.That(meta.DeltaPermilleFor(StatKey.Attack), Is.EqualTo(0));
            Assert.That(meta.DeltaPermilleFor(StatKey.Hp), Is.EqualTo(0));
            Assert.That(meta.RoundEndHealBonusPermille, Is.EqualTo(0));
            Assert.That(meta.CurrencyOnWinBonus, Is.EqualTo(0));
        }

        [Test]
        public void 레벨은_곱이_아니라_합으로_쌓인다()
        {
            // M-ATK 레벨당 +4%. 3레벨이면 1.04³(12.5%)가 아니라 12% 다.
            var meta = New();
            meta.SetLevel("M-ATK", 3);

            Assert.That(meta.DeltaPermilleFor(StatKey.Attack), Is.EqualTo(120));
        }

        [Test]
        public void 최대_레벨을_넘길_수_없다()
        {
            var meta = New();
            meta.SetLevel("M-ATK", 99);

            Assert.That(meta.LevelOf("M-ATK"), Is.EqualTo(5));
            Assert.That(meta.DeltaPermilleFor(StatKey.Attack), Is.EqualTo(200), "최대 +20%");
        }

        [Test]
        public void 간격_강화는_음수_방향이다()
        {
            // M-HASTE 는 레벨당 -3%. 낮을수록 빠르다 (_schema §2).
            var meta = New();
            meta.SetLevel("M-HASTE", 4);

            Assert.That(meta.DeltaPermilleFor(StatKey.AttackInterval), Is.EqualTo(-120));
        }

        [Test]
        public void 회복과_재화는_비율이_아니라_절대값이다()
        {
            // ★ M-HEAL 은 +50‰, M-GOLD 는 +1 이다. 스탯 강화와 같은 함수로 다루면
            //   50 이 5% 로 읽혀 회복량이 통째로 틀린다.
            var meta = New();
            meta.SetLevel("M-HEAL", 4);
            meta.SetLevel("M-GOLD", 3);

            Assert.That(meta.RoundEndHealBonusPermille, Is.EqualTo(200), "기본 40% → 60%");
            Assert.That(meta.CurrencyOnWinBonus, Is.EqualTo(3));
        }

        [Test]
        public void 측정점_비율은_절삭한다()
        {
            // meta50 = allLevelsRatio 0.5. maxLevel 5 에서 2 다.
            // 반올림하면 "절반"보다 세져서 측정점 이름과 실제가 어긋난다.
            var meta = New();
            meta.SetAllLevelsRatio(0.5);

            Assert.That(meta.LevelOf("M-ATK"), Is.EqualTo(2));
            Assert.That(meta.LevelOf("M-MOVE"), Is.EqualTo(1), "maxLevel 3 의 절반");
        }

        [Test]
        public void 만렙_측정점은_전부_최대다()
        {
            var meta = New();
            meta.SetAllLevelsRatio(1.0);

            foreach (var u in _data.Meta.Upgrades)
                Assert.That(meta.LevelOf(u.Id), Is.EqualTo(u.MaxLevel), u.Id);
        }

        [Test]
        public void 재화가_모자라면_사지_못한다()
        {
            var meta = New();
            meta.Currency = 7;   // M-ATK 1레벨 비용은 8

            Assert.That(meta.TryUpgrade("M-ATK"), Is.False);
            Assert.That(meta.LevelOf("M-ATK"), Is.EqualTo(0));
            Assert.That(meta.Currency, Is.EqualTo(7), "실패한 구매가 재화를 깎으면 안 된다");
        }

        [Test]
        public void 사면_재화가_빠지고_레벨이_오른다()
        {
            var meta = New();
            meta.Currency = 30;

            Assert.That(meta.TryUpgrade("M-ATK"), Is.True);
            Assert.That(meta.Currency, Is.EqualTo(22));
            Assert.That(meta.NextCost("M-ATK"), Is.EqualTo(14), "다음 레벨은 더 비싸다");
        }

        [Test]
        public void 만렙이면_더_살_수_없다()
        {
            var meta = New();
            meta.SetLevel("M-MOVE", 3);
            meta.Currency = 9999;

            Assert.That(meta.NextCost("M-MOVE"), Is.Null);
            Assert.That(meta.TryUpgrade("M-MOVE"), Is.False);
        }

        [Test]
        public void 패배해도_도달_라운드만큼은_받는다()
        {
            // 실패한 런도 다음 런을 위한 진전이 되는 게 이 장르의 핵심이다.
            // 0 을 주면 연패했을 때 진행이 멈춘다.
            var meta = New();

            Assert.That(meta.EarnedFrom(roundsCleared: 5, runCleared: false), Is.EqualTo(10));
            Assert.That(meta.EarnedFrom(roundsCleared: 8, runCleared: true), Is.EqualTo(26),
                "전승 시 8*2 + 10 = 26 (meta.json _maxPerRun)");
        }

        [Test]
        public void 스테이지는_처음에_하나만_열려_있다()
        {
            var meta = New();

            Assert.That(meta.IsUnlocked("S1"), Is.True);
            Assert.That(meta.IsUnlocked("S2"), Is.False);

            meta.UnlockStage("S2");
            meta.UnlockStage("S2");
            Assert.That(meta.UnlockedStages.Count(s => s == "S2"), Is.EqualTo(1), "중복 등록되지 않는다");
        }

        [Test]
        public void 전_항목_만렙_비용이_문서와_맞는다()
        {
            // meta.json 이 "전 항목 만렙 = 558 (검증됨)" 이라고 적어뒀다.
            // 값이 바뀌면 _runsToMax(35~50런) 도 같이 틀려지므로 여기서 잡는다.
            int total = _data.Meta.Upgrades.Sum(u => u.Costs.Take(u.MaxLevel).Sum());

            Assert.That(total, Is.EqualTo(558));
        }
    }
}
