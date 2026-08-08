// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System;
using System.Collections;
using System.Text;
using DomoNinja.Core.Domain;
using UnityEngine;
using UnityEngine.Networking;

namespace DomoNinja.Unity
{
    /// <summary>
    /// 런이 끝나면 그 결과를 한 번 보낸다. <b>게임에 영향을 줄 수 없는 것이 이 클래스의 유일한 계약이다</b> (`25` §5).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>왜 이걸 하는가.</b> 밸런스를 잡으려는 게 아니다 — 심사자 몇 명의 판은 표본이 안 되고
    /// <c>sim</c> 은 21,600런을 1.5초에 돈다. 얻으려는 건 <b>"sim 이 예측한 분포와 사람이 고른 분포를
    /// 나란히 놓는 자리"</b> 다(`25` §1). 그래서 여기서 보내는 항목은 `sim` 이 이미 재는 것과 같은 축이다.
    /// </para>
    /// <para>
    /// ★ <b><see cref="RunManager"/> 는 이 클래스를 모른다.</b> 반대로 여기서
    /// <see cref="RunManager.RunEnded"/> 를 구독한다. 방향을 이렇게 잡은 이유는
    /// <c>IEventSink</c> 를 쓰기 전용으로 둔 것과 같다(`23` §1) — <b>측정이 게임을 못 건드리는 것을
    /// 약속이 아니라 구조로 만든다.</b> 이 파일을 지우면 측정이 통째로 사라지고 게임은 그대로다.
    /// </para>
    /// <para>
    /// ★ <b>씬을 건드리지 않는다.</b> <see cref="RuntimeInitializeOnLoadMethod"/> 로 스스로 뜬다 —
    /// 씬 파일은 손으로 병합이 안 되고 2인이 같이 만진다(`19` §5.1). 컴포넌트 하나 얹자고
    /// 씬을 고치면 그 순간부터 팀원과 충돌 지점이 하나 는다.
    /// </para>
    /// </remarks>
    public sealed class Telemetry : MonoBehaviour
    {
        /// <summary>익명 세션 id 저장 키. <b>같은 사람의 여러 런을 잇는 것 외의 용도가 없다.</b></summary>
        private const string SessionKey = "telemetry.sessionId";

        /// <summary>
        /// 응답을 안 기다리지만 요청 자체는 언젠가 끝나야 한다. 브라우저 탭에 죽은 요청이 쌓이면
        /// 그 자체가 자원이다. 짧게 끊는다 — <b>어차피 결과를 안 본다.</b>
        /// </summary>
        private const int TimeoutSeconds = 10;

        public static Telemetry? Instance { get; private set; }

        private TelemetryConfig? _config;
        private string _sessionId = "";

        /// <summary>보낸 뒤에도 남겨 두는 마지막 payload. 개발 중 눈으로 확인하려고 둔다.</summary>
        public string LastPayload { get; private set; } = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;

            var go = new GameObject("Telemetry");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<Telemetry>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _config = Resources.Load<TelemetryConfig>(TelemetryConfig.ResourceName);
            _sessionId = LoadOrCreateSessionId();

            RunManager.RunEnded += OnRunEnded;
        }

        private void OnDestroy()
        {
            RunManager.RunEnded -= OnRunEnded;
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// 브라우저(기기)마다 하나. <b>사람을 식별하지 않는다</b> — 같은 방문자의 런 여러 개를
        /// 하나로 묶는 것이 전부다. 지우면 그냥 새 사람이 된다.
        /// </summary>
        private static string LoadOrCreateSessionId()
        {
            string id = PlayerPrefs.GetString(SessionKey, "");
            if (!string.IsNullOrEmpty(id)) return id;

            id = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(SessionKey, id);
            PlayerPrefs.Save();
            return id;
        }

        private void OnRunEnded(RunState run, bool cleared, int roundsWon)
        {
            // 여기서 예외가 나가면 런 마감이 깨진다. 구독자 쪽에서도 한 번 더 막는다 —
            // RunManager 가 try/catch 를 하고 있지만, 그건 저쪽의 사정이지 이쪽의 보증이 아니다.
            try
            {
                LastPayload = BuildPayload(run, cleared, roundsWon);

                if (_config == null || !_config.HasEndpoint)
                {
                    // 엔드포인트가 없는 게 기본 상태다. 조용히 로컬에만 남긴다.
                    Debug.Log($"[Telemetry] (로컬) {LastPayload}");
                    return;
                }

                StartCoroutine(Post(_config.Endpoint.TrimEnd('/') + "/ingest", LastPayload));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Telemetry] 적재 실패 — {e.Message}");
            }
        }

        /// <summary>
        /// 던지고 잊는다. <b>응답을 보지 않고, 실패해도 아무것도 하지 않는다.</b>
        /// </summary>
        /// <remarks>
        /// 코루틴은 프레임을 막지 않는다 — <c>yield</c> 는 이 코루틴만 재우고 게임 루프는 계속 돈다.
        /// 여기서 한 번이라도 블로킹하면 네트워크가 느린 심사자의 화면이 멈추고,
        /// <b>그건 "게임이 구리다"로 읽히지 "서버가 느리다"로 안 읽힌다.</b>
        /// </remarks>
        private static IEnumerator Post(string url, string json)
        {
            using var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = TimeoutSeconds;

            yield return req.SendWebRequest();

            // 성공이든 실패든 게임은 이미 다음 화면에 가 있다. 로그만 남긴다.
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.Log($"[Telemetry] 전송 실패(무시) — {req.error}");
            }
        }

        /// <summary>
        /// <c>/ingest</c> 가 받는 모양으로 만든다. 키 이름은 서버 <c>validate()</c> 와 짝이다.
        /// </summary>
        /// <remarks>
        /// <c>JsonUtility</c> 를 안 쓴다 — <c>null</c> 을 빈 문자열로 바꿔버려서
        /// <b>"액티브를 아직 안 골랐다"와 "빈 id 를 골랐다"가 구분되지 않는다.</b>
        /// 서버는 그 둘을 다르게 취급한다.
        /// </remarks>
        private string BuildPayload(RunState run, bool cleared, int roundsWon)
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"v\":1");
            sb.Append(",\"sid\":\"").Append(Escape(_sessionId)).Append('"');
            sb.Append(",\"stage\":\"").Append(Escape(run.StageId)).Append('"');
            sb.Append(",\"cleared\":").Append(cleared ? "true" : "false");
            sb.Append(",\"roundsWon\":").Append(roundsWon);
            sb.Append(",\"appVer\":\"").Append(Escape(_config != null ? _config.AppVersion : "")).Append('"');

            sb.Append(",\"roster\":[");
            for (int i = 0; i < run.Deployed.Count; i++)
            {
                var e = run.Deployed[i];
                if (i > 0) sb.Append(',');

                sb.Append("{\"c\":\"").Append(Escape(e.CharacterId)).Append('"');

                sb.Append(",\"active\":");
                if (e.ActiveSkillId == null) sb.Append("null");
                else sb.Append('"').Append(Escape(e.ActiveSkillId)).Append('"');

                sb.Append(",\"support\":");
                AppendStrings(sb, e.SupportSkillIds);

                sb.Append(",\"items\":");
                AppendItems(sb, e.Items);

                sb.Append('}');
            }
            sb.Append(']');

            sb.Append(",\"teamItems\":");
            AppendItems(sb, run.TeamItems);

            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendStrings(StringBuilder sb, System.Collections.Generic.IReadOnlyList<string> values)
        {
            sb.Append('[');
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(Escape(values[i])).Append('"');
            }
            sb.Append(']');
        }

        private static void AppendItems(StringBuilder sb, System.Collections.Generic.IReadOnlyList<OwnedItem> items)
        {
            sb.Append('[');
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                // OwnedItem.ToString() 이 `key#index` 를 준다 — sim 리포트에서 읽던 표기와 같다.
                sb.Append('"').Append(Escape(items[i].ToString())).Append('"');
            }
            sb.Append(']');
        }

        private static string Escape(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            var sb = new StringBuilder(s!.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
