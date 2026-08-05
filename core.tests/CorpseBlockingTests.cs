using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Events;
using NUnit.Framework;

namespace DomoNinja.Core.Tests
{
    /// <summary>
    /// 시체가 칸을 물고 있으면 안 된다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 원인: <see cref="Board"/> 의 칸 비우기가 <b>구현돼 있는데 호출부가 0 개</b>였다.
    /// 죽은 유닛이 <c>_occupant</c> 를 계속 물고, <see cref="Board.StepToward"/> 는
    /// 경로 탐색이 아니라 그리디 1스텝이라 우회로가 없고, 막히면 쿨다운도 안 쓰고 그 틱을 포기한다 —
    /// <b>자기 상태가 안 바뀌니 다음 틱에 똑같이 실패한다.</b>
    /// </para>
    /// <para>
    /// ⚠️ <b>이 테스트는 수정 전 코드에서 반드시 실패해야 한다.</b> 통과하는 회귀 테스트는
    /// 회귀 테스트가 아니다. 실제로 수정 전에는 <c>AllyLoss</c>(서든데스로 아군 전멸)가 나온다.
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
        /// <see cref="Board.Remove"/> 는 <b>주인이 맞을 때만</b> 비운다.
        /// </summary>
        /// <remarks>
        /// 좌표만 받는 오버로드를 두지 않은 이유가 이것이다. 시체 치우기는 죽은 유닛에 대해
        /// 매 틱 다시 도는데 그 유닛의 <see cref="Unit.At"/> 는 죽은 자리를 계속 가리킨다.
        /// 이미 비워진 그 칸에 산 유닛이 걸어 들어온 뒤 좌표만으로 또 지우면
        /// <b>산 유닛이 보드에서 사라진다</b> — 유닛은 살아 있는데 칸은 비었으니 겹쳐 서게 된다.
        ///
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
    }
}
