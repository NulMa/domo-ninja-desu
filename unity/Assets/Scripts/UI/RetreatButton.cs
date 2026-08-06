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

        /// <remarks>
        /// ★ <b>이 화면에서 유일하게 되돌릴 수 없는 버튼이다.</b> 누르면 진행 중인 런 —
        /// 고른 로스터, 배치, 상점에서 산 것 — 이 전부 사라진다.
        /// 그런데 옵션창 안에서 "뒤로"·"설정"과 <b>같은 크기·같은 색으로</b> 나란히 있다.
        /// 한 번 묻는다.
        /// </remarks>
        private void OnClick()
        {
            UIConfirmPopup.Show(
                "퇴각하면 지금 런이 사라집니다.\n고른 용병과 산 물건도 함께 사라집니다.",
                "퇴각한다",
                Retreat);
        }

        private static void Retreat()
        {
            RunManager.Instance?.EndRun();
            UIScreenManager.HidePopup("InGameOptions");
            UIScreenManager.ShowScreen("StageSelect");
        }
    }
}
