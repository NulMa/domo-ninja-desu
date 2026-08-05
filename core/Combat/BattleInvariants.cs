// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using DomoNinja.Core.Domain;

namespace DomoNinja.Core.Combat
{
    /// <summary>불변식이 깨졌다. <b>이 예외는 잡지 않는다</b> — 잡으면 검사의 의미가 없다.</summary>
    public sealed class BattleInvariantException : Exception
    {
        public BattleInvariantException(string message) : base(message) { }
    }

    /// <summary>
    /// 매 틱 성립해야 하는 것들. <b>테스트가 못 잡는 부류를 잡으려고 있다.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>왜 테스트로 부족한가.</b> 이 파일이 생긴 계기는 <see cref="Board"/> 의
    /// 칸 비우기가 <b>구현돼 있는데 아무도 안 부르는</b> 상태로 남아 죽은 유닛이 칸을 영구 점유한
    /// 버그였다. 테스트는 <i>누군가 의심한 것</i>만 검사한다 — 아무도 그 메서드를 의심하지 않았으니
    /// 359개 테스트 중 한 개도 걸리지 않았다. 컴파일러도 public 메서드라 침묵했다.
    /// <b>불변식은 반대 방향이다.</b> "무엇이 틀렸을까"가 아니라 "무엇이 항상 참인가"를 적고,
    /// 이미 돌고 있는 21,600 런이 그걸 전수로 두들긴다. <b>실행량이 그대로 탐지력이 된다.</b>
    /// </para>
    /// <para>
    /// ★ <b>지표로는 안 잡힌다.</b> 그 버그의 유일한 흔적은 `M7`(타임아웃) 이었는데
    /// 목표가 &lt;5% 라 1.67% 는 <b>합격</b>으로 읽혔다. 서든데스는 반드시 누군가를 죽이므로
    /// 타임아웃은 구조상 0 에 가까워야 한다 — 즉 그 1.67% 는 <i>합격이지만 설명이 필요한 숫자</i>였다.
    /// 집계 지표는 임계값만 말하고 이유를 묻지 않는다. 불변식은 그 자리에서 터진다.
    /// </para>
    /// <para>
    /// ★ <b>장르에 묶이지 않는다.</b> 여기 적힌 건 "8×6 격자 오토배틀러의 규칙"이 아니라
    /// <i>상태를 두 군데 두면 어긋난다</i>는 일반 명제의 이 게임 버전이다.
    /// 본선에서 장르가 바뀌어도 <b>이 파일의 자리는 그대로 남는다</b> (`05` §1.6).
    /// </para>
    /// </remarks>
    public static class BattleInvariants
    {
        /// <summary>
        /// 검사를 켠다. <b>기본은 꺼짐</b> — 전수 탐색(21,600런)의 처리량 예산을 지킨다.
        /// </summary>
        /// <remarks>
        /// static 인 이유는 <b>진단 스위치라서</b>다. 전투 파라미터가 아니므로
        /// <see cref="CombatConfig"/>(전부 `economy.json` 에서 온다) 에 넣으면 규칙과 디버그가 섞인다.
        /// 시작할 때 한 번 켜고 이후 읽기만 하므로 <c>Parallel.For</c> 와도 안전하다 —
        /// <b>런 도중에 바꾸지 말 것.</b>
        /// </remarks>
        public static bool Enabled;

        /// <summary>
        /// 틱 시작 시점(시체 정리 <b>직후</b>)에 성립해야 하는 것들.
        /// </summary>
        /// <remarks>
        /// 부르는 자리가 규칙의 일부다. 시체 정리 전에 부르면 ②가 정상 상태에서도 터진다 —
        /// <b>"틱 시작 시점에 죽어 있으면 보드에 없다"</b> 가 검사하려는 그 규칙이기 때문이다.
        /// </remarks>
        public static void Verify(IReadOnlyList<Unit> units, Board board, int tick)
        {
            StringBuilder? bad = null;

            int alive = 0;

            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];

                // ① HP 는 범위 안. 음수 HP 는 IsAlive 가 걸러주지만, 최대치 초과는
                //    아무도 안 본다 — 회복 상한이 새면 "안 죽는 유닛"이 조용히 생긴다.
                if (u.Hp > u.MaxHp)
                    Add(ref bad, $"유닛 {u.Id} HP 초과: {u.Hp}/{u.MaxHp}");

                if (u.Hp < 0)
                    Add(ref bad, $"유닛 {u.Id} HP 음수: {u.Hp}");

                // ② 보호막은 음수가 될 수 없다. RevokeShield 가 자기 몫보다 많이 거두면 여기서 걸린다.
                if (u.Shield < 0)
                    Add(ref bad, $"유닛 {u.Id} 보호막 음수: {u.Shield}");

                if (!u.IsAlive)
                {
                    // ③ 시체는 보드에 없다. ★ 이 버그가 여기서 잡힌다.
                    if (board.OccupantAt(u.At) == u.Id)
                        Add(ref bad, $"죽은 유닛 {u.Id} 가 {u.At} 를 점유 중");

                    continue;
                }

                alive++;

                // ④ 좌표가 보드 안.
                if (!board.InBounds(u.At))
                {
                    Add(ref bad, $"유닛 {u.Id} 가 보드 밖: {u.At}");
                    continue;
                }

                // ⑤ 산 유닛은 자기 칸을 점유한다. 유닛의 At 와 보드의 _occupant 는
                //    같은 사실을 두 군데 적어둔 것이라 어긋날 수 있다 — 어긋나는 순간
                //    이동은 A 를 보고 충돌 판정은 B 를 보게 된다.
                int owner = board.OccupantAt(u.At);
                if (owner != u.Id)
                    Add(ref bad, $"유닛 {u.Id} 는 {u.At} 에 있다는데 그 칸 주인은 {Name(owner)}");
            }

            // ⑥ 점유 칸 수 == 생존 수. ⑤ 가 통과했다면 생존 수만큼은 점유돼 있으므로,
            //    이 검사가 잡는 건 <b>주인 없는 점유</b>다 — 유닛 목록 어디에도 없는 id 가
            //    칸을 물고 있는 경우. ⑤ 를 유닛에서 보드로 보는 검사라면 이건 반대 방향이다.
            int occupied = board.OccupiedCount;
            if (occupied != alive)
                Add(ref bad, $"점유 칸 {occupied} 개 ≠ 생존 유닛 {alive} 명");

            if (bad != null)
                throw new BattleInvariantException($"[틱 {tick}] 불변식 위반\n{bad}");
        }

        private static void Add(ref StringBuilder? sb, string line)
        {
            sb ??= new StringBuilder();
            sb.Append("  - ").Append(line).Append('\n');
        }

        private static string Name(int occupant)
            => occupant == Board.Empty ? "없음(빈 칸)" : $"유닛 {occupant}";
    }
}
