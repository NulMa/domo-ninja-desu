using UnityEngine;
using UnityEngine.UI;
using UImage = UnityEngine.UI.Image;

namespace DomoNinja.Unity
{
    /// <summary>정보 팝업의 탭 전환만 담당한다. 캐릭터/아이템 상세 내용 채우기는 별도 작업이다.</summary>
    public sealed class InfoPopupController : MonoBehaviour
    {
        private GameObject _characterTab;
        private GameObject _itemTab;
        private Button _tabCharacter;
        private Button _tabItem;

        private void Awake()
        {
            var board = transform.Find("Board");
            _characterTab = board.Find("CharacterTab").gameObject;
            _itemTab = board.Find("ItemTab").gameObject;

            _tabCharacter = EnsureButton(board.Find("TabCharacter").gameObject);
            _tabItem = EnsureButton(board.Find("TabItem").gameObject);
            var closeButton = EnsureButton(board.Find("CloseButton").gameObject);

            _tabCharacter.onClick.AddListener(() => ShowTab(character: true));
            _tabItem.onClick.AddListener(() => ShowTab(character: false));
            closeButton.onClick.AddListener(() => UIScreenManager.HidePopup("InfoPopup"));
        }

        private void OnEnable() => ShowTab(character: true);

        private void ShowTab(bool character)
        {
            _characterTab.SetActive(character);
            _itemTab.SetActive(!character);
        }

        private static Button EnsureButton(GameObject go)
        {
            var btn = go.GetComponent<Button>();
            if (btn == null) btn = go.AddComponent<Button>();
            var img = go.GetComponent<UImage>();
            if (img != null) btn.targetGraphic = img;
            return btn;
        }
    }
}
