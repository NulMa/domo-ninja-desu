// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System.Collections.Generic;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Events;

namespace DomoNinja.Core.Combat
{
    /// <summary>표적 선택 기준. `targeting` 템플릿의 <c>priority</c> 에 대응한다 (`_schema` §3).</summary>
    public enum TargetPriority
    {
        /// <summary>기본값. 가장 가까운 적.</summary>
        Nearest = 0,

        /// <summary>현재 HP <b>비율</b>이 가장 낮은 적. 절대값이 아니다.</summary>
        LowestHp = 1,

        /// <summary>가장 먼 적. 후열이 사라지면서 `backline_first` 가 넘겨받은 의미다 (`A-3`).</summary>
        Farthest = 2,
    }

    /// <summary>
    /// 표적을 고른다. <b>동률을 남기지 않는 것이 이 타입의 전부다.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 우선순위는 `_schema` §3 이 못박아뒀다:
    /// <code>
    /// ① 어그로(taunt) 보유 대상 중 threatMult 가 가장 높은 쪽
    /// ② 동률이거나 어그로 보유자가 없으면 → priority 기준 (기본 최근접)
    /// ③ 그래도 동률이면 → 슬롯 인덱스 오름차순
    /// </code>
    /// </para>
    /// <para>
    /// ★ <b>③ 이 없으면 결정론이 깨진다.</b> 거리가 같은 적이 둘이면 어느 쪽을 때리는지가
    /// 후보 리스트의 순회 순서로 결정되는데, 그 순서는 컬렉션 구현이 바뀌면 같이 바뀐다.
    /// 초기 구현에서 실제로 겪은 종류라 <see cref="Coord.OrderKey"/> 와 같은 이유로 여기 못박는다.
    /// </para>
    /// <para>
    /// ★ <b>사거리 밖의 어그로 대상은 무시한다.</b> 안 그러면 적이 닿지도 않는 대상만 바라보며
    /// 제자리에 서고, 전투가 타임아웃까지 간다. 어그로는 "누구를 먼저 때릴까"를 바꾸는 규칙이지
    /// "누구에게 다가갈까"를 바꾸는 규칙이 아니다.
    /// </para>
    /// </remarks>
    public static class Targeting
    {
        /// <summary>
        /// <paramref name="self"/> 가 때릴 대상. 살아 있는 후보가 없으면 <c>null</c>.
        /// </summary>
        /// <param name="candidates">상대 진영 유닛. <b>슬롯 인덱스 오름차순으로 들어와야 한다.</b></param>
        public static Unit? SelectTarget(Unit self, IReadOnlyList<Unit> candidates,
                                         TargetPriority priority = TargetPriority.Nearest)
        {
            // ① 어그로 — 사거리 안에 있는 것만 본다
            Unit? aggro = null;
            int bestThreat = 0;

            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (!c.IsAlive) continue;

                int threat = c.Status.ThreatPermille;
                if (threat <= 0) continue;
                if (!self.InRangeOf(c)) continue;

                // 동률이면 ② 로 내려가지 않고 여기서 최근접 → 슬롯 인덱스로 끊는다.
                // 어그로 보유자가 둘일 때 비어그로 대상이 선택되면 어그로의 의미가 사라진다.
                if (aggro == null || threat > bestThreat || (threat == bestThreat && Closer(self, c, aggro)))
                {
                    aggro = c;
                    bestThreat = threat;
                }
            }

            if (aggro != null) return aggro;

            // ② 우선순위 기준
            Unit? best = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (!c.IsAlive) continue;
                if (best == null || Better(self, c, best, priority)) best = c;
            }

            return best;
        }

        /// <summary><paramref name="a"/> 가 <paramref name="b"/> 보다 나은가. <b>동률은 슬롯 인덱스로 끊는다.</b></summary>
        private static bool Better(Unit self, Unit a, Unit b, TargetPriority priority)
        {
            switch (priority)
            {
                case TargetPriority.LowestHp:
                {
                    // 비율로 비교한다. 절대값으로 하면 체력 180 인 수도승과 80 인 주술사 중
                    // 항상 주술사만 골라진다 (`_schema` §3 lowestHpAlly 와 같은 이유).
                    int ha = a.HpPermille, hb = b.HpPermille;
                    if (ha != hb) return ha < hb;
                    break;
                }
                case TargetPriority.Farthest:
                {
                    int da = self.At.SqrDistanceTo(a.At), db = self.At.SqrDistanceTo(b.At);
                    if (da != db) return da > db;
                    break;
                }
                default:
                {
                    int da = self.At.SqrDistanceTo(a.At), db = self.At.SqrDistanceTo(b.At);
                    if (da != db) return da < db;
                    break;
                }
            }

            // ③ 슬롯 인덱스 오름차순. 여기서 반드시 갈린다 — id 는 유일하다.
            return a.Id < b.Id;
        }

        private static bool Closer(Unit self, Unit a, Unit b)
        {
            int da = self.At.SqrDistanceTo(a.At), db = self.At.SqrDistanceTo(b.At);
            if (da != db) return da < db;
            return a.Id < b.Id;
        }

        /// <summary>어그로를 걸어 둔 유닛인지. <see cref="StatusKind.Taunt"/> 하나로 판정한다.</summary>
        public static bool HasThreat(Unit u) => u.Status.ThreatPermille > 0;
    }
}
