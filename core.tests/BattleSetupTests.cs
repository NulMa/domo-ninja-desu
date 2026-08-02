using System.Linq;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Economy;
using NUnit.Framework;

namespace DomoNinja.Core.Tests
{
    /// <summary>표준 배치 (`08` §6.1 · `D-46`).</summary>
    [TestFixture]
    public class BattleSetupTests
    {
        private static GameData _data = null!;

        [OneTimeSetUp]
        public void Load() => _data = RepoData.LoadAll();

        private static RunState Run(params string[] characterIds) =>
            new RunEngine(_data, CombatConfig.From(_data.Economy, 20))
                .StartRun("S1", characterIds, new MetaProgress(_data.Meta));

        private static System.Collections.Generic.List<Unit> Build(RunState run) =>
            BattleSetup.Build(_data, run, _data.Rounds[0].Variants[0], new MetaProgress(_data.Meta));

        [Test]
        public void 사거리가_짧을수록_앞에_선다()
        {
            // C1 사거리 1 / C6 사거리 3 / C4 사거리 5
            var units = Build(Run("C4", "C6", "C1"));

            int X(string id) => units.First(u => u.TypeId == id).At.X;

            Assert.That(X("C1"), Is.GreaterThan(X("C6")), "근접이 가장 앞");
            Assert.That(X("C6"), Is.GreaterThan(X("C4")), "중거리가 가운데");
        }

        [Test]
        public void 배치_순서가_슬롯_인덱스를_바꾸지_않는다()
        {
            // ★ 위치는 사거리로 정하지만 순회 순서는 로스터 순서 그대로여야 한다.
            //   슬롯 인덱스가 배치에 끌려가면 모든 동률 판정의 기준이 같이 흔들린다.
            var units = Build(Run("C4", "C6", "C1"));
            var allies = units.Where(u => u.Team == Team.Ally).ToList();

            Assert.That(allies.Select(u => u.TypeId), Is.EqualTo(new[] { "C4", "C6", "C1" }));
            Assert.That(allies.Select(u => u.Id), Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void 스킬로_사거리가_바뀌면_배치도_따라간다()
        {
            // ★ 자리를 유닛 생성 시점에 정하면 이게 안 된다.
            //   C4-A 저격은 사거리를 5 → 7 로 올리므로 스킬을 반영한 뒤에야 앞뒤가 확정된다.
            var plain = Run("C4", "C1", "C6");
            var sniper = Run("C4", "C1", "C6");
            sniper.Deployed[0].ActiveSkillId = "C4-A";

            int XOf(RunState r) => Build(r).First(u => u.TypeId == "C4").At.X;

            Assert.That(XOf(sniper), Is.LessThanOrEqualTo(XOf(plain)),
                "사거리가 늘었으면 더 뒤이거나 같아야 한다");
            Assert.That(Build(sniper).First(u => u.TypeId == "C4").Range, Is.EqualTo(7));
        }

        [Test]
        public void 같은_사거리는_슬롯_인덱스로_앞뒤를_끊는다()
        {
            // List.Sort 는 불안정 정렬이라 동률을 안 끊으면 같은 입력에서도 순서가 뒤집힐 수 있다.
            // C1·C2·C3 은 전부 사거리 1 이다.
            var a = Build(Run("C1", "C2", "C3")).Where(u => u.Team == Team.Ally)
                                                .Select(u => $"{u.TypeId}@{u.At}").ToList();
            var b = Build(Run("C1", "C2", "C3")).Where(u => u.Team == Team.Ally)
                                                .Select(u => $"{u.TypeId}@{u.At}").ToList();

            Assert.That(b, Is.EqualTo(a));
            Assert.That(a[0], Does.Contain("(3,"), "첫 슬롯이 앞열");
        }

        [Test]
        public void 모두_자기_진영_안에_선다()
        {
            // 상대 진영에는 배치할 수 없다 (economy.placement.ownSideOnly).
            var units = Build(Run("C1", "C4", "C5"));

            foreach (var u in units.Where(u => u.Team == Team.Ally))
                Assert.That(u.At.IsAllyZone, Is.True, $"{u.TypeId}@{u.At}");
        }

        [Test]
        public void 아무도_겹치지_않는다()
        {
            var units = Build(Run("C1", "C2", "C3"));
            var keys = units.Select(u => u.At.OrderKey).ToList();

            Assert.That(keys.Distinct().Count(), Is.EqualTo(keys.Count));
        }

        [Test]
        public void 죽은_캐릭터는_보드에_오르지_않는다()
        {
            var run = Run("C1", "C2", "C4");
            run.Deployed[1].Hp = 0;

            var units = Build(run);

            Assert.That(units.Any(u => u.Team == Team.Ally && u.TypeId == "C2"), Is.False);
            Assert.That(units.Count(u => u.Team == Team.Ally), Is.EqualTo(2));
        }
    }
}
