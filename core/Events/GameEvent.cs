// Unity 는 Directory.Build.props 를 읽지 않는다 — asmdef 컴파일 경로가 다르다.
// 그래서 nullable 문맥이 dotnet 쪽에만 켜지고 Unity 쪽에는 꺼진 채로 갈라졌고,
// 실제로 WebGL 빌드에서 CS8632 로 드러났다. 파일마다 명시해 두 컴파일러를 맞춘다.
#nullable enable

using System.Collections.Generic;

namespace DomoNinja.Core.Events
{
    /// <summary>
    /// core 가 뱉는 이벤트의 종류. <b>core 와 unity 가 만나는 유일한 지점이다.</b> (19 §2.5)
    /// </summary>
    /// <remarks>
    /// core 는 전투를 계산하고 이 로그만 남긴다. unity 는 로그를 재생만 한다.
    /// 이 경계가 지켜지면 연출이 전투 시간에 영향을 줄 수 없고,
    /// 그래야 sim 결과와 Unity 결과가 갈라지지 않는다.
    /// 어기면 밸런스 검증 전체가 무너진다.
    ///
    /// ★ 이 enum 의 값은 <b>고정이다.</b> 팀원이 이 포맷에 맞춰 View 를 짜고 있으므로
    /// 중간에 항목을 끼워 넣어 번호가 밀리면 상대 코드가 조용히 틀린 걸 그린다.
    /// 추가는 반드시 <b>맨 뒤에</b>. 자세한 계약은 docs/23_이벤트로그_포맷.md.
    /// </remarks>
    public enum EventKind
    {
        None = 0,

        /// <summary>1칸 이동. Value = 도착 <see cref="Domain.Coord.OrderKey"/>, Aux = 출발 OrderKey.</summary>
        Move = 1,

        /// <summary>공격/스킬 발동 모션. Value = 0(평타) 또는 스킬 인덱스, Aux = 0. 피해는 별도 Damage 로 온다.</summary>
        Attack = 2,

        /// <summary>피해 적용. Value = 피해량, Aux = <b>적용 후 HP</b>.</summary>
        Damage = 3,

        /// <summary>보호막 총량 변화(부여·차감·소멸 전부). Value = 변화량(차감이면 음수), Aux = <b>적용 후 보호막 총량</b>.</summary>
        Shield = 4,

        /// <summary>회복. Value = 실제 회복량(상한 절삭 후), Aux = <b>적용 후 HP</b>.</summary>
        Heal = 5,

        /// <summary>사망. Value = 0, Aux = 0.</summary>
        Death = 6,

        /// <summary>전투(=라운드) 종료. Actor/Target = -1, Value = 1 승 / 0 패, Aux = 0.</summary>
        RoundEnd = 7,

        /// <summary>상태이상 부여·갱신. Value = <see cref="StatusKind"/>, Aux = <b>만료 틱(절대값)</b>.</summary>
        StatusApply = 8,

        /// <summary>상태이상 해제. Value = <see cref="StatusKind"/>, Aux = 0.</summary>
        StatusExpire = 9,

        /// <summary>서든데스 진입. 전투당 최대 1회. Actor/Target = -1, Value = 0, Aux = 0. (08 §5.3)</summary>
        SuddenDeath = 10,

        /// <summary>
        /// 피격 무효 (<c>C3-A</c> 그림자). Value = 0, Aux = <b>현재 HP</b>(안 깎였음을 그대로 확인 가능).
        /// </summary>
        /// <remarks>
        /// ★ <b>v1 동결(2026-08-02) 이후 처음 늘어난 항목이다</b> (D+4 포맷 리뷰).
        /// <b>맨 뒤에 붙였으므로 1~10 번호는 하나도 안 밀린다</b> — 동결이 실제로 지키려던 것이 그것이다.
        /// <para>
        /// 전에는 <see cref="Damage"/> 에 Value 0 을 실어 표현했다. 그러면 <b>View 가 회피를 그릴 수 없고</b>,
        /// *"피해 0"* 과 구분하려면 앞뒤 이벤트를 조합해 읽어야 하는 <b>묵시적 규칙</b>이 생긴다.
        /// 회피는 실제 기믹이고 60초 영상에서 보여야 하므로 이름을 준다.
        /// </para>
        /// <para>
        /// 모르는 항목을 만난 재생기는 <b>그냥 흘린다</b>(<c>BattleReplayer</c> 의 <c>switch</c> 에 해당 case 가 없으면 무시).
        /// 그래서 View 대응은 이 커밋과 분리해도 화면이 깨지지 않는다.
        /// </para>
        /// </remarks>
        Dodge = 11,
    }

    /// <summary>
    /// 상태이상 8종 (08 §5, 19 §6.3). <see cref="EventKind.StatusApply"/> 의 Value 에 실린다.
    /// </summary>
    /// <remarks>번호 고정. 추가는 맨 뒤에만.</remarks>
    public enum StatusKind
    {
        None = 0,
        Weaken = 1,
        DotRamping = 2,
        Invulnerable = 3,
        Regen = 4,
        Slow = 5,
        Root = 6,
        Shield = 7,
        Taunt = 8,
    }

    /// <summary>
    /// 이벤트 한 건. <b>참조 타입을 담지 않는다.</b>
    /// </summary>
    /// <remarks>
    /// 문자열이나 객체를 넣지 않는 이유 — 한 런에서 수만 건이 쌓이고,
    /// sim 은 이걸 수만 런 반복한다. 할당이 생기면 처리량이 그대로 깎인다.
    /// 표시에 필요한 이름·스프라이트는 <see cref="BattleLog.Units"/> 헤더가 전투당 한 번만 전달한다.
    ///
    /// ★ <see cref="Aux"/> 를 따로 둔 이유가 설계의 핵심이다.
    /// Damage 가 피해량만 실어 보내면 unity 는 남은 HP 를 스스로 빼서 계산해야 하고,
    /// 그 순간 <b>피해 규칙(보호막 우선 차감·정수 절삭)이 View 에 복제된다.</b>
    /// 복제된 규칙은 반드시 갈라지고, 갈라지면 화면과 sim 이 다른 값을 말한다.
    /// 그래서 상태를 바꾸는 이벤트는 전부 Aux 에 <b>적용 후 값</b>을 싣는다 — unity 는 그리기만 한다.
    /// </remarks>
    public readonly struct GameEvent
    {
        public readonly EventKind Kind;

        /// <summary>고정 틱. 20틱 = 1초 (08 §5.2).</summary>
        public readonly int Tick;

        /// <summary>행위 주체 <see cref="UnitSpec.UnitId"/>. 주체가 없는 이벤트는 -1.</summary>
        public readonly int ActorId;

        /// <summary>대상 <see cref="UnitSpec.UnitId"/>. 대상이 없으면 -1.</summary>
        public readonly int TargetId;

        /// <summary>의미는 <see cref="Kind"/> 마다 다르다. 각 항목 주석 참조.</summary>
        public readonly int Value;

        /// <summary>상태를 바꾸는 이벤트에서는 <b>적용 후 값</b>. 그 외는 항목 주석 참조.</summary>
        public readonly int Aux;

        public GameEvent(EventKind kind, int tick, int actorId, int targetId, int value, int aux = 0)
        {
            Kind = kind;
            Tick = tick;
            ActorId = actorId;
            TargetId = targetId;
            Value = value;
            Aux = aux;
        }

        public override string ToString() =>
            $"[{Tick}] {Kind} actor={ActorId} target={TargetId} value={Value} aux={Aux}";
    }

    /// <summary>이벤트를 받는 쪽. core 는 누가 받는지 모른다.</summary>
    public interface IEventSink
    {
        void Emit(in GameEvent e);
    }

    /// <summary>
    /// 아무것도 하지 않는 싱크. <b>sim 이 쓴다.</b>
    /// </summary>
    /// <remarks>
    /// 로그를 끄는 것이 sim 처리량의 전제다(19 §2.5).
    /// null 검사로 처리하지 않고 싱크를 갈아끼우는 이유는,
    /// null 검사를 빠뜨린 곳이 생기면 그 지점만 조용히 로그를 남기고
    /// 처리량이 왜 떨어졌는지 찾기 어려워지기 때문이다.
    /// </remarks>
    public sealed class NullEventSink : IEventSink
    {
        public static readonly NullEventSink Instance = new NullEventSink();

        private NullEventSink() { }

        public void Emit(in GameEvent e) { }
    }

    /// <summary>메모리에 쌓는 싱크. Unity 재생과 테스트가 쓴다.</summary>
    public sealed class ListEventSink : IEventSink
    {
        private readonly List<GameEvent> _events = new List<GameEvent>();

        public IReadOnlyList<GameEvent> Events => _events;

        public void Emit(in GameEvent e) => _events.Add(e);

        public void Clear() => _events.Clear();
    }
}
