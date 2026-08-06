using UnityEngine;
using UnityEngine.UI;

namespace DomoNinja.Unity
{
    /// <summary>
    /// 타이틀 화면. 아무 곳이나 터치하면 StageSelect로 넘어가고, 이 기기에서 처음 켠 거라면
    /// 이어서 튜토리얼을 자동으로 띄운다.
    /// </summary>
    /// <remarks>
    /// <see cref="PlayerPrefs"/>는 WebGL 빌드에서도 브라우저 IndexedDB에 저장돼 재방문 시
    /// 유지된다 — 시크릿 모드·데이터 삭제 시에만 다시 "처음"으로 보이는데, 그건 오히려
    /// 심사자의 첫 접속에서 항상 튜토리얼이 뜬다는 뜻이라 원하는 동작과 같다.
    /// </remarks>
    public sealed class TitleScreenController : MonoBehaviour
    {
        private const string TutorialSeenKey = "TutorialSeen";

        private void Awake()
        {
            var button = UITheme.EnsureButton(gameObject);
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(OnTap);
        }

        private void OnTap()
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
