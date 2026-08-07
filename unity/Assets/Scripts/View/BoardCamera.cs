using DomoNinja.Core.Domain;
using UnityEngine;

namespace DomoNinja.Unity.View
{
    /// <summary>
    /// 보드 8×6 이 <b>어떤 창 비율에서도</b> 화면에 들어오게 카메라를 잡는다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>웹 빌드라서 필요하다.</b> 심사자가 창을 어떤 크기로 열지 우리가 정하지 못한다.
    /// 직교 카메라의 가시 <b>폭</b>은 <c>orthographicSize × aspect</c> 라 세로 기준으로만 잡으면
    /// 창이 좁아질수록 좌우가 잘린다 — 보드 절반 폭이 4.0 이므로
    /// <c>aspect &lt; 4.2/4.2 = 1.0</c> 부근부터 <b>판이 화면 밖으로 나간다.</b>
    /// 세로가 가로보다 긴 창(모바일 세로가 기본)에서는 확정적으로 잘린다.
    /// </para>
    /// <para>
    /// 값이 <see cref="BattleViewBootstrap"/> 과 <c>GamePlayController</c> 에 <b>두 벌</b>로 있었다.
    /// 한쪽만 고치면 화면에 따라 프레이밍이 달라지는데, 그건 "어느 화면에서 봤느냐"에 따라
    /// 다르게 보이는 종류라 눈으로 찾기 어렵다. 여기 한 벌로 모은다 —
    /// 프레이밍은 보드 크기의 함수이고 보드 크기를 아는 건 이 어셈블리다.
    /// </para>
    /// <para>
    /// 창은 <b>언제든</b> 바뀔 수 있으므로 한 번 잡고 끝내지 않고, 해상도가 바뀌면 다시 잡는다.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Camera))]
    public sealed class BoardCamera : MonoBehaviour
    {
        /// <summary>세로 기준 여유. 보드 높이 절반은 3.0 이라 위아래로 넉넉하다.</summary>
        private const float MinHalfHeight = 4.2f;

        /// <summary>보드 절반 폭 + 여백. 칸 중심은 ±3.5 지만 칸 자체가 폭 1 이라 가장자리는 ±4.0 이다.</summary>
        private const float HalfWidth = Coord.BoardWidth * 0.5f + 0.2f;

        private static readonly Color Background = new Color(0.08f, 0.09f, 0.11f);

        private int _lastWidth;
        private int _lastHeight;

        /// <summary>지금 화면을 잡고 있는 카메라. 흔들기를 부를 곳이 필요해서 둔다.</summary>
        public static BoardCamera Active { get; private set; }

        private Vector2 _shakeDir;
        private float _shakeAmp;
        private float _shakeLeft;
        private float _shakeTotal;
        private float _shakeElapsed;

        /// <summary>흔들림이 오가는 빠르기(라디안/초). 낮으면 미끄러지고 높으면 지직거린다.</summary>
        private const float ShakeFrequency = 46f;

        private void OnEnable() => Active = this;

        private void OnDisable()
        {
            if (Active == this) Active = null;
            transform.position = new Vector3(0f, 0f, -10f);
        }

        /// <summary>
        /// 화면을 짧게 흔든다. <b>연출 전용이며 전투에 아무 영향이 없다.</b>
        /// </summary>
        /// <remarks>
        /// <para>
        /// 지시(사용자): "큰 스킬을 쓰거나, 적 처치, 혹은 아군 사망시에 화면 흔들림."
        /// </para>
        /// <para>
        /// ★ <b>난수 대신 한 방향 진동</b>이다. 매 프레임 아무 방향으로 튀게 하면
        /// 8×6 판의 픽셀 아트가 <b>읽히지 않는 지직거림</b>이 된다. 방향을 한 번 정하고
        /// 그 축으로만 오가면 <b>맞은 방향</b>이 읽히고, 같은 세기로도 훨씬 덜 어지럽다.
        /// </para>
        /// <para>
        /// 세기는 <b>월드 단위</b>다. 세로 반높이가 4.2 이므로 0.16 이면 화면 높이의 약 2%다 —
        /// 웹에서 심사자가 어떤 창 크기로 열든 비율이 같다.
        /// </para>
        /// <para>
        /// 이미 흔들리는 중이면 <b>센 쪽이 이긴다.</b> 더하면 여럿이 겹칠 때
        /// (광역기로 여럿이 동시에 죽는다) 진폭이 누적돼 화면이 튀어나간다.
        /// </para>
        /// </remarks>
        public static void Shake(float amplitude, float seconds)
        {
            var cam = Active;
            if (cam == null || amplitude <= 0f) return;

            if (cam._shakeLeft > 0f && cam._shakeAmp >= amplitude) return;

            cam._shakeDir = Random.insideUnitCircle.normalized;
            if (cam._shakeDir == Vector2.zero) cam._shakeDir = Vector2.right;

            cam._shakeAmp = amplitude;
            cam._shakeLeft = seconds;
            cam._shakeTotal = seconds;
            cam._shakeElapsed = 0f;
        }

        /// <summary>카메라를 보드에 맞춰 잡고, 창 크기가 바뀌어도 유지되게 해둔다.</summary>
        public static Camera Frame(Camera camera)
        {
            if (camera == null)
            {
                camera = new GameObject("Main Camera").AddComponent<Camera>();
                camera.tag = "MainCamera";
            }

            camera.orthographic = true;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.backgroundColor = Background;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.orthographicSize = SizeFor(camera.aspect);

            // 편집 모드에서도 부르므로(에디터 미리보기) 컴포넌트를 강제하지 않는다.
            if (Application.isPlaying && camera.GetComponent<BoardCamera>() == null)
                camera.gameObject.AddComponent<BoardCamera>();

            return camera;
        }

        /// <summary>가로가 좁으면 그만큼 물러선다. 넓으면 세로 기준을 그대로 쓴다.</summary>
        public static float SizeFor(float aspect) =>
            aspect <= 0f ? MinHalfHeight : Mathf.Max(MinHalfHeight, HalfWidth / aspect);

        private void LateUpdate()
        {
            // ★ 창 크기 검사보다 <b>앞</b>이다. 뒤에 두면 크기가 안 바뀐 프레임에 통째로 걸러져
            //   흔들림이 첫 프레임만 보이고 제자리로 못 돌아온다.
            TickShake();

            if (Screen.width == _lastWidth && Screen.height == _lastHeight) return;

            _lastWidth = Screen.width;
            _lastHeight = Screen.height;

            var camera = GetComponent<Camera>();
            camera.orthographicSize = SizeFor(camera.aspect);
        }

        private void TickShake()
        {
            if (_shakeLeft <= 0f) return;

            _shakeLeft -= Time.deltaTime;
            _shakeElapsed += Time.deltaTime;

            if (_shakeLeft <= 0f)
            {
                // ★ 반드시 정확히 제자리로 돌린다. 감쇠에 맡기면 <b>0 이 아닌 값에서 멈춰</b>
                //   판이 미세하게 어긋난 채 남는다 — 한 번은 안 보이지만 라운드마다 쌓인다.
                transform.position = new Vector3(0f, 0f, -10f);
                return;
            }

            float damp = _shakeLeft / _shakeTotal;
            float offset = _shakeAmp * damp * damp * Mathf.Sin(_shakeElapsed * ShakeFrequency);

            transform.position = new Vector3(_shakeDir.x * offset, _shakeDir.y * offset, -10f);
        }
    }
}
