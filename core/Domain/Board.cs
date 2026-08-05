// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System;

namespace DomoNinja.Core.Domain
{
    /// <summary>
    /// 8×6 격자의 <b>점유 상태</b>. 한 칸에 한 체.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>Dictionary 가 아니라 배열이다.</b> 좌표가 0~47 의 연속 정수(<see cref="Coord.OrderKey"/>)라
    /// 배열이 그대로 완전한 인덱스가 된다. Dictionary 를 쓰면 순회 순서가 구현에 따라 달라지는데,
    /// 이 프로젝트에서 그건 성능 문제가 아니라 <b>결정론 문제</b>다 (`_schema` §7).
    /// </para>
    /// <para>
    /// 유닛 객체가 아니라 <b>유닛 번호</b>를 담는다. 보드는 "누가 어디 있나"만 알면 되고,
    /// 유닛의 수명은 전투 쪽이 관리한다. 참조를 양쪽에 두면 죽은 유닛이 보드에 남는 종류의 버그가 생긴다.
    /// </para>
    /// </remarks>
    public sealed class Board
    {
        /// <summary>빈 칸.</summary>
        public const int Empty = -1;

        private readonly int[] _occupant;

        public int Width { get; }
        public int Height { get; }

        public Board(int width = Coord.BoardWidth, int height = Coord.BoardHeight)
        {
            Width = width;
            Height = height;
            _occupant = new int[width * height];
            Clear();
        }

        public void Clear()
        {
            for (int i = 0; i < _occupant.Length; i++) _occupant[i] = Empty;
        }

        /// <summary>그 칸의 유닛 번호. 비었으면 <see cref="Empty"/>.</summary>
        public int OccupantAt(Coord c) => InBounds(c) ? _occupant[c.OrderKey] : Empty;

        public bool IsFree(Coord c) => InBounds(c) && _occupant[c.OrderKey] == Empty;

        public bool InBounds(Coord c) => c.X >= 0 && c.X < Width && c.Y >= 0 && c.Y < Height;

        /// <summary>배치. 이미 차 있으면 false 를 돌려주고 아무것도 하지 않는다.</summary>
        public bool TryPlace(int unitId, Coord c)
        {
            if (!IsFree(c)) return false;
            _occupant[c.OrderKey] = unitId;
            return true;
        }

        /// <summary>
        /// 1칸 이동. <b>목표 칸이 차 있으면 실패하고 그 틱은 정지한다</b> (`_schema` §7.1).
        /// </summary>
        /// <remarks>
        /// "먼저 처리된 유닛이 점유"라는 규칙이 여기서 성립한다 —
        /// 매 틱 슬롯 인덱스 오름차순으로 부르므로, 먼저 부른 쪽이 칸을 가져간다.
        /// 순서를 바꾸면 같은 시드에서 다른 결과가 나온다.
        /// </remarks>
        public bool TryMove(int unitId, Coord from, Coord to)
        {
            if (!InBounds(from) || !InBounds(to)) return false;
            if (_occupant[from.OrderKey] != unitId) return false;
            if (!IsFree(to)) return false;

            _occupant[from.OrderKey] = Empty;
            _occupant[to.OrderKey] = unitId;
            return true;
        }

        /// <summary>
        /// 그 칸을 비운다 — <b><paramref name="unitId"/> 가 실제로 거기 있을 때만.</b>
        /// 비웠으면 true.
        /// </summary>
        /// <remarks>
        /// <para>
        /// ★ <b>좌표만 받는 버전을 두지 않는다.</b> 시체 치우기는 죽은 유닛에 대해 매 틱 다시 도는데,
        /// 그 유닛의 <see cref="Unit.At"/> 는 죽은 자리를 계속 가리킨다. 이미 비워진 그 칸에
        /// 산 유닛이 걸어 들어온 뒤 좌표만으로 다시 지우면 <b>산 유닛이 보드에서 사라진다</b> —
        /// 유닛은 살아 있는데 칸은 비어 있으니 다른 유닛이 같은 칸에 겹쳐 선다.
        /// </para>
        /// <para>
        /// 호출부가 <c>if (OccupantAt(c) == id)</c> 를 앞에 붙이면 되는 일이지만,
        /// <b>붙이는 걸 잊을 수 있는 형태로 두지 않는다.</b> 이 파일이 겪은 버그가 정확히
        /// "있는데 아무도 안 부르는 메서드"였다. 잘못 부를 수 있는 메서드도 같은 부류다.
        /// </para>
        /// </remarks>
        public bool Remove(int unitId, Coord c)
        {
            if (!InBounds(c) || _occupant[c.OrderKey] != unitId) return false;
            _occupant[c.OrderKey] = Empty;
            return true;
        }

        /// <summary>
        /// 목표를 향한 <b>그리디 1스텝</b> 목적지. 경로 탐색은 하지 않는다 (`_schema` §7.1).
        /// </summary>
        /// <remarks>
        /// dx/dy 중 차이가 큰 축을 먼저 줄이고, <b>동률이면 x 축</b>으로 고정한다.
        /// 동률을 규칙으로 못 박지 않으면 컴파일러·플랫폼에 따라 갈릴 수 있는 지점이라
        /// 여기서만 결정한다. 막혀 있는지 여부는 이 함수가 보지 않는다 —
        /// 판단은 <see cref="TryMove"/> 가 하고, 실패하면 그 틱은 정지다.
        /// </remarks>
        public static Coord StepToward(Coord from, Coord to)
        {
            int dx = to.X - from.X;
            int dy = to.Y - from.Y;

            if (dx == 0 && dy == 0) return from;

            if (Math.Abs(dx) >= Math.Abs(dy))
                return new Coord(from.X + Math.Sign(dx), from.Y);

            return new Coord(from.X, from.Y + Math.Sign(dy));
        }
    }
}
