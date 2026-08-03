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

        private static readonly Color LockedColor = new Color(0.11f, 0.12f, 0.13f);
        private static readonly Color UnlockedColor = new Color(0.18f, 0.19f, 0.21f);
        private static readonly Color SelectedColor = new Color(0.25f, 0.48f, 0.78f);

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
