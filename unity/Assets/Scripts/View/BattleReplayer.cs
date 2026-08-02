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

        [Tooltip("재생 배속. 로그는 그대로고 보는 속도만 바뀐다.")]
        [SerializeField] private float _speed = 1f;

        private BoardView _board;
        private BattleLog _log;
        private int _cursor;
        private float _playhead;
        private bool _playing;

        public bool IsPlaying => _playing;

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
            _board.ClearFlash();

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

                case EventKind.Damage:
                case EventKind.Heal:
                    _board.SetHp(e.TargetId, e.Aux);
                    break;

                case EventKind.Death:
                    _board.SetDead(e.TargetId);
                    break;

                // Shield·StatusApply·StatusExpire·SuddenDeath 는 아직 그리지 않는다.
                // 아이콘·게이지는 연출 영역이라 팀원 몫이고, 여기서 임의로 만들면
                // 나중에 두 벌이 된다. 로그에는 이미 다 들어와 있다.
            }
        }
    }
}
