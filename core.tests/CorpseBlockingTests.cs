using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Events;
using NUnit.Framework;

namespace DomoNinja.Core.Tests
{
    /// <summary>
    /// 시체가 칸을 물고 있으면 안 된다 — 그리고 그걸 <b>불변식으로도</b> 잡는다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 원인: <see cref="Board"/> 의 칸 비우기가 <b>구현돼 있는데 호출부가 0 개</b>였다.
    /// 죽은 유닛이 <c>_occupant</c> 를 계속 물고, <see cref="Board.StepToward"/> 는
    /// 경로 탐색이 아니라 그리디 1스텝이라 우회로가 없고, 막히면 쿨다운도 안 쓰고 그 틱을 포기한다 —
    /// <b>자기 상태가 안 바뀌니 다음 틱에 똑같이 실패한다.</b>
    /// </para>
    /// <para>
    /// ⚠️ <b>이 테스트들은 수정 전 코드에서 반드시 실패해야 한다.</b> 통과하는 회귀 테스트는
    /// 회귀 테스트가 아니다. 아래 <c>시체가_길을_막으면_영원히_못_지나간다</c> 는
    /// 수정 전에 <c>AllyLoss</c>(서든데스로 아군 전멸)를 낸다.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class CorpseBlockingTests
    {
        private static CombatConfig Config()
        {
            var data = GameDataLoader.Load(RepoData.Characters, RepoData.Skills,
                                           RepoData.Encounters, RepoData.Economy, RepoData.Meta);
            return CombatConfig.From(data.Economy, tickRate: 20);
        }

        private static Unit Ally(int id, int x, int y, int hp, int atk, int range = 1) =>
            new Unit(id, Team.Ally, "C1", hp, atk, attackInterval: 5, range, moveInterval: 2,
                     new Coord(x, y));

        private static Unit Enemy(int id, int x, int y, int hp) =>
            new Unit(id, Team.Enemy, "slime", hp, attack: 0, attackInterval: 20, range: 1,
                     moveInterval: 5, new Coord(x, y), immobile: true);

        /// <summary>
        /// ★ 이 버그의 최소 재현. <b>일렬로 세워 우회로를 없앤다.</b>
        /// </summary>
        /// <remarks>
        /// 전원 y=2 한 줄에 둔다. 진영이 x 축으로 갈려 있어(`Coord.AllyMaxX`) 교전은 x 축이고,
        /// <c>|dx| ≥ |dy|</c> 면 x 로 간다 — dy=0 이면 <b>비켜갈 분기 자체가 없다.</b>
        /// 실전에서 시체 벽이 접전선에 세로로 쌓이는 상황과 같은 모양이다.
        ///
        /// 적은 <c>immobile</c> 에 공격력 0 이다. 적이 움직이면 표적 좌표가 변해
        /// <c>StepToward</c> 결과가 바뀌므로 <b>막힘이 우연히 풀릴 수 있다</b> —
        /// 그러면 무엇을 재는 테스트인지 흐려진다.
        /// </remarks>
        [Test]
        public void 시체가_길을_막으면_영원히_못_지나간다()
        {
            var sim = new BattleSimulator(Config());
            var sink = new ListEventSink();

            var hero = Ally(0, 0, 2, hp: 5000, atk: 50);
            var blocker = Enemy(1, 2, 2, hp: 1);    // 한 대에 죽어 (2,2) 에 시체가 된다
            var behind = Enemy(2, 4, 2, hp: 1);     // 그 시체 너머

            var result = sim.Run(new[] { hero, blocker, behind }, sink);

            Assert.That(result.Outcome, Is.EqualTo(BattleOutcome.AllyWin),
                "시체를 치우지 않으면 (2,2) 에서 막혀 서든데스로 아군이 죽는다");

            Assert.That(hero.At.X, Is.GreaterThanOrEqualTo(2),
                "시체가 있던 칸을 실제로 지나갔어야 한다");
        }

        /// <summary>
        /// 시체 정리가 <b>산 유닛을 지우지 않는지</b>. 가드가 없으면 여기서 깨진다.
        /// </summary>
        /// <remarks>
        /// 죽은 유닛의 <see cref="Unit.At"/> 는 죽은 자리를 계속 가리키고, 정리는 매 틱 다시 돈다.
        /// 좌표만으로 지우면 <b>그 칸에 걸어 들어온 산 유닛이 보드에서 사라진다</b> —
        /// 유닛은 살아 있는데 칸은 비어 있으니 다른 유닛이 같은 칸에 겹쳐 선다.
        /// 위 테스트는 이걸 못 잡는다(겹쳐도 이기기는 한다). 불변식이 잡는다.
        /// </remarks>
        [Test]
        public void 시체_정리가_그_칸에_들어온_산_유닛을_지우지_않는다()
        {
            bool before = BattleInvariants.Enabled;
            BattleInvariants.Enabled = true;
            try
            {
                var sim = new BattleSimulator(Config());

                var hero = Ally(0, 0, 2, hp: 5000, atk: 50);
                var blocker = Enemy(1, 2, 2, hp: 1);
                var behind = Enemy(2, 4, 2, hp: 1);

                Assert.DoesNotThrow(() => sim.Run(new[] { hero, blocker, behind },
                                                  NullEventSink.Instance));
            }
            finally
            {
                BattleInvariants.Enabled = before;
            }
        }

        /// <summary>
        /// 불변식 검사가 <b>실제로 위반을 잡는지</b>. 검사기 자체의 회귀 테스트다.
        /// </summary>
        /// <remarks>
        /// 항상 통과하는 검사는 검사가 아니다. 위반을 손으로 만들어 터지는 걸 확인한다 —
        /// 이게 없으면 <see cref="BattleInvariants.Verify"/> 가 조용히 무력해져도 아무도 모른다.
        /// </remarks>
        [Test]
        public void 불변식_검사는_시체_점유를_잡는다()
        {
            var board = new Board();
            var dead = new Unit(0, Team.Ally, "C1", 100, 10, 20, 1, 5, new Coord(2, 2));
            var alive = new Unit(1, Team.Enemy, "slime", 100, 10, 20, 1, 5, new Coord(4, 2));

            board.TryPlace(dead.Id, dead.At);
            board.TryPlace(alive.Id, alive.At);

            dead.Hp = 0;   // 죽었는데 보드에는 그대로 — 이게 그 버그의 상태다

            var ex = Assert.Throws<BattleInvariantException>(
                () => BattleInvariants.Verify(new[] { dead, alive }, board, tick: 7));

            Assert.That(ex!.Message, Does.Contain("죽은 유닛 0"));
            Assert.That(ex.Message, Does.Contain("(2,2)"));
        }

        /// <summary>불변식 검사가 <b>겹침</b>도 잡는지. 위 가드가 없을 때 실제로 생기는 상태다.</summary>
        [Test]
        public void 불변식_검사는_유닛_겹침을_잡는다()
        {
            var board = new Board();
            var a = new Unit(0, Team.Ally, "C1", 100, 10, 20, 1, 5, new Coord(2, 2));
            var b = new Unit(1, Team.Ally, "C1", 100, 10, 20, 1, 5, new Coord(2, 2));

            board.TryPlace(a.Id, a.At);
            // b 는 같은 칸이라 TryPlace 가 거부한다 — 보드에 없는 채로 좌표만 갖는다.

            Assert.Throws<BattleInvariantException>(
                () => BattleInvariants.Verify(new[] { a, b }, board, tick: 0));
        }

        /// <summary>
        /// <see cref="Board.Remove"/> 는 <b>주인이 맞을 때만</b> 비운다.
        /// </summary>
        /// <remarks>
        /// 좌표만 받는 오버로드를 두지 않은 이유가 이것이다 —
        /// "부르는 쪽이 확인하면 된다"는 확인을 잊을 수 있다는 뜻이고,
        /// 이 저장소는 이미 <b>있는데 아무도 안 부르는 메서드</b>로 한 번 당했다.
        /// </remarks>
        [Test]
        public void Remove_는_주인이_아니면_비우지_않는다()
        {
            var board = new Board();
            var c = new Coord(3, 1);

            board.TryPlace(7, c);

            Assert.That(board.Remove(9, c), Is.False, "9 번은 거기 없다");
            Assert.That(board.OccupantAt(c), Is.EqualTo(7), "남의 칸을 비우면 안 된다");

            Assert.That(board.Remove(7, c), Is.True);
            Assert.That(board.IsFree(c), Is.True);

            Assert.That(board.Remove(7, c), Is.False, "이미 빈 칸을 또 비울 수는 없다");
        }

        [Test]
        public void OccupiedCount_는_점유_칸_수다()
        {
            var board = new Board();
            Assert.That(board.OccupiedCount, Is.Zero);

            board.TryPlace(0, new Coord(1, 1));
            board.TryPlace(1, new Coord(2, 1));
            Assert.That(board.OccupiedCount, Is.EqualTo(2));

            board.Remove(0, new Coord(1, 1));
            Assert.That(board.OccupiedCount, Is.EqualTo(1));
        }
    }
}
