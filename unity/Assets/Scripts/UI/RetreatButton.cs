using UnityEngine;
using UnityEngine.UI;

namespace DomoNinja.Unity
{
    /// <summary>인게임 옵션창의 "퇴각" 버튼. 런을 강제 종료하고 초기 화면으로 돌아간다.</summary>
    [RequireComponent(typeof(Button))]
    public sealed class RetreatButton : MonoBehaviour
    {
        private void Awake()
        {
            GetComponent<Button>().onClick.AddListener(OnClick);
        }

        private void OnClick()
        {
            RunManager.Instance?.EndRun();
            UIScreenManager.HidePopup("InGameOptions");
            UIScreenManager.ShowScreen("StageSelect");
        }
    }
}
