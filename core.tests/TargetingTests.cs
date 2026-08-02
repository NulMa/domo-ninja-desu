using System.Collections.Generic;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Events;
using NUnit.Framework;

namespace DomoNinja.Core.Tests
{
    /// <summary>타겟팅 — 어그로 &gt; 우선순위 &gt; 슬롯 인덱스 (`_schema` §3 · `08` §5.2).</summary>
    [TestFixture]
    public class TargetingTests
    {
        private static Unit At(int id, int x, int y, int hp = 100, int maxHp = 100, int range = 1)
        {
            var u = new Unit(id, Team.Enemy, "e", maxHp, attack: 10, attackInterval: 20,
                             range: range, moveInterval: 5, at: new Coord(x, y));
            u.Hp = hp;
            return u;
        }

        private static Unit Me(int range = 1, int x = 0, int y = 0) =>
            new Unit(99, Team.Ally, "C1", 100, 10, 20, range, 5, new Coord(x, y));

        [Test]
        public void 기본은_최근접이다()
        {
            var me = Me();
            var candidates = new List<Unit> { At(0, 5, 0), At(1, 2, 0), At(2, 7, 0) };

            Assert.That(Targeting.SelectTarget(me, candidates)!.Id, Is.EqualTo(1));
        }

        [Test]
        public void 거리_동률은_슬롯_인덱스로_끊는다()
        {
            // ★ 이게 없으면 어느 쪽을 때리는지가 리스트 순회 순서로 결정된다.
            //   순회 순서는 컬렉션 구현이 바뀌면 같이 바뀌므로 규칙이 될 수 없다.
            var me = Me();
            var candidates = new List<Unit> { At(3, 2, 0), At(1, 0, 2), At(2, 2, 0) };

            // 셋 다 제곱거리 4. 가장 작은 id 가 골라져야 한다.
            Assert.That(Targeting.SelectTarget(me, candidates)!.Id, Is.EqualTo(1));
        }

        [Test]
        public void 후보_순서를_바꿔도_같은_대상이_나온다()
        {
            var me = Me();
            var forward = new List<Unit> { At(1, 2, 0), At(2, 2, 0), At(3, 0, 2) };
            var reversed = new List<Unit> { At(3, 0, 2), At(2, 2, 0), At(1, 2, 0) };

            Assert.That(Targeting.SelectTarget(me, reversed)!.Id,
                        Is.EqualTo(Targeting.SelectTarget(me, forward)!.Id));
        }

        [Test]
        public void 죽은_유닛은_고르지_않는다()
        {
            var me = Me();
            var dead = At(0, 1, 0, hp: 0);
            var alive = At(1, 5, 0);

            Assert.That(Targeting.SelectTarget(me, new List<Unit> { dead, alive })!.Id, Is.EqualTo(1));
        }

        [Test]
        public void 살아있는_후보가_없으면_null()
        {
            var me = Me();
            Assert.That(Targeting.SelectTarget(me, new List<Unit> { At(0, 1, 0, hp: 0) }), Is.Null);
            Assert.That(Targeting.SelectTarget(me, new List<Unit>()), Is.Null);
        }

        // ────────────────────────────── 어그로

        [Test]
        public void 어그로_대상을_먼저_노린다()
        {
            var me = Me(range: 5);
            var near = At(0, 1, 0);
            var taunter = At(1, 4, 0);
            taunter.Status.Apply(new StatusEffect(StatusKind.Taunt, StatusEffect.Never, valueA: 3000));

            Assert.That(Targeting.SelectTarget(me, new List<Unit> { near, taunter })!.Id, Is.EqualTo(1),
                "더 가까운 적이 있어도 어그로가 우선한다");
        }

        [Test]
        public void 사거리_밖의_어그로는_무시한다()
        {
            // ★ 안 그러면 적이 닿지도 않는 대상만 바라보며 제자리에 서고 전투가 타임아웃까지 간다.
            var me = Me(range: 2);
            var near = At(0, 1, 0);
            var farTaunter = At(1, 7, 0);
            farTaunter.Status.Apply(new StatusEffect(StatusKind.Taunt, StatusEffect.Never, valueA: 3000));

            Assert.That(Targeting.SelectTarget(me, new List<Unit> { near, farTaunter })!.Id, Is.EqualTo(0));
        }

        [Test]
        public void 어그로_세기가_큰_쪽을_고른다()
        {
            var me = Me(range: 5);
            var weak = At(0, 1, 0);
            var strong = At(1, 4, 0);
            weak.Status.Apply(new StatusEffect(StatusKind.Taunt, StatusEffect.Never, valueA: 2000));
            strong.Status.Apply(new StatusEffect(StatusKind.Taunt, StatusEffect.Never, valueA: 3000));

            Assert.That(Targeting.SelectTarget(me, new List<Unit> { weak, strong })!.Id, Is.EqualTo(1));
        }

        [Test]
        public void 어그로_세기가_같으면_가까운_쪽을_고른다()
        {
            // 동률에서 ② 로 내려가면 비어그로 대상이 선택될 수 있고, 그러면 어그로의 의미가 사라진다.
            var me = Me(range: 5);
            var far = At(0, 4, 0);
            var near = At(1, 2, 0);
            far.Status.Apply(new StatusEffect(StatusKind.Taunt, StatusEffect.Never, valueA: 3000));
            near.Status.Apply(new StatusEffect(StatusKind.Taunt, StatusEffect.Never, valueA: 3000));

            Assert.That(Targeting.SelectTarget(me, new List<Unit> { far, near })!.Id, Is.EqualTo(1));
        }

        [Test]
        public void 어그로_보유자가_죽으면_평범한_규칙으로_내려간다()
        {
            var me = Me(range: 5);
            var taunter = At(0, 4, 0, hp: 0);
            var other = At(1, 3, 0);
            taunter.Status.Apply(new StatusEffect(StatusKind.Taunt, StatusEffect.Never, valueA: 3000));

            Assert.That(Targeting.SelectTarget(me, new List<Unit> { taunter, other })!.Id, Is.EqualTo(1));
        }

        // ────────────────────────────── 우선순위

        [Test]
        public void 최저HP_는_절대값이_아니라_비율로_고른다()
        {
            // 체력 180 인 탱커(90 = 50%)와 80 인 마법사(60 = 75%).
            // 절대값이면 마법사가 골라지지만 실제로 위험한 건 탱커다.
            var me = Me(range: 7);
            var tank = At(0, 3, 0, hp: 90, maxHp: 180);
            var mage = At(1, 4, 0, hp: 60, maxHp: 80);

            var picked = Targeting.SelectTarget(me, new List<Unit> { tank, mage }, TargetPriority.LowestHp);
            Assert.That(picked!.Id, Is.EqualTo(0));
        }

        [Test]
        public void 최원거리는_가장_먼_적을_고른다()
        {
            // C4-A 저격. 후열이 사라지면서 backline_first 가 넘겨받은 의미다.
            var me = Me(range: 7);
            var candidates = new List<Unit> { At(0, 2, 0), At(1, 6, 0), At(2, 4, 0) };

            Assert.That(Targeting.SelectTarget(me, candidates, TargetPriority.Farthest)!.Id, Is.EqualTo(1));
        }

        [Test]
        public void 우선순위가_동률이어도_슬롯_인덱스로_끊는다()
        {
            var me = Me(range: 7);
            var a = At(5, 3, 0, hp: 50);
            var b = At(2, 4, 0, hp: 50);

            Assert.That(Targeting.SelectTarget(me, new List<Unit> { a, b }, TargetPriority.LowestHp)!.Id,
                        Is.EqualTo(2));
        }
    }
}
