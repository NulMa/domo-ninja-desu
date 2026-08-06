using DomoNinja.Core.Events;
using UnityEngine;

namespace DomoNinja.Unity.View
{
    /// <summary>
    /// <see cref="BattleLog"/> 를 시간축으로 재생한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>재생은 전투 결과에 영향을 줄 수 없다</b> (`08` §5.5).
    /// 로그가 이미 다 나와 있으므로 core 를 다시 부르지 않는다 —
    /// 되감기·배속·일시정지가 전부 이 안에서 끝난다.
    /// 연출이 전투 진행을 멈추거나 늦출 수 있으면 sim 결과와 게임 결과가 갈라진다.
    /// </para>
    /// <para>
    /// 같은 틱의 이벤트가 여러 건이면 <b>리스트 순서가 곧 인과 순서</b>다. 재정렬하지 않는다.
    /// </para>
    /// </remarks>
    public sealed class BattleReplayer : MonoBehaviour
    {
        /// <summary>1초당 틱. `A-2` 고정값이며 재생 속도와는 무관하다.</summary>
        private const float TicksPerSecond = 20f;

        /// <summary>`23` §2.2 의 <c>StatusKind</c> 중 도발(어그로). 숫자를 그대로 쓰지 않으려고 이름을 준다.</summary>
        private const int TauntStatusKind = 8;

        [Tooltip("재생 배속. 로그는 그대로고 보는 속도만 바뀐다.")]
        [SerializeField] private float _speed = 1f;

        private BoardView _board;
        private BattleLog _log;
        private int _cursor;
        private float _playhead;
        private bool _playing;

        public bool IsPlaying => _playing;

        /// <summary>재생 배속. UI 의 배속 버튼이 이걸 바꾼다.</summary>
        public float Speed
        {
            get => _speed;
            set => _speed = value;
        }

        /// <summary>재생이 끝난 뒤에도 마지막 상태는 화면에 남는다.</summary>
        public bool IsFinished => _log != null && _cursor >= _log.Events.Count;

        public void Bind(BoardView board) => _board = board;

        public void Play(BattleLog log)
        {
            _log = log;
            _cursor = 0;
            _playhead = 0f;
            _playing = true;

            _board.Setup(log);
        }

        public void Stop() => _playing = false;

        private void Update()
        {
            if (!_playing || _log == null) return;

            _playhead += Time.deltaTime * TicksPerSecond * Mathf.Max(0.01f, _speed);

            // ★ 전에는 여기서 색을 통째로 지웠다. 그러면 번쩍임이 **그 프레임 안에서만** 살아 있어
            //   60fps 기준 16ms 만 보인다 — 연출이 없는 것과 화면상 같다. 시간으로 되돌린다.
            _board.TickFlashes(Time.deltaTime);
            _board.TickAnimations(Time.deltaTime);

            // 지나간 틱의 이벤트를 순서대로 소비한다.
            while (_cursor < _log.Events.Count && _log.Events[_cursor].Tick <= _playhead)
            {
                Apply(_log.Events[_cursor]);
                _cursor++;
            }

            if (_cursor >= _log.Events.Count) _playing = false;
        }

        /// <remarks>
        /// <c>Aux</c> 는 언제나 <b>적용 후 값</b>이다 (`23` §2.1).
        /// 여기서 빼거나 더하면 피해 규칙이 View 에 복제되고, 복제된 규칙은 반드시 갈라진다.
        /// </remarks>
        private void Apply(in GameEvent e)
        {
            switch (e.Kind)
            {
                case EventKind.Move:
                    _board.MoveTo(e.ActorId, e.Value);
                    break;

                case EventKind.Attack:
                    _board.FlashAttack(e.ActorId, e.TargetId);
                    break;

                // ★ 맞은 것과 회복한 것을 **다른 그림**으로 낸다.
                //   전에는 둘 다 SetHp 하나만 불러서, 체력 막대가 오르내리는 것 말고는
                //   화면에 아무 차이가 없었다 — 영상에서 회복이 회복으로 안 읽힌다.
                case EventKind.Damage:
                    _board.SetHp(e.TargetId, e.Aux);
                    _board.FlashDamage(e.TargetId);
                    break;

                case EventKind.Heal:
                    _board.SetHp(e.TargetId, e.Aux);
                    _board.FlashHeal(e.TargetId);
                    break;

                // Aux 는 적용 후 보호막 총량이다(`23` §2.1). 여기서 더하거나 빼지 않는다.
                case EventKind.Shield:
                    _board.SetShield(e.TargetId, e.Aux);
                    break;

                case EventKind.StatusApply:
                    if (e.Value == TauntStatusKind) _board.SetTaunt(e.TargetId, true);
                    break;

                case EventKind.StatusExpire:
                    if (e.Value == TauntStatusKind) _board.SetTaunt(e.TargetId, false);
                    break;

                case EventKind.SuddenDeath:
                    _board.SetSuddenDeath(true);
                    break;

                case EventKind.Death:
                    _board.SetDead(e.TargetId);
                    break;

                // 나머지 상태(약화·도트·무적·재생·둔화·속박)는 아직 아이콘이 없다.
                // 어떤 그림을 쓸지는 연출 결정이라 팀원 몫이고, 로그에는 이미 다 들어와 있다.
            }
        }
    }
}
