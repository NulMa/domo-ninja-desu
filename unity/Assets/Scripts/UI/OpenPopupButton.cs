using UnityEngine;
using UnityEngine.UI;

namespace DomoNinja.Unity
{
    /// <summary>이 버튼을 누르면 <see cref="PopupKey"/> 팝업을 연다. 단순 팝업 오픈 버튼 전부가 공용으로 쓴다.</summary>
    [RequireComponent(typeof(Button))]
    public sealed class OpenPopupButton : MonoBehaviour
    {
        public string PopupKey = "";

        private void Awake()
        {
            GetComponent<Button>().onClick.AddListener(() => UIScreenManager.ShowPopup(PopupKey));
        }
    }
}
