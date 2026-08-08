// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using UnityEngine;

namespace DomoNinja.Unity
{
    /// <summary>
    /// 운영 측정 전송 설정. <b>URL 을 코드에 박지 않기 위해 존재한다</b> (`25` §6-2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>엔드포인트가 비어 있으면 전송이 꺼진다.</b> 그게 기본값이고, 그래야 안전하다 —
    /// 에셋이 없거나 값이 안 채워진 상태로 빌드가 나가도 <b>게임은 그냥 평소대로 돈다.</b>
    /// 반대로 기본값을 켜둔 채로 두면, 설정을 빠뜨린 빌드가 조용히 아무 데도 못 보내면서
    /// 매 런마다 실패 요청을 날린다.
    /// </para>
    /// <para>
    /// <see cref="AudioCatalog"/>·<c>SpriteCatalog</c> 와 같은 구조다(<c>Resources</c> + 이름 상수).
    /// 같은 문제에 다른 해법을 쓰면 나중에 두 번 조사하게 된다.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = ResourceName, menuName = "DomoNinja/Telemetry Config")]
    public sealed class TelemetryConfig : ScriptableObject
    {
        /// <summary>`Resources.Load` 로 찾을 이름.</summary>
        public const string ResourceName = "TelemetryConfig";

        [Tooltip("Worker 주소. 비워두면 전송하지 않는다(개발 중 기본값). 예: https://domo-telemetry.example.workers.dev")]
        [SerializeField] private string _endpoint = "";

        [Tooltip("밸런스 버전 꼬리표. 재튜닝 전후 데이터가 섞이지 않게 갈 때마다 올린다. 비우면 Application.version 을 쓴다.")]
        [SerializeField] private string _appVersion = "";

        public string Endpoint => _endpoint.Trim();

        /// <summary>
        /// 데이터 묶음의 이름표. <b>에디터에서 돈 판은 <c>-editor</c> 가 붙어 따로 쌓인다.</b>
        /// </summary>
        /// <remarks>
        /// <para>
        /// ★ <b>이게 없으면 개발용 플레이가 실측에 섞인다.</b> 전송은 에디터에서도 그대로 나가므로,
        /// UI 를 붙이며 도는 판·밸런스를 보며 도는 판이 전부 같은 통에 들어간다.
        /// 그러면 <c>M1</c> 은 <b>심사자의 클리어율이 아니라 개발자의 클리어율</b>이 되고,
        /// 그 순간 `25` §1 이 만들려던 대조표의 값이 통째로 사라진다.
        /// </para>
        /// <para>
        /// 끄지 않고 <b>이름을 나누는</b> 쪽을 골랐다 — 꺼버리면 에디터에서 전송 경로를 확인할 방법이 없어진다.
        /// <b>섞이는 것이 문제지 보내는 것이 문제가 아니다.</b>
        /// </para>
        /// </remarks>
        public string AppVersion
        {
            get
            {
                string baseVersion = string.IsNullOrWhiteSpace(_appVersion)
                    ? Application.version
                    : _appVersion.Trim();

                return Application.isEditor ? baseVersion + "-editor" : baseVersion;
            }
        }

        /// <summary>보낼 곳이 있는가. <b>없는 게 정상 상태다</b> — 그때는 로컬 로그만 남는다.</summary>
        public bool HasEndpoint => !string.IsNullOrWhiteSpace(_endpoint);
    }
}
