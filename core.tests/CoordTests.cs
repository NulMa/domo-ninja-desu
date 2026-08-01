using DomoNinja.Core.Domain;
using NUnit.Framework;

namespace DomoNinja.Core.Tests
{
    [TestFixture]
    public class CoordTests
    {
        [Test]
        public void 제곱거리는_정수다()
        {
            var a = new Coord(0, 0);
            var b = new Coord(3, 4);

            // 실제 거리는 5 지만 우리는 25 를 쓴다. sqrt 를 쓸 일이 없어야 한다.
            Assert.That(a.SqrDistanceTo(b), Is.EqualTo(25));
        }

        [Test]
        public void 제곱거리는_대칭이다()
        {
            var a = new Coord(1, 5);
            var b = new Coord(6, 2);

            Assert.That(b.SqrDistanceTo(a), Is.EqualTo(a.SqrDistanceTo(b)));
        }

        [Test]
        public void 사거리_판정은_제곱값_비교로_한다()
        {
            var self = new Coord(2, 2);

            // 사거리 1 -> 임계값 1 (상하좌우만). 대각선은 2 라 닿지 않는다.
            const int rangeSqr = 1;

            Assert.That(self.SqrDistanceTo(new Coord(3, 2)), Is.LessThanOrEqualTo(rangeSqr));
            Assert.That(self.SqrDistanceTo(new Coord(3, 3)), Is.GreaterThan(rangeSqr));
        }

        [TestCase(0, 0, true)]
        [TestCase(7, 5, true)]
        [TestCase(8, 0, false)]
        [TestCase(0, 6, false)]
        [TestCase(-1, 0, false)]
        public void 보드_범위_판정(int x, int y, bool expected)
        {
            Assert.That(new Coord(x, y).InBounds, Is.EqualTo(expected));
        }

        [TestCase(0, 0, true)]
        [TestCase(3, 5, true)]
        [TestCase(4, 0, false)]
        public void 아군_배치_구역은_x_3까지다(int x, int y, bool expected)
        {
            // 팀원 확정 A-3 — 자기 진영 4x6 만 배치 가능
            Assert.That(new Coord(x, y).IsAllyZone, Is.EqualTo(expected));
        }

        [Test]
        public void OrderKey_는_좌표마다_유일하다()
        {
            var seen = new System.Collections.Generic.HashSet<int>();

            for (int y = 0; y < Coord.BoardHeight; y++)
            for (int x = 0; x < Coord.BoardWidth; x++)
            {
                Assert.That(seen.Add(new Coord(x, y).OrderKey), Is.True,
                    $"({x},{y}) 의 OrderKey 가 중복이다 — 동률을 끊지 못한다.");
            }

            Assert.That(seen.Count, Is.EqualTo(Coord.BoardWidth * Coord.BoardHeight));
        }
    }
}
