using UnityEngine;
using UnityEngine.UI;

namespace DomoNinja.Unity
{
    /// <summary>이 버튼을 누르면 <see cref="PopupKey"/> 팝업을 닫는다.</summary>
    [RequireComponent(typeof(Button))]
    public sealed class ClosePopupButton : MonoBehaviour
    {
        public string PopupKey = "";

        private void Awake()
        {
            GetComponent<Button>().onClick.AddListener(() => UIScreenManager.HidePopup(PopupKey));
        }
    }
}
