using System;

namespace DomoNinja.Core.Domain
{
    /// <summary>
    /// 격자 좌표. <b>정수만 쓴다.</b>
    /// </summary>
    /// <remarks>
    /// 부동소수점을 쓰지 않는 것이 이 타입의 존재 이유다.
    /// float 좌표는 플랫폼·컴파일러에 따라 마지막 비트가 갈리고,
    /// 그러면 같은 시드로 돌린 sim 결과와 Unity 결과가 달라진다.
    /// 밸런스 검증 전체가 "시뮬 결과 == 실제 게임 결과" 위에 서 있으므로
    /// 여기서 결정론이 깨지면 그 위의 모든 수치가 의미를 잃는다. (05 §2.4)
    /// </remarks>
    public readonly struct Coord : IEquatable<Coord>
    {
        /// <summary>보드 가로. 아군 진영 x 0..3 / 적 진영 x 4..7</summary>
        public const int BoardWidth = 8;

        /// <summary>보드 세로 y 0..5</summary>
        public const int BoardHeight = 6;

        /// <summary>아군이 배치 가능한 x 상한(포함). 팀원 확정 A-3 — 자기 진영 4x6 만 배치 가능</summary>
        public const int AllyMaxX = 3;

        public readonly int X;
        public readonly int Y;

        public Coord(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool InBounds =>
            X >= 0 && X < BoardWidth && Y >= 0 && Y < BoardHeight;

        public bool IsAllyZone => InBounds && X <= AllyMaxX;

        /// <summary>
        /// 제곱 거리. <b>sqrt 를 쓰지 않는다.</b>
        /// </summary>
        /// <remarks>
        /// 사거리 판정은 전부 이 값과 임계값의 정수 비교로 한다.
        /// 거리 자체가 필요한 곳은 설계상 없다 — 필요해 보이면 그건
        /// 임계값을 제곱해두지 않았다는 뜻이다.
        /// </remarks>
        public int SqrDistanceTo(Coord other)
        {
            int dx = X - other.X;
            int dy = Y - other.Y;
            return dx * dx + dy * dy;
        }

        /// <summary>
        /// 결정론적 정렬 키. 타겟팅에서 거리가 동률일 때 쓴다.
        /// </summary>
        /// <remarks>
        /// ★ 동률 처리를 여기 둔 이유가 있다.
        /// 초기 구현에서 거리 동률 시 List 순회 순서에 의존해 결정론이 깨졌다.
        /// 순회 순서는 컬렉션 구현이 바뀌면 같이 바뀌므로 규칙이 될 수 없다.
        /// 동률은 반드시 좌표 자체로 끊는다.
        /// </remarks>
        public int OrderKey => Y * BoardWidth + X;

        public bool Equals(Coord other) => X == other.X && Y == other.Y;

        public override bool Equals(object? obj) => obj is Coord c && Equals(c);

        public override int GetHashCode() => (X * 397) ^ Y;

        public override string ToString() => $"({X},{Y})";

        public static bool operator ==(Coord a, Coord b) => a.Equals(b);

        public static bool operator !=(Coord a, Coord b) => !a.Equals(b);
    }
}
