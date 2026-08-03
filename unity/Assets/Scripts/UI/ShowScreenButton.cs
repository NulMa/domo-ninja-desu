using UnityEngine;
using UnityEngine.UI;

namespace DomoNinja.Unity
{
    /// <summary>이 버튼을 누르면 <see cref="ScreenKey"/> 풀스크린으로 전환한다(배타 전환).</summary>
    [RequireComponent(typeof(Button))]
    public sealed class ShowScreenButton : MonoBehaviour
    {
        public string ScreenKey = "";

        private void Awake()
        {
            GetComponent<Button>().onClick.AddListener(() => UIScreenManager.ShowScreen(ScreenKey));
        }
    }
}
