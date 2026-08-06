using UnityEngine;
using UnityEngine.UI;

namespace DomoNinja.Unity
{
    /// <summary>이 버튼을 누르면 <see cref="TutorialPopup"/>을 다시 띄운다.</summary>
    [RequireComponent(typeof(Button))]
    public sealed class OpenTutorialButton : MonoBehaviour
    {
        private void Awake()
        {
            GetComponent<Button>().onClick.AddListener(TutorialPopup.Show);
        }
    }
}
