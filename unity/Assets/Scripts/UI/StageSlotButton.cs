using UnityEngine;
using UnityEngine.UI;
using UImage = UnityEngine.UI.Image;

namespace DomoNinja.Unity
{
    /// <summary>
    /// 스테이지 선택 화면의 슬롯 하나. <see cref="StageId"/> 가 비어있으면 미저작 스테이지라 항상 잠김이다.
    /// </summary>
    /// <remarks>
    /// 잠금 여부는 <see cref="MetaProgress.IsUnlocked"/> 를 따른다. 선택 상태는
    /// <see cref="RunManager.SelectedStageId"/> 에 저장되고, 용병 선택 화면이 그걸 읽어 런을 시작한다.
    /// </remarks>
    [RequireComponent(typeof(Button), typeof(UImage))]
    public sealed class StageSlotButton : MonoBehaviour
    {
        public string StageId = "";

        // 스프라이트(나무 버튼) 위에 곱해지는 틴트다. 흰색이 원본 그대로다.
        // ★ 잠김은 색을 깎지 않는다 — `interactable=false` 가 이미 `button_disabled` 그림으로 바꾼다.
        //   여기서 또 어둡게 하면 두 번 깎여서 "잠김"이 아니라 "안 보임"이 된다.
        private static readonly Color LockedColor = Color.white;
        private static readonly Color UnlockedColor = Color.white;
        private static readonly Color SelectedColor = new Color(1.00f, 0.85f, 0.55f);

        private UImage _image;
        private Button _button;

        private void Awake()
        {
            _image = GetComponent<UImage>();
            _button = GetComponent<Button>();
            _button.onClick.AddListener(OnClick);
        }

        private void OnEnable()
        {
            var mgr = RunManager.Instance;
            if (mgr != null && mgr.IsDataLoaded) Refresh();
            if (mgr != null) mgr.DataLoaded += Refresh;
        }

        private void OnDisable()
        {
            var mgr = RunManager.Instance;
            if (mgr != null) mgr.DataLoaded -= Refresh;
        }

        private void OnClick()
        {
            var mgr = RunManager.Instance;
            if (mgr == null || mgr.Meta == null || !mgr.Meta.IsUnlocked(StageId)) return;

            mgr.SelectedStageId = StageId;
            foreach (var slot in transform.parent.GetComponentsInChildren<StageSlotButton>())
                slot.Refresh();

            // 예전엔 여기서 선택만 하고 별도 "용병 선택" 버튼을 또 눌러야 했다 — 이제 스테이지를
            // 고르는 순간 바로 로스터 화면으로 들어간다(두 단계를 한 번의 조작으로 합침).
            UIScreenManager.ShowScreen("RosterSelect");
        }

        public void Refresh()
        {
            var mgr = RunManager.Instance;
            bool unlocked = mgr != null && mgr.Meta != null && mgr.Meta.IsUnlocked(StageId);
            bool selected = unlocked && mgr!.SelectedStageId == StageId;

            _button.interactable = unlocked;
            _image.color = selected ? SelectedColor : (unlocked ? UnlockedColor : LockedColor);
        }
    }
}
