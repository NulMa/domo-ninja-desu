using UnityEngine;
using UnityEngine.UI;

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
        }

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
