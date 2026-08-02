using DomoNinja.Core.Domain;
using NUnit.Framework;

namespace DomoNinja.Core.Tests
{
    [TestFixture]
    public class BoardTests
    {
        [Test]
        public void 한_칸에_두_체가_들어가지_않는다()
        {
            var board = new Board();

            Assert.That(board.TryPlace(0, new Coord(2, 2)), Is.True);
            Assert.That(board.TryPlace(1, new Coord(2, 2)), Is.False);
            Assert.That(board.OccupantAt(new Coord(2, 2)), Is.EqualTo(0), "실패한 배치가 기존 점유를 덮으면 안 된다");
        }

        [Test]
        public void 목표_칸이_차_있으면_그_틱은_정지한다()
        {
            // _schema §7.1 — "먼저 처리된 유닛이 점유하고, 나중 유닛은 그 틱 정지".
            // 이 규칙이 없으면 같은 틱에 두 유닛이 겹쳐 서고 이후 전개가 시드마다 갈린다.
            var board = new Board();
            var blocked = new Coord(3, 2);

            board.TryPlace(0, blocked);
            board.TryPlace(1, new Coord(2, 2));

            Assert.That(board.TryMove(1, new Coord(2, 2), blocked), Is.False);
            Assert.That(board.OccupantAt(new Coord(2, 2)), Is.EqualTo(1), "이동에 실패했으면 제자리에 있어야 한다");
        }

        [Test]
        public void 이동하면_출발_칸이_비워진다()
        {
            var board = new Board();
            board.TryPlace(7, new Coord(1, 1));

            Assert.That(board.TryMove(7, new Coord(1, 1), new Coord(2, 1)), Is.True);
            Assert.That(board.IsFree(new Coord(1, 1)), Is.True);
            Assert.That(board.OccupantAt(new Coord(2, 1)), Is.EqualTo(7));
        }

        [Test]
        public void 남의_유닛_번호로는_움직일_수_없다()
        {
            var board = new Board();
            board.TryPlace(3, new Coord(0, 0));

            Assert.That(board.TryMove(4, new Coord(0, 0), new Coord(1, 0)), Is.False);
        }

        [Test]
        public void 큰_축을_먼저_줄인다()
        {
            // dx=5, dy=2 → x 축 우선
            Assert.That(Board.StepToward(new Coord(0, 0), new Coord(5, 2)), Is.EqualTo(new Coord(1, 0)));

            // dx=1, dy=4 → y 축 우선
            Assert.That(Board.StepToward(new Coord(0, 0), new Coord(1, 4)), Is.EqualTo(new Coord(0, 1)));
        }

        [Test]
        public void 동률이면_x축으로_고정한다()
        {
            // ★ 동률을 규칙으로 못 박지 않으면 여기가 플랫폼·컴파일러에 따라 갈릴 수 있다.
            //    같은 시드로 sim 과 Unity 가 다른 결과를 내는 전형적인 지점이다 (A-1).
            Assert.That(Board.StepToward(new Coord(0, 0), new Coord(3, 3)), Is.EqualTo(new Coord(1, 0)));
            Assert.That(Board.StepToward(new Coord(4, 4), new Coord(2, 2)), Is.EqualTo(new Coord(3, 4)));
        }

        [Test]
        public void 도착해_있으면_제자리다()
        {
            var here = new Coord(3, 3);
            Assert.That(Board.StepToward(here, here), Is.EqualTo(here));
        }

        [Test]
        public void 보드_밖은_점유도_이동도_안_된다()
        {
            var board = new Board();

            Assert.That(board.TryPlace(0, new Coord(-1, 0)), Is.False);
            Assert.That(board.TryPlace(0, new Coord(8, 0)), Is.False);
            Assert.That(board.OccupantAt(new Coord(99, 99)), Is.EqualTo(Board.Empty));
        }
    }

    [TestFixture]
    public class UnitTests
    {
        private static Unit Make(int id, int maxHp = 100, int range = 1, int x = 0, int y = 0) =>
            new Unit(id, Team.Ally, "C1", maxHp, attack: 10, attackInterval: 20,
                     range: range, moveInterval: 5, at: new Coord(x, y));

        [Test]
        public void 사거리_판정에_제곱거리를_쓴다()
        {
            var a = Make(0, range: 4, x: 0, y: 0);
            var b = Make(1, x: 3, y: 2);   // 제곱거리 13, range² = 16

            Assert.That(a.InRangeOf(b), Is.True);

            var far = Make(2, x: 3, y: 3); // 제곱거리 18 > 16
            Assert.That(a.InRangeOf(far), Is.False);
        }

        [Test]
        public void HP_비율은_천분율_정수다()
        {
            // 절대값으로 비교하면 최대 체력이 낮은 유닛만 계속 회복 대상이 된다 (_schema §3 lowestHpAlly).
            var tank = Make(0, maxHp: 180);
            var mage = Make(1, maxHp: 80);

            tank.Hp = 90;   // 50%
            mage.Hp = 60;   // 75%

            Assert.That(tank.HpPermille, Is.EqualTo(500));
            Assert.That(mage.HpPermille, Is.EqualTo(750));
            Assert.That(tank.HpPermille, Is.LessThan(mage.HpPermille),
                "절대값(90 > 60)으로는 탱커가 더 건강해 보이지만 비율로는 반대다");
        }

        [Test]
        public void HP_가_0_이면_죽은_것이다()
        {
            var u = Make(0);
            u.Hp = 0;

            Assert.That(u.IsAlive, Is.False);
            Assert.That(u.HpPermille, Is.EqualTo(0));
        }
    }

    [TestFixture]
    public class RunStateTests
    {
        private static RunState Make(int lives = 3)
        {
            var roster = new[]
            {
                new RosterEntry("C1", 120),
                new RosterEntry("C3", 90),
                new RosterEntry("C6", 110),
            };
            return new RunState("S1", lives, startingCurrency: 0, deployed: roster);
        }

        [Test]
        public void 생명이_0_이면_런이_끝난다()
        {
            var run = Make(lives: 0);
            Assert.That(run.IsOver, Is.True);
        }

        [Test]
        public void 전원_사망이면_런이_끝난다()
        {
            var run = Make();
            foreach (var r in run.Deployed) r.Hp = 0;

            Assert.That(run.IsOver, Is.True);
        }

        [Test]
        public void 한_명이라도_살아_있으면_계속된다()
        {
            var run = Make();
            run.Deployed[0].Hp = 0;
            run.Deployed[1].Hp = 0;

            Assert.That(run.IsOver, Is.False);
        }

        [Test]
        public void 배타_제거_목록은_순서를_보존한다()
        {
            // HashSet 이었다면 순회 순서가 구현에 따라 달라지고, 그 순서에 의존하는
            // 상점 추첨이 같은 시드에서 다른 결과를 낸다 (_schema §7).
            var run = Make();
            run.RemovedSkillIds.Add("C1-B");
            run.RemovedSkillIds.Add("C3-A");

            Assert.That(run.RemovedSkillIds, Is.EqualTo(new[] { "C1-B", "C3-A" }));
        }

        [Test]
        public void 런_재화와_영구_재화는_같은_필드에_담기지_않는다()
        {
            // 섞이면 "런 안에서 잘하기"와 "런을 반복하기"가 같은 자원을 두고 경쟁해
            // 밸런스 해석이 불가능해진다. 타입이 갈라져 있다는 것 자체가 계약이다.
            var run = Make();
            run.Currency = 12;

            Assert.That(typeof(RunState).GetProperty("MetaCurrency"), Is.Null,
                "RunState 에 영구 재화 필드가 생기면 화폐 분리 계약이 깨진다 — meta 쪽에 둔다");
        }
    }
}
