using UnityEngine;
using UnityEngine.UI;

namespace DomoNinja.Unity
{
    /// <summary>게임 종료 버튼. 스탠드얼론 빌드에서만 의미가 있다 — WebGL/에디터에서는 아무 일도 안 한다.</summary>
    [RequireComponent(typeof(Button))]
    public sealed class QuitGameButton : MonoBehaviour
    {
        private void Awake()
        {
            GetComponent<Button>().onClick.AddListener(Application.Quit);
        }
    }
}
