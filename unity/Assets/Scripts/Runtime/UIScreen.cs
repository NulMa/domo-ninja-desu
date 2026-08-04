using UnityEngine;

namespace DomoNinja.Unity
{
    /// <summary>화면/팝업 종류. 풀스크린은 서로 배타적이고, 팝업은 그 위에 독립적으로 뜬다.</summary>
    public enum UIScreenKind
    {
        FullScreen,
        Popup,
    }

    /// <summary>화면 하나를 식별하는 태그. <see cref="UIScreenManager"/> 는 이 컴포넌트만 보고 움직인다.</summary>
    public sealed class UIScreen : MonoBehaviour
    {
        public string Key = "";
        public UIScreenKind Kind = UIScreenKind.FullScreen;
    }
}
