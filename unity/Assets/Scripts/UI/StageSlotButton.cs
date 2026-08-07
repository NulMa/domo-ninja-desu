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

        /// <summary>잠긴 슬롯 위에 얹는 자물쇠. 씬을 건드리지 않고 코드로 만든다(`19` §5.1).</summary>
        private const string LockIconKey = "UI/StageLock";

        private UImage _image;
        private Button _button;
        private UImage _lockIcon;

        private void Awake()
        {
            _image = GetComponent<UImage>();
            _button = GetComponent<Button>();
            _button.onClick.AddListener(OnClick);
            _lockIcon = BuildLockIcon();
        }

        /// <summary>
        /// 잠금 표시를 <b>그림으로</b> 단다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 지시(사용자): "3~6 스테이지 버튼엔 자물쇠 모양 아이콘이나 비슷한 무언가라도 달아둬,
        /// 안 만들고 제출할 건가 봄."
        /// </para>
        /// <para>
        /// ★ 전에는 <c>interactable=false</c> 로 <b>회색 버튼</b>이 되는 게 전부였다.
        /// 그건 "잠겼다"와 <b>"고장났다"가 구분되지 않는다</b> — 심사자가 3분 안에 이해해야 하는
        /// 화면에서 눌리지 않는 버튼이 이유 없이 넷이면 미완성으로 읽힌다.
        /// 자물쇠는 <b>"의도적으로 닫아둔 것"</b>이라는 신호다.
        /// </para>
        /// </remarks>
        private UImage BuildLockIcon()
        {
            var go = new GameObject("LockIcon", typeof(RectTransform));
            go.transform.SetParent(transform, false);

            // ★ 가운데가 아니라 <b>오른쪽 끝</b>이다. 처음엔 버튼 중앙에 뒀는데
            //   "2스[자물쇠]이지" 처럼 글자 한가운데를 덮었다 — 잠금을 알리려다 이름을 지운 꼴이다.
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-10f, 0f);
            rt.sizeDelta = new Vector2(34f, 34f);

            var img = go.AddComponent<UImage>();
            img.sprite = UITheme.Find(LockIconKey);
            img.preserveAspect = true;
            // 버튼이 이미 클릭을 받는다 — 아이콘이 또 받으면 잠긴 칸에서 클릭이 여기서 먹힌다.
            img.raycastTarget = false;

            go.SetActive(false);
            return img;
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

            if (_lockIcon != null) _lockIcon.gameObject.SetActive(!unlocked);
        }
    }
}
