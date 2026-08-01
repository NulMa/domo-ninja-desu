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
    /// </remarks>
    public enum EventKind
    {
        None = 0,
        Move,
        Attack,
        Damage,
        Shield,
        Heal,
        Death,
        RoundEnd,
    }

    /// <summary>
    /// 이벤트 한 건. <b>참조 타입을 담지 않는다.</b>
    /// </summary>
    /// <remarks>
    /// 문자열이나 객체를 넣지 않는 이유 — 한 런에서 수만 건이 쌓이고,
    /// sim 은 이걸 수만 런 반복한다. 할당이 생기면 처리량이 그대로 깎인다.
    /// 표시용 문자열은 unity 가 Id 를 보고 만든다.
    /// </remarks>
    public readonly struct GameEvent
    {
        public readonly EventKind Kind;
        public readonly int Tick;
        public readonly int ActorId;
        public readonly int TargetId;

        /// <summary>의미는 <see cref="Kind"/> 에 따라 다르다. Damage=피해량, Heal=회복량, Move=목적지 OrderKey.</summary>
        public readonly int Value;

        public GameEvent(EventKind kind, int tick, int actorId, int targetId, int value)
        {
            Kind = kind;
            Tick = tick;
            ActorId = actorId;
            TargetId = targetId;
            Value = value;
        }

        public override string ToString() =>
            $"[{Tick}] {Kind} actor={ActorId} target={TargetId} value={Value}";
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
