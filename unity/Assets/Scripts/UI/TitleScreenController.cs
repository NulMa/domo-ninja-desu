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
