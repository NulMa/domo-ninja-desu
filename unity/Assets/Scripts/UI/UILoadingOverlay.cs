using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DomoNinja.Unity
{
    /// <summary>
    /// 데이터를 불러오는 동안 화면을 덮는다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>웹 빌드라서 필요하다.</b> <see cref="StreamingGameData"/> 는 <c>UnityWebRequest</c> 로
    /// 파일 5종을 받아오는데, 브라우저에서는 이게 네트워크 왕복이다. 그동안
    /// <see cref="UIScreenManager"/> 는 이미 첫 화면을 띄워둔 상태라 —
    /// <b>화면은 다 켜져 있는데 눌러도 아무 일이 안 일어난다.</b>
    /// 처음 여는 사람에게 그건 로딩이 아니라 고장으로 보인다.
    /// </para>
    /// <para>
    /// ⚠️ <b>로드 실패를 성공과 같이 다루면 안 된다.</b> <see cref="RunManager.DataLoaded"/> 는
    /// 성공/실패 어느 쪽이든 한 번 불린다. 실패했는데 덮개만 걷으면
    /// <b>데이터 없는 UI 가 원인 불명으로 멈춘 상태</b>가 되고, 그건 콘솔을 못 보는 심사자에게
    /// 아무 단서도 남기지 않는다. 실패면 덮개를 문구만 바꿔 그대로 둔다.
    /// </para>
    /// </remarks>
    public sealed class UILoadingOverlay : MonoBehaviour
    {
        /// <summary>다른 캔버스보다 확실히 위. 화면 캔버스들은 기본값(0)을 쓴다.</summary>
        private const int SortingOrder = 9000;

        private static UILoadingOverlay _instance;

        private TMP_Text _label;
        private RectTransform _spinner;
        private bool _done;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("UILoadingOverlay");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<UILoadingOverlay>();
        }

        private void Awake()
        {
            Build();
            StartCoroutine(Watch());
        }

        private void Build()
        {
            UITheme.SetupFullScreenCanvas(gameObject, SortingOrder);

            var bg = NewChild("Backdrop", transform);
            var bgImage = bg.gameObject.AddComponent<Image>();
            bgImage.color = new Color(0.07f, 0.08f, 0.09f, 1f);
            Stretch(bg);

            var spinner = NewChild("Spinner", transform);
            var spinnerImage = spinner.gameObject.AddComponent<Image>();
            spinnerImage.sprite = UITheme.Find("UI/Theme/button_normal");
            spinnerImage.raycastTarget = false;
            spinner.sizeDelta = new Vector2(96f, 24f);
            spinner.anchoredPosition = new Vector2(0f, 60f);
            _spinner = spinner;

            var label = NewChild("Label", transform);
            _label = label.gameObject.AddComponent<TextMeshProUGUI>();
            _label.text = "불러오는 중…";
            _label.fontSize = 32f;
            _label.alignment = TextAlignmentOptions.Center;
            _label.color = new Color(0.9f, 0.88f, 0.84f);
            _label.raycastTarget = false;
            label.sizeDelta = new Vector2(900f, 120f);
            label.anchoredPosition = new Vector2(0f, -40f);
        }

        private IEnumerator Watch()
        {
            // RunManager 가 아직 Awake 전일 수 있다. 나타날 때까지 기다린다.
            while (RunManager.Instance == null) yield return null;

            var mgr = RunManager.Instance;
            if (mgr.IsDataLoaded) { Hide(); yield break; }

            mgr.DataLoaded += OnDataLoaded;
        }

        private void OnDataLoaded()
        {
            var mgr = RunManager.Instance;
            if (mgr != null) mgr.DataLoaded -= OnDataLoaded;

            if (mgr != null && mgr.IsDataLoaded) { Hide(); return; }

            // 실패. 덮개를 걷지 않는다 — 걷으면 아무것도 안 되는 화면만 남는다.
            _done = true;
            _label.text = "데이터를 불러오지 못했습니다.\n새로고침해 주세요.";
            _label.color = new Color(1f, 0.6f, 0.5f);
            if (_spinner != null) _spinner.gameObject.SetActive(false);
        }

        private void Hide()
        {
            _done = true;
            gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_done || _spinner == null) return;
            // 돌아가는 것이 있어야 "멈춘 화면"이 아니라 "기다리는 화면"으로 읽힌다.
            _spinner.localRotation = Quaternion.Euler(0f, 0f, -Time.unscaledTime * 180f);
        }

        private static RectTransform NewChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
