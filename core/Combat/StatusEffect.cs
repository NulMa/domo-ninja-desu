// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System.Collections.Generic;
using DomoNinja.Core.Events;

namespace DomoNinja.Core.Combat
{
    /// <summary>
    /// 유닛에 걸린 상태이상 1건. <b>8종뿐이다</b> (`_schema` §3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <see cref="StatusKind"/> 를 <c>Events</c> 네임스페이스 것을 그대로 쓴다.
    /// 전투용 enum 을 따로 만들면 <b>같은 8종이 두 벌 생기고</b>, 둘 사이 매핑을 어딘가에서
    /// 손으로 유지해야 한다. 그 매핑이 하나만 틀려도 화면에는 다른 상태가 뜬다.
    /// 이벤트 로그 포맷은 이미 동결됐으므로(`23`) 그쪽을 정본으로 삼는다.
    /// </para>
    /// <para>
    /// 파라미터를 <see cref="ValueA"/>·<see cref="ValueB"/> 두 개로 묶은 이유 —
    /// 종류마다 필요한 값이 다른데(보호막은 획득량·상한, 둔화는 배율 하나) 종류별 타입을 만들면
    /// 8개 클래스와 그만큼의 할당이 생긴다. 한 전투에 상태가 수백 번 걸리고 sim 은 그걸 수만 런 반복한다.
    /// 의미는 아래 표에 고정한다.
    /// </para>
    /// </remarks>
    public readonly struct StatusEffect
    {
        public readonly StatusKind Kind;

        /// <summary>이 틱에 도달하면 사라진다. <b>절대 틱이다.</b> 무기한이면 <see cref="Never"/>.</summary>
        public readonly int ExpireTick;

        /// <summary>
        /// 종류별 첫 번째 값. 전부 <b>천분율 정수</b>다.
        /// <list type="bullet">
        /// <item><c>Weaken</c> — 공격력 배율</item>
        /// <item><c>DotRamping</c> — 초기 피해량(천분율 아님, 절대값)</item>
        /// <item><c>Regen</c> — 회복 천분율</item>
        /// <item><c>Slow</c> — 이동 간격 배율</item>
        /// <item><c>Shield</c> — 남은 보호막 상한</item>
        /// <item><c>Taunt</c> — 위협 배율</item>
        /// <item><c>Invulnerable</c> · <c>Root</c> — 쓰지 않는다</item>
        /// </list>
        /// </summary>
        public readonly int ValueA;

        /// <summary>
        /// 종류별 두 번째 값.
        /// <list type="bullet">
        /// <item><c>Weaken</c> — 받는 피해 배율</item>
        /// <item><c>DotRamping</c> — 틱당 증가분(천분율)</item>
        /// <item><c>Regen</c> — 발동 주기(틱)</item>
        /// <item>나머지 — 0</item>
        /// </list>
        /// </summary>
        public readonly int ValueB;

        /// <summary>건 쪽의 유닛 번호. 없으면 -1. <c>Taunt</c> 대상 판정과 로그에 쓴다.</summary>
        public readonly int SourceUnitId;

        /// <summary>
        /// 걸린 틱. <c>DotRamping</c> 이 경과 시간을 재는 데 쓴다.
        /// </summary>
        /// <remarks>
        /// 남은 시간(<see cref="ExpireTick"/>)에서 역산하지 않는 이유 — 무기한 상태는 만료 틱이 없고,
        /// 같은 종류를 다시 걸면 지속시간이 새로 시작되므로 역산값이 조용히 튄다.
        /// </remarks>
        public readonly int AppliedTick;

        /// <summary>만료되지 않는다는 뜻. 전투가 끝날 때까지 유지된다.</summary>
        public const int Never = int.MaxValue;

        public StatusEffect(StatusKind kind, int expireTick, int valueA = 0, int valueB = 0,
                            int sourceUnitId = -1, int appliedTick = 0)
        {
            Kind = kind;
            ExpireTick = expireTick;
            ValueA = valueA;
            ValueB = valueB;
            SourceUnitId = sourceUnitId;
            AppliedTick = appliedTick;
        }

        public bool IsExpiredAt(int tick) => tick >= ExpireTick;

        public override string ToString() =>
            $"{Kind}(a={ValueA} b={ValueB} ~{(ExpireTick == Never ? "∞" : ExpireTick.ToString())})";
    }

    /// <summary>
    /// 한 유닛에 걸린 상태이상 전부.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>List 다. Dictionary 가 아니다.</b> 종류가 8개뿐이라 선형 탐색이 해시보다 빠르고,
    /// 무엇보다 <b>순회 순서가 삽입 순서로 고정된다.</b> Dictionary 순회 순서에 의존하면
    /// 구현이 바뀔 때 같이 바뀌는데, 이 프로젝트에서 그건 성능이 아니라 결정론 문제다 (`_schema` §7).
    /// </para>
    /// <para>
    /// 같은 종류를 다시 걸면 <b>누적하지 않고 갱신한다.</b>
    /// 누적하게 두면 둔화 배율이 곱해지며 발산하고(1.35² = 1.82), 그건 합연산 규칙(§8)과도 어긋난다.
    /// 세기를 올리는 건 스킬 강화의 몫이지 중첩의 몫이 아니다.
    /// </para>
    /// </remarks>
    public sealed class StatusSet
    {
        private readonly List<StatusEffect> _effects = new List<StatusEffect>();

        public int Count => _effects.Count;

        public IReadOnlyList<StatusEffect> All => _effects;

        /// <summary>건다. 같은 종류가 이미 있으면 <b>교체한다</b>(지속시간도 새로 시작).</summary>
        public void Apply(StatusEffect effect)
        {
            for (int i = 0; i < _effects.Count; i++)
            {
                if (_effects[i].Kind == effect.Kind)
                {
                    _effects[i] = effect;
                    return;
                }
            }
            _effects.Add(effect);
        }

        public bool Has(StatusKind kind)
        {
            for (int i = 0; i < _effects.Count; i++)
                if (_effects[i].Kind == kind) return true;
            return false;
        }

        public bool TryGet(StatusKind kind, out StatusEffect effect)
        {
            for (int i = 0; i < _effects.Count; i++)
            {
                if (_effects[i].Kind == kind)
                {
                    effect = _effects[i];
                    return true;
                }
            }
            effect = default;
            return false;
        }

        public bool Remove(StatusKind kind)
        {
            for (int i = 0; i < _effects.Count; i++)
            {
                if (_effects[i].Kind == kind)
                {
                    _effects.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public void Clear() => _effects.Clear();

        /// <summary>
        /// 만료된 것을 걷어내고 <b>무엇이 사라졌는지</b> <paramref name="expired"/> 에 담는다.
        /// </summary>
        /// <remarks>
        /// 사라진 목록을 돌려주는 이유 — View 가 <see cref="EventKind.StatusExpire"/> 를 받아야
        /// 아이콘을 지운다. 조용히 없애면 화면에 상태 아이콘이 영원히 남는다.
        /// 뒤에서부터 훑는 것은 제거로 인덱스가 밀리지 않게 하기 위함이고, 그래서
        /// <paramref name="expired"/> 의 순서는 <b>역순</b>이다 — 순서에 의미를 두지 말 것.
        /// </remarks>
        public void ExpireAt(int tick, List<StatusKind> expired)
        {
            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                if (_effects[i].IsExpiredAt(tick))
                {
                    expired.Add(_effects[i].Kind);
                    _effects.RemoveAt(i);
                }
            }
        }

        /// <summary>이동할 수 있는가. <c>Root</c> 는 이동만 막고 공격은 막지 않는다 (`_schema` §3).</summary>
        public bool CanMove => !Has(StatusKind.Root);

        /// <summary>공격력 보정. 합연산에 넣을 증감분이다.</summary>
        public int AttackDeltaPermille =>
            TryGet(StatusKind.Weaken, out var w) ? w.ValueA - Permille.One : 0;

        /// <summary>받는 피해 보정.</summary>
        public int DamageTakenDeltaPermille =>
            TryGet(StatusKind.Weaken, out var w) && w.ValueB != 0 ? w.ValueB - Permille.One : 0;

        /// <summary>이동 간격 보정. <c>Slow</c> 는 간격을 <b>늘린다</b>(느려진다).</summary>
        public int MoveIntervalDeltaPermille =>
            TryGet(StatusKind.Slow, out var s) ? s.ValueA - Permille.One : 0;

        /// <summary>어그로 세기. 없으면 0 이고, 클수록 먼저 노려진다.</summary>
        public int ThreatPermille =>
            TryGet(StatusKind.Taunt, out var t) ? t.ValueA : 0;
    }
}
