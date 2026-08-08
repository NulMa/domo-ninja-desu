using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UImage = UnityEngine.UI.Image;

namespace DomoNinja.Unity
{
    /// <summary>
    /// 타이틀 화면. 아무 곳이나 터치하면, 이 기기에서 처음 켠 거라면 스토리를 먼저 보여주고
    /// StageSelect로 넘어가면서 튜토리얼도 자동으로 띄운다. 재방문이면 곧장 StageSelect로 간다.
    /// </summary>
    /// <remarks>
    /// <see cref="PlayerPrefs"/>는 WebGL 빌드에서도 브라우저 IndexedDB에 저장돼 재방문 시
    /// 유지된다 — 시크릿 모드·데이터 삭제 시에만 다시 "처음"으로 보이는데, 그건 오히려
    /// 심사자의 첫 접속에서 항상 스토리·튜토리얼이 뜬다는 뜻이라 원하는 동작과 같다.
    /// 스토리·튜토리얼을 각각 별도 키로 관리하는 이유: 나중에 튜토리얼만 다시 보기(StageSelect
    /// "?" 버튼)가 가능해야 하는데, 스토리는 그 대상이 아니다 — 한 키로 묶으면 구분이 안 된다.
    /// </remarks>
    public sealed class TitleScreenController : MonoBehaviour
    {
        private const string StorySeenKey = "StorySeen";
        private const string TutorialSeenKey = "TutorialSeen";

        private void Awake()
        {
            var button = UITheme.EnsureButton(gameObject);
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(OnTap);

            BuildSettingsButton();
            BuildCollectionNotice();
        }

        private const string NoticeName = "TitleCollectionNotice";

        /// <summary>
        /// 수집 고지를 <b>타이틀 하단</b>에 한 줄 둔다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// ★ <b>설정에도 같은 고지가 있는데 여기 또 두는 이유는 순서다.</b>
        /// 수집은 첫 플레이부터 시작되는데 설정은 그 뒤에 열린다 — 설정에만 있으면
        /// 고지가 항상 <b>사후</b>가 되고, 대부분은 설정을 아예 안 연다.
        /// <b>고지는 수집보다 먼저 보여야 한다.</b> 타이틀은 누구나 반드시 거치는 유일한 화면이라
        /// 그 자리에서 순서가 바로잡힌다. (`25` §5 — 원문도 *"설정 화면 또는 타이틀 하단"* 이다)
        /// </para>
        /// <para>
        /// ★ <b><c>raycastTarget</c> 을 끈다.</b> 타이틀은 화면 전체가 버튼이라(§<see cref="Awake"/>)
        /// 켜두면 <b>이 글자 위를 탭했을 때만 게임이 시작되지 않는다.</b> 원인을 찾기 어려운 종류의 버그다.
        /// </para>
        /// <para>
        /// 실제로 보내고 있을 때만 띄운다 — 안 보내는데 "수집됩니다"가 떠 있으면 화면이 거짓말을 한다.
        /// </para>
        /// </remarks>
        private void BuildCollectionNotice()
        {
            if (transform.Find(NoticeName) != null) return;

            var config = Resources.Load<TelemetryConfig>(TelemetryConfig.ResourceName);
            if (config == null || !config.HasEndpoint) return;

            var go = new GameObject(NoticeName, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            go.transform.SetAsLastSibling();

            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 18f);
            rt.sizeDelta = new Vector2(1000f, 32f);

            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = "플레이 기록(고른 캐릭터·스킬·클리어 여부)이 밸런스 확인용으로 익명 수집됩니다.";
            label.fontSize = 17f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(1f, 1f, 1f, 0.55f);
            label.raycastTarget = false;

            ApplySceneFont(label);
        }

        /// <summary>
        /// 씬에 이미 쓰이는 TMP 폰트를 물려받는다.
        /// </summary>
        /// <remarks>
        /// TMP 기본 폰트는 라틴 문자만 들고 있어서, 폰트를 안 물려주면 <b>한글이 네모로 나온다.</b>
        /// 타이틀 안에 글자가 없을 수도 있으므로 씬 전체에서도 한 번 찾는다.
        /// </remarks>
        private void ApplySceneFont(TMP_Text label)
        {
            TMP_Text source = null;

            foreach (var t in GetComponentsInChildren<TMP_Text>(true))
            {
                if (t != label) { source = t; break; }
            }

            if (source == null)
            {
                foreach (var t in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (t != label) { source = t; break; }
                }
            }

            if (source == null) return;

            label.font = source.font;
            label.fontSharedMaterial = source.fontSharedMaterial;
        }

        /// <summary>
        /// 타이틀에서도 소리를 조절할 수 있게 <b>톱니 버튼</b>을 얹는다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 지시(사용자): "시작 시 설정창이나 게임종료 안 띄워줘서 소리 조정 못하고."
        /// </para>
        /// <para>
        /// ★ 타이틀은 <b>화면 전체가 버튼</b>이라 설정 진입로가 없었다. 소리를 줄이려면
        /// 일단 게임을 시작해야 했는데, <b>소리가 큰 게 문제일 때 그 방법은 답이 아니다.</b>
        /// </para>
        /// <para>
        /// 클릭이 뒤의 "탭하여 시작"으로 새지 않는다 — UGUI 는 <b>가장 위 레이캐스트 대상</b>
        /// 하나에만 클릭을 전달하고, 이 버튼이 더 늦은 자식이라 위에 있다.
        /// </para>
        /// <para>
        /// <c>MenuOptions</c> 가 아니라 <c>Settings</c> 를 바로 연다 — 그쪽엔 "뒤로가기" 가 있는데
        /// 타이틀에서는 <b>돌아갈 곳이 없다.</b>
        /// </para>
        /// </remarks>
        private void BuildSettingsButton()
        {
            if (transform.Find("TitleSettingsButton") != null) return;

            var go = new GameObject("TitleSettingsButton", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            go.transform.SetAsLastSibling();

            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-24f, -24f);
            rt.sizeDelta = new Vector2(72f, 72f);

            var bg = go.AddComponent<UImage>();
            bg.sprite = UITheme.Find(UITheme.ButtonNormalKey);
            bg.type = UImage.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 1f;

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(go.transform, false);
            var irt = (RectTransform)iconGo.transform;
            irt.anchorMin = new Vector2(0.5f, 0.5f);
            irt.anchorMax = new Vector2(0.5f, 0.5f);
            irt.sizeDelta = new Vector2(40f, 40f);

            var icon = iconGo.AddComponent<UImage>();
            icon.sprite = UITheme.Find(GearIconKey);
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            UITheme.EnsureButton(go).onClick.AddListener(() => UIScreenManager.ShowPopup("Settings"));
        }

        private const string GearIconKey = "UI/gear_icon";

        private void OnTap()
        {
            if (PlayerPrefs.GetInt(StorySeenKey, 0) == 0)
            {
                PlayerPrefs.SetInt(StorySeenKey, 1);
                PlayerPrefs.Save();
                StoryScreen.Show(EnterStageSelect);
            }
            else
            {
                EnterStageSelect();
            }
        }

        private static void EnterStageSelect()
        {
            UIScreenManager.ShowScreen("StageSelect");

            if (PlayerPrefs.GetInt(TutorialSeenKey, 0) == 0)
            {
                PlayerPrefs.SetInt(TutorialSeenKey, 1);
                PlayerPrefs.Save();
                TutorialPopup.Show();
            }
        }
    }
}
