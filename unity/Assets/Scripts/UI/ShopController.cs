using System.Collections.Generic;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Economy;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UImage = UnityEngine.UI.Image;
using DomoNinja.Unity.View;

namespace DomoNinja.Unity
{
    /// <summary>상점 화면. 5칸(스킬3+아이템2)이 구매/판매 탭에 따라 다른 걸 보여준다.</summary>
    /// <remarks>
    /// 구매 모드에서는 슬롯 위치가 곧 <see cref="Shop.Offers"/> 인덱스다 — `Restock` 이
    /// 스킬 슬롯 수만큼 먼저 뽑고 아이템을 그 뒤에 채우므로(`D-69`), 화면의
    /// SkillSlot1~3 · ItemSlot1~2 순서가 그 목록 순서와 그대로 맞는다.
    /// </remarks>
    public sealed class ShopController : MonoBehaviour
    {
        /// <summary>런 전용 재화 아이콘. 메타 재화(StageSelect, `Meta/M-GOLD_재화`)와 구분되는 별개 그림이다.</summary>
        private const string RunCurrencyIconKey = "UI/RunCurrency_재화";

        /// <summary>아이템 표시명. 데이터에 플레이어용 이름 필드가 없어 여기 하나에 둔다 — InfoPopup 도 이걸 그대로 쓴다.</summary>
        internal static readonly Dictionary<string, string> ItemNames = new Dictionary<string, string>
        {
            { "statBoost", "스탯 강화" },
            { "teamBoost", "팀 강화" },
            { "conditionalBoost", "조건부 강화" },
            { "healItem", "회복 아이템" },
        };

        private readonly struct SellEntry
        {
            /// <summary>null 이면 팀 전체 아이템.</summary>
            public readonly string CharacterId;
            public readonly string ItemKey;
            public readonly int OptionIndex;

            public SellEntry(string characterId, string itemKey, int optionIndex)
            {
                CharacterId = characterId;
                ItemKey = itemKey;
                OptionIndex = optionIndex;
            }
        }

        private readonly struct TargetSlot
        {
            public readonly Button Button;
            public readonly UImage Face;
            public readonly TMP_Text NameLabel;
            public readonly TMP_Text StatusLabel;

            public TargetSlot(Button button, UImage face, TMP_Text nameLabel, TMP_Text statusLabel)
            {
                Button = button;
                Face = face;
                NameLabel = nameLabel;
                StatusLabel = statusLabel;
            }
        }

        // 스프라이트 위에 곱해지는 틴트다. 흰색이 원본 그대로다.
        private static readonly Color AliveSlotColor = Color.white;
        private static readonly Color DeadSlotColor = new Color(0.55f, 0.42f, 0.42f, 1f);

        private Transform _board;
        private TMP_Text _currencyLabel;
        private Button[] _slotButtons;
        private TMP_Text[] _slotLabels;
        private UImage[] _slotIcons;
        private Button _buyTab;
        private Button _sellTab;
        private Button _rerollButton;
        private Button _nextRoundButton;

        private bool _sellMode;
        private readonly List<SellEntry> _sellableItems = new List<SellEntry>();

        private Transform _targetPicker;
        private TargetSlot[] _targetSlots;
        private Button _targetCancelButton;
        private SpriteCatalog _catalog;
        private int _pendingOfferIndex = -1;

        // ── 일러스트 영역(`IllustrationZone`). 예전엔 아무것도 안 그려진 자리표시자였다 —
        //    아이템을 눌렀을 때 뜨던 전체화면 확인창(UIConfirmPopup)을 여기로 옮기고,
        //    "무엇을 사는지" 정보 표시까지 얹었다(D+9).
        private GameObject _illustEmptyLabel;
        private GameObject _illustInfo;
        private UImage _illustIcon;
        private TMP_Text _illustNameLabel;
        private TMP_Text _illustDescLabel;
        private Button _illustConfirmButton;
        private TMP_Text _illustConfirmLabel;
        private int _illustOfferIndex = -1;

        private void Awake()
        {
            _board = transform.Find("Board");
            var currencyDisplay = _board.Find("CurrencyDisplay");
            _currencyLabel = currencyDisplay.Find("Label").GetComponent<TMP_Text>();
            currencyDisplay.Find("Icon").GetComponent<UImage>().sprite = UITheme.Find(RunCurrencyIconKey);

            _slotButtons = new Button[5];
            _slotLabels = new TMP_Text[5];
            _slotIcons = new UImage[5];

            for (int i = 0; i < 3; i++) BindSlot(i, "SkillSlot" + (i + 1));
            for (int i = 0; i < 2; i++) BindSlot(3 + i, "ItemSlot" + (i + 1));

            _buyTab = UITheme.EnsureButton(_board.Find("BuyTab").gameObject);
            _sellTab = UITheme.EnsureButton(_board.Find("SellTab").gameObject);
            _rerollButton = UITheme.EnsureButton(_board.Find("RerollButton").gameObject);
            _nextRoundButton = UITheme.EnsureButton(_board.Find("NextRoundButton").gameObject);

            _buyTab.onClick.AddListener(() => SetSellMode(false));
            _sellTab.onClick.AddListener(() => SetSellMode(true));
            _rerollButton.onClick.AddListener(OnReroll);
            _nextRoundButton.onClick.AddListener(OnNextRound);

            _catalog = Resources.Load<SpriteCatalog>(SpriteCatalog.ResourceName);
            BindTargetPicker();
            BuildIllustrationZone();
            BuildRosterStrip();
        }

        // ─────────────────────────────────────────────────────────────
        //  출전 용병 띠 — 상점에서도 내 팀 상태가 보이게
        // ─────────────────────────────────────────────────────────────

        /// <summary>용병 한 칸의 초상화. 갱신할 때 스프라이트·체력만 갈아끼운다.</summary>
        private UImage[] _rosterFaces;
        private TMP_Text[] _rosterHpLabels;
        private HoverTooltipTrigger[] _rosterHovers;

        private const int RosterStripSlots = 3;

        /// <summary>
        /// 상점 아래쪽에 <b>출전 3인의 초상화와 체력</b>을 상시로 깐다. 올리면 전체 스펙이 뜬다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 지시(사용자): "상점 페이지에서 용병 초상화로 현재 스펙도 보여주는 게 좋아 보임."
        /// </para>
        /// <para>
        /// ★ 대상 선택창(<c>TargetPicker</c>)에도 초상화가 있지만 그건 <b>이미 사기로 정한 뒤</b>에
        /// 뜬다. 무엇을 살지 <b>고르는 동안</b>에는 팀 상태가 화면에 없어서,
        /// "체력이 간당간당한 애가 있었나"를 확인하려면 상점을 나갔다 와야 했다.
        /// 사는 판단에 필요한 정보가 사는 화면에 없던 셈이다.
        /// </para>
        /// <para>
        /// 씬을 건드리지 않고 코드로 세운다 (`19` §5.1 — 씬 파일은 손으로 병합이 안 된다).
        /// </para>
        /// </remarks>
        private void BuildRosterStrip()
        {
            _rosterFaces = new UImage[RosterStripSlots];
            _rosterHpLabels = new TMP_Text[RosterStripSlots];
            _rosterHovers = new HoverTooltipTrigger[RosterStripSlots];

            var strip = NewChild("RosterStrip", _board);
            strip.anchorMin = new Vector2(0f, 0f);
            strip.anchorMax = new Vector2(1f, 0f);
            strip.pivot = new Vector2(0.5f, 0f);
            strip.anchoredPosition = new Vector2(0f, 12f);
            strip.sizeDelta = new Vector2(-40f, 108f);

            const float slotWidth = 190f;
            const float gap = 12f;
            float totalWidth = RosterStripSlots * slotWidth + (RosterStripSlots - 1) * gap;

            for (int i = 0; i < RosterStripSlots; i++)
            {
                var cell = NewChild("RosterSlot" + i, strip);
                cell.anchorMin = new Vector2(0.5f, 0f);
                cell.anchorMax = new Vector2(0.5f, 1f);
                cell.pivot = new Vector2(0f, 0f);
                cell.offsetMin = new Vector2(-totalWidth * 0.5f + i * (slotWidth + gap), 0f);
                cell.offsetMax = new Vector2(cell.offsetMin.x + slotWidth, 0f);

                var bg = cell.gameObject.AddComponent<UImage>();
                bg.sprite = UITheme.Find("UI/Theme/inventory_cell");
                bg.type = UImage.Type.Sliced;
                bg.pixelsPerUnitMultiplier = 1f;

                var face = NewChild("Face", cell);
                face.anchorMin = new Vector2(0f, 0.5f);
                face.anchorMax = new Vector2(0f, 0.5f);
                face.pivot = new Vector2(0f, 0.5f);
                face.anchoredPosition = new Vector2(10f, 0f);
                face.sizeDelta = new Vector2(80f, 80f);
                _rosterFaces[i] = face.gameObject.AddComponent<UImage>();
                _rosterFaces[i].preserveAspect = true;

                var hp = NewChild("HpLabel", cell);
                hp.anchorMin = new Vector2(0f, 0f);
                hp.anchorMax = new Vector2(1f, 1f);
                hp.offsetMin = new Vector2(96f, 6f);
                hp.offsetMax = new Vector2(-8f, -6f);
                var hpLabel = hp.gameObject.AddComponent<TextMeshProUGUI>();
                hpLabel.fontSize = 17f;
                hpLabel.alignment = TextAlignmentOptions.Left;
                hpLabel.color = Color.white;
                hpLabel.raycastTarget = false;
                // ★ 줄바꿈을 끈다. `HP 153 / 153` 이 폭에 안 맞아 접히면서 두 줄이 세 줄이 되고,
                //   칸 높이를 넘겨 **뒷사람 이름이 잘렸다**(적영이 그랬다).
                //   접는 대신 폭에 맞춰 줄여 쓴다 — 숫자는 작아도 읽히지만 잘리면 못 읽는다.
                hpLabel.textWrappingMode = TextWrappingModes.NoWrap;
                hpLabel.overflowMode = TextOverflowModes.Overflow;
                hpLabel.enableAutoSizing = true;
                hpLabel.fontSizeMin = 12f;
                hpLabel.fontSizeMax = 17f;
                _rosterHpLabels[i] = hpLabel;

                _rosterHovers[i] = cell.gameObject.AddComponent<HoverTooltipTrigger>();
            }
        }

        /// <summary>띠를 지금 런 상태로 맞춘다. 상점을 열 때·구매 직후 부른다.</summary>
        private void RefreshRosterStrip(RunManager mgr)
        {
            if (_rosterFaces == null) return;

            var deployed = mgr != null && mgr.CurrentRun != null ? mgr.CurrentRun.Deployed : null;

            for (int i = 0; i < RosterStripSlots; i++)
            {
                bool has = deployed != null && i < deployed.Count;
                _rosterFaces[i].transform.parent.gameObject.SetActive(has);
                if (!has) continue;

                var entry = deployed[i];
                var def = mgr.Data.FindCharacter(entry.CharacterId);

                _rosterFaces[i].sprite = def != null ? _catalog?.Find(def.Sprite) : null;
                _rosterFaces[i].color = entry.IsAlive ? AliveSlotColor : DeadSlotColor;

                _rosterHpLabels[i].text = entry.IsAlive
                    ? $"{(def != null ? def.Name : entry.CharacterId)}\n<size=15>HP {entry.Hp}/{entry.MaxHp}</size>"
                    : $"{(def != null ? def.Name : entry.CharacterId)}\n<size=15><color=#9A948C>사망</color></size>";

                // ★ 문자열을 미리 굽지 않고 대리자로 넘긴다 — 이 띠는 구매 직후에도 갱신되는데,
                //   그때 캡처한 값을 들고 있으면 툴팁만 옛 스펙을 계속 보여준다.
                var captured = entry;
                var capturedDef = def;
                _rosterHovers[i].Describe = () => capturedDef == null
                    ? null
                    : UnitStatText.ForDeployedAlly(capturedDef, captured, mgr.Data);
            }
        }

        /// <summary>
        /// <c>IllustrationZone</c>(씬에 이미 있던 빈 자리표시자 패널) 안에 아이콘·이름·설명·구매 버튼을
        /// 코드로 채운다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// ★ 씬을 손으로 더 건드리지 않는다 — 이 저장소의 UI 화면은 대부분 씬에 직접 배치돼 있지만,
        /// <c>UIConfirmPopup</c>처럼 <b>동적으로 채워지는 조각은 코드로 만드는 관례</b>가 이미 있다
        /// (`19` §5.1 — 씬 파일은 손으로 병합이 안 된다). 패널의 크기·위치는 <c>IllustrationZone</c>
        /// 자체(씬)가 갖고, 그 안의 내용만 여기서 채운다.
        /// </para>
        /// <para>
        /// 기존 <c>Label</c>("[일러스트존]")은 <b>빈 상태 안내문</b>으로 재활용한다 — 아이템을 아직
        /// 안 눌렀을 때도 이 자리가 죽은 공간처럼 보이면 안 된다.
        /// </para>
        /// </remarks>
        private void BuildIllustrationZone()
        {
            var zone = (RectTransform)_board.Find("IllustrationZone");
            if (zone == null) return; // 구버전 씬 폴백 — 없으면 그냥 옛 확인창으로 동작한다.

            var emptyLabel = zone.Find("Label").GetComponent<TMP_Text>();
            emptyLabel.text = "품목을 선택하면\n여기에 정보가 표시됩니다";
            emptyLabel.fontSize = 22f;
            _illustEmptyLabel = emptyLabel.gameObject;

            var info = NewChild("Info", zone);
            Stretch(info);
            _illustInfo = info.gameObject;

            var iconRt = NewChild("Icon", info);
            iconRt.anchorMin = new Vector2(0.5f, 1f);
            iconRt.anchorMax = new Vector2(0.5f, 1f);
            iconRt.pivot = new Vector2(0.5f, 1f);
            iconRt.sizeDelta = new Vector2(180f, 180f);
            iconRt.anchoredPosition = new Vector2(0f, -24f);
            _illustIcon = iconRt.gameObject.AddComponent<UImage>();
            _illustIcon.preserveAspect = true;

            _illustNameLabel = MakeStretchedLabel(info, "NameLabel", 26f, TextAlignmentOptions.Center,
                top: 212f, height: 44f, margin: 16f);

            _illustDescLabel = MakeStretchedLabel(info, "DescLabel", 20f, TextAlignmentOptions.TopLeft,
                top: 264f, height: 168f, margin: 16f);
            _illustDescLabel.color = new Color(0.86f, 0.84f, 0.80f);

            var buttonRt = NewChild("ConfirmButton", info);
            buttonRt.anchorMin = new Vector2(0.5f, 0f);
            buttonRt.anchorMax = new Vector2(0.5f, 0f);
            buttonRt.pivot = new Vector2(0.5f, 0f);
            buttonRt.sizeDelta = new Vector2(300f, 72f);
            buttonRt.anchoredPosition = new Vector2(0f, 20f);

            var buttonImage = buttonRt.gameObject.AddComponent<UImage>();
            buttonImage.sprite = UITheme.Find(UITheme.ButtonNormalKey);
            buttonImage.type = UImage.Type.Sliced;

            _illustConfirmButton = UITheme.EnsureButton(buttonRt.gameObject);
            _illustConfirmButton.onClick.AddListener(OnIllustrationConfirmClicked);

            _illustConfirmLabel = MakeLabel(buttonRt, "Label", 24f, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, 0f);
            Stretch((RectTransform)_illustConfirmLabel.transform);

            ResetIllustrationZone();
        }

        /// <summary>
        /// 가로로 꽉 채우고(좌우 여백만 두고) 위에서부터 <paramref name="top"/>만큼 내려온 라벨.
        /// </summary>
        /// <remarks>
        /// ★ <c>anchoredPosition</c> + <c>sizeDelta</c> 조합으로 만들었다가 실제로 폭이 부모보다
        /// 넓게 잡혀 설명 텍스트가 <c>IllustrationZone</c> 밖으로 흘러나왔다 — 가로 스트레치
        /// (<c>anchorMin.x=0, anchorMax.x=1</c>) 상태에서는 <c>anchoredPosition.x</c>가 아니라
        /// <c>offsetMin</c>/<c>offsetMax</c>로 좌우 여백을 줘야 폭이 부모를 넘지 않는다.
        /// </remarks>
        private static TMP_Text MakeStretchedLabel(Transform parent, string name, float fontSize,
                                                    TextAlignmentOptions alignment, float top, float height, float margin)
        {
            var rt = NewChild(name, parent);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(margin, -(top + height));
            rt.offsetMax = new Vector2(-margin, -top);

            var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = Color.white;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Truncate;
            return label;
        }

        private static TMP_Text MakeLabel(Transform parent, string name, float fontSize,
                                          TextAlignmentOptions alignment, Vector2 anchorMin, Vector2 anchorMax,
                                          Vector2 pivot, Vector2 anchoredPosition, float height)
        {
            var rt = NewChild(name, parent);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPosition;
            if (height > 0f) rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);

            var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = Color.white;
            label.raycastTarget = false;
            return label;
        }

        private static RectTransform NewChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private void BindTargetPicker()
        {
            _targetPicker = transform.Find("TargetPicker");
            if (_targetPicker == null) return; // 아직 씬에 없으면 자동 지정으로 조용히 폴백한다.

            var panel = _targetPicker.Find("Panel");
            _targetSlots = new TargetSlot[3];
            for (int i = 0; i < 3; i++)
            {
                var slot = panel.Find("Slot" + i);
                var button = UITheme.EnsureButton(slot.gameObject);
                var face = slot.Find("Face").GetComponent<UImage>();
                var nameLabel = slot.Find("NameLabel").GetComponent<TMP_Text>();
                var statusLabel = slot.Find("StatusLabel").GetComponent<TMP_Text>();
                _targetSlots[i] = new TargetSlot(button, face, nameLabel, statusLabel);

                int captured = i;
                button.onClick.AddListener(() => OnTargetSlotClicked(captured));
            }

            _targetCancelButton = UITheme.EnsureButton(panel.Find("CancelButton").gameObject);
            _targetCancelButton.onClick.AddListener(CloseTargetPicker);

            _targetPicker.gameObject.SetActive(false);
        }

        private void BindSlot(int index, string childName)
        {
            var slot = _board.Find(childName);
            var button = UITheme.EnsureButton(slot.gameObject);
            _slotButtons[index] = button;
            _slotLabels[index] = slot.Find("Label").GetComponent<TMP_Text>();
            _slotIcons[index] = slot.Find("Icon")?.GetComponent<UImage>();
            button.onClick.AddListener(() => OnSlotClicked(index));
        }

        private void OnEnable()
        {
            RunManager.Instance?.EnsureShopRestocked();
            _sellMode = false;
            CloseTargetPicker();
            ResetIllustrationZone();
            RefreshUI();
        }

        private void SetSellMode(bool sell)
        {
            _sellMode = sell;
            RefreshTabs();
            ResetIllustrationZone();
            RefreshUI();
        }

        /// <summary>
        /// 어느 탭이 켜져 있는지 그림으로 보여준다.
        /// </summary>
        /// <remarks>
        /// 지금까지 두 탭이 <b>완전히 같게 그려졌다.</b> 화면 내용은 바뀌는데 탭은 그대로라
        /// "구매 화면인지 판매 화면인지"를 매번 슬롯을 보고 역추론해야 했다.
        /// 팩에 <c>tab_selected</c>/<c>tab_unselected</c> 가 이미 들어 있다.
        /// </remarks>
        private void RefreshTabs()
        {
            var selected = UITheme.Find("UI/Theme/tab_selected");
            var unselected = UITheme.Find("UI/Theme/tab_unselected");
            if (selected == null || unselected == null) return;

            var buy = _buyTab.targetGraphic as UImage;
            var sell = _sellTab.targetGraphic as UImage;
            if (buy != null) buy.sprite = _sellMode ? unselected : selected;
            if (sell != null) sell.sprite = _sellMode ? selected : unselected;
        }

        private void RefreshUI()
        {
            var mgr = RunManager.Instance;
            var run = mgr != null ? mgr.CurrentRun : null;
            if (mgr == null || run == null) return;

            _currencyLabel.text = run.Currency.ToString();

            if (_sellMode) RefreshSellSlots(run);
            else RefreshBuySlots(mgr, run);

            // 구매·판매·리롤이 전부 여기로 돌아오므로 띠도 같이 갱신된다 —
            // 산 직후에 체력/보유가 안 바뀐 것처럼 보이면 "샀는데 안 붙었나"로 읽힌다.
            RefreshRosterStrip(mgr);
        }

        private void RefreshBuySlots(RunManager mgr, RunState run)
        {
            var offers = mgr.CurrentShop?.Offers;

            for (int i = 0; i < _slotButtons.Length; i++)
            {
                if (offers == null || i >= offers.Count)
                {
                    _slotLabels[i].text = "-";
                    _slotButtons[i].interactable = false;
                    SetIcon(i, null);
                    continue;
                }

                var offer = offers[i];
                _slotLabels[i].text = $"{OfferLabel(offer, mgr)}\n{offer.Price}";
                _slotButtons[i].interactable = run.Currency >= offer.Price;
                SetIcon(i, IconFor(offer, mgr.Data));
            }
        }

        private void RefreshSellSlots(RunState run)
        {
            _sellableItems.Clear();

            foreach (var owned in run.TeamItems)
                _sellableItems.Add(new SellEntry(null, owned.Key, owned.OptionIndex));

            foreach (var entry in run.Deployed)
                foreach (var owned in entry.Items)
                    _sellableItems.Add(new SellEntry(entry.CharacterId, owned.Key, owned.OptionIndex));

            for (int i = 0; i < _slotButtons.Length; i++)
            {
                if (i >= _sellableItems.Count)
                {
                    _slotLabels[i].text = "-";
                    _slotButtons[i].interactable = false;
                    SetIcon(i, null);
                    continue;
                }

                var item = _sellableItems[i];
                string name = ItemNames.TryGetValue(item.ItemKey, out string label) ? label : item.ItemKey;
                _slotLabels[i].text = $"{name}\n판매";
                _slotButtons[i].interactable = true;
                SetIcon(i, IconForItemKey(item.ItemKey));
            }
        }

        private void SetIcon(int slotIndex, Sprite sprite)
        {
            var icon = _slotIcons[slotIndex];
            if (icon == null) return;

            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        /// <summary>스킬은 <see cref="DomoNinja.Core.Data.SkillDef.Icon"/>, 아이템은 `economy.json`의
        /// 원시 <c>items.{id}.icon</c> 필드에서 읽는다 — 아이템은 전용 클래스가 없어 `Raw`로 조회한다.</summary>
        private Sprite IconFor(ShopOffer offer, GameData data)
        {
            switch (offer.Kind)
            {
                case OfferKind.ActiveSkill:
                case OfferKind.SupportSkill:
                    var skill = data.FindSkill(offer.Id);
                    return skill?.Icon != null ? _catalog?.Find(skill.Icon) : null;

                default:
                    return IconForItemKey(offer.Id);
            }
        }

        private Sprite IconForItemKey(string itemKey)
        {
            var data = RunManager.Instance?.Data;
            string iconKey = data?.Economy.Raw["items"]?[itemKey]?["icon"]?.Value<string>();
            return iconKey != null ? _catalog?.Find(iconKey) : null;
        }

        private void OnSlotClicked(int slotIndex)
        {
            var mgr = RunManager.Instance;
            if (mgr == null) return;

            if (_sellMode) OnSellClicked(mgr, slotIndex);
            else OnBuyClicked(mgr, slotIndex);

            RefreshUI();
        }

        /// <summary>
        /// 예전엔 여기서 바로 전체화면 확인창(UIConfirmPopup)을 띄웠다. 이제 그 자리를
        /// <see cref="ShowOfferInfo"/> 가 대신한다 — IllustrationZone에 정보를 채우고,
        /// 실제 구매는 그 안의 구매 버튼(<see cref="OnIllustrationConfirmClicked"/>)이 맡는다.
        /// </summary>
        private void OnBuyClicked(RunManager mgr, int offerIndex)
        {
            var offers = mgr.CurrentShop?.Offers;
            if (offers == null || offerIndex >= offers.Count) return;

            if (_illustInfo == null)
            {
                // 구버전 씬 폴백 — IllustrationZone이 없으면 예전처럼 즉시 대상 선택/구매로 간다.
                var offer = offers[offerIndex];
                if (!NeedsTarget(offer)) ExecutePurchase(mgr, offerIndex, null);
                else if (_targetPicker == null) ExecutePurchase(mgr, offerIndex, FirstAliveCharacterId(mgr));
                else OpenTargetPicker(mgr, offerIndex);
                return;
            }

            ShowOfferInfo(mgr, offerIndex);
        }

        /// <summary>눌린 품목의 아이콘·이름·설명을 IllustrationZone에 채우고 구매 버튼을 켠다.</summary>
        private void ShowOfferInfo(RunManager mgr, int offerIndex)
        {
            var offers = mgr.CurrentShop?.Offers;
            if (offers == null || offerIndex >= offers.Count) return;

            var offer = offers[offerIndex];

            _illustOfferIndex = offerIndex;
            _illustIcon.sprite = IconFor(offer, mgr.Data);
            _illustIcon.enabled = _illustIcon.sprite != null;
            _illustNameLabel.text = OfferLabel(offer, mgr);
            _illustDescLabel.text = DescribeOffer(mgr, offer);
            _illustConfirmLabel.text = $"{offer.Price} 재화에 구매";
            _illustConfirmButton.interactable = mgr.CurrentRun.Currency >= offer.Price;

            _illustEmptyLabel.SetActive(false);
            _illustInfo.SetActive(true);
        }

        /// <summary>스킬은 <see cref="InfoPopupController.FormatSkill"/>, 아이템은
        /// <see cref="InfoPopupController.DescribeItem"/> 로 그대로 재사용한다 — 정보 팝업과 상점
        /// 일러스트 영역의 설명 문구가 따로 놀면 언젠가 어긋난다.
        /// 스킬에 <c>Flavor</c>(캐릭터가 그 스킬을 두고 하는 한마디)가 있으면 맨 위에 얹는다 —
        /// 메인 12개만 갖고 보조 18개·아이템은 없다(값이 없으면 그냥 안 붙는다).</summary>
        private static string DescribeOffer(RunManager mgr, ShopOffer offer)
        {
            switch (offer.Kind)
            {
                case OfferKind.ActiveSkill:
                case OfferKind.SupportSkill:
                    var skill = mgr.Data.FindSkill(offer.Id);
                    if (skill == null) return "-";
                    string mechanics = InfoPopupController.FormatSkill(skill);
                    return string.IsNullOrEmpty(skill.Flavor) ? mechanics : $"“{skill.Flavor}”\n\n{mechanics}";

                default:
                    return InfoPopupController.DescribeItem(mgr, offer.Id, offer.OptionIndex);
            }
        }

        /// <summary>정보 표시를 접고 빈 상태 안내문으로 되돌린다. 구매·리롤·탭 전환·화면 재진입 때 부른다 —
        /// 방금 산 품목의 정보가 화면에 그대로 남아 있으면 "또 살 수 있나"로 헷갈린다.</summary>
        private void ResetIllustrationZone()
        {
            _illustOfferIndex = -1;
            if (_illustInfo == null) return;

            _illustInfo.SetActive(false);
            _illustEmptyLabel.SetActive(true);
        }

        /// <summary>IllustrationZone의 구매 버튼. 대상 지정이 필요한 품목이면 대상 선택으로 이어간다.</summary>
        private void OnIllustrationConfirmClicked()
        {
            var mgr = RunManager.Instance;
            if (mgr == null || _illustOfferIndex < 0) return;

            var offers = mgr.CurrentShop?.Offers;
            if (offers == null || _illustOfferIndex >= offers.Count) return;

            int offerIndex = _illustOfferIndex;
            var offer = offers[offerIndex];

            if (!NeedsTarget(offer))
            {
                ExecutePurchase(mgr, offerIndex, null);
                return;
            }

            // 대상 선택 UI가 씬에 없으면(구버전 씬 등) 생존한 첫 캐릭터로 자동 지정해 폴백한다.
            if (_targetPicker == null)
            {
                ExecutePurchase(mgr, offerIndex, FirstAliveCharacterId(mgr));
                return;
            }

            OpenTargetPicker(mgr, offerIndex);
        }

        /// <summary>실제 구매 실행. <see cref="RunManager.TryBuy"/> 를 부르는 곳은 여기 한 곳뿐이다.</summary>
        private void ExecutePurchase(RunManager mgr, int offerIndex, string targetCharacterId)
        {
            mgr.TryBuy(offerIndex, targetCharacterId);
            ResetIllustrationZone();
            RefreshUI();
        }

        /// <summary>대상 지정이 필요한 품목(`statBoost`·`conditionalBoost`·`healItem`). `teamBoost`는 전체 대상이라 제외.</summary>
        private static bool NeedsTarget(ShopOffer offer) => offer.Kind == OfferKind.Item && offer.Id != "teamBoost";

        private static string FirstAliveCharacterId(RunManager mgr)
        {
            foreach (var entry in mgr.CurrentRun.Deployed)
                if (entry.IsAlive) return entry.CharacterId;

            return null;
        }

        private void OpenTargetPicker(RunManager mgr, int offerIndex)
        {
            _pendingOfferIndex = offerIndex;

            var deployed = mgr.CurrentRun.Deployed;
            for (int i = 0; i < _targetSlots.Length; i++)
            {
                if (i >= deployed.Count)
                {
                    _targetSlots[i].Button.gameObject.SetActive(false);
                    continue;
                }

                var entry = deployed[i];
                var def = mgr.Data.FindCharacter(entry.CharacterId);

                _targetSlots[i].Button.gameObject.SetActive(true);
                _targetSlots[i].Button.interactable = entry.IsAlive;
                _targetSlots[i].Face.color = entry.IsAlive ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                var slotImg = _targetSlots[i].Button.GetComponent<UImage>();
                if (slotImg != null) slotImg.color = entry.IsAlive ? AliveSlotColor : DeadSlotColor;
                _targetSlots[i].Face.sprite = def != null ? _catalog?.Find(def.Sprite) : null;
                _targetSlots[i].NameLabel.text = def != null ? def.Name : entry.CharacterId;
                _targetSlots[i].StatusLabel.text = entry.IsAlive ? $"HP {entry.Hp}/{entry.MaxHp}" : "사망";

                // 누구에게 붙일지 고르는 자리다 — 여기서 스펙을 못 보면 "누가 더 급한가"를
                // 체력 한 줄만 보고 정하게 된다. 이미 산 것도 같이 보여야 중복 강화를 피한다.
                var hover = _targetSlots[i].Button.gameObject.GetComponent<HoverTooltipTrigger>()
                            ?? _targetSlots[i].Button.gameObject.AddComponent<HoverTooltipTrigger>();
                var capturedEntry = entry;
                var capturedDef = def;
                hover.Describe = () => capturedDef == null
                    ? null
                    : UnitStatText.ForDeployedAlly(capturedDef, capturedEntry, mgr.Data);
            }

            _targetPicker.gameObject.SetActive(true);
        }

        private void OnTargetSlotClicked(int slotIndex)
        {
            var mgr = RunManager.Instance;
            if (mgr == null || _pendingOfferIndex < 0) return;

            var deployed = mgr.CurrentRun.Deployed;
            if (slotIndex >= deployed.Count || !deployed[slotIndex].IsAlive) return;

            int offerIndex = _pendingOfferIndex;
            string targetCharacterId = deployed[slotIndex].CharacterId;

            CloseTargetPicker();
            ExecutePurchase(mgr, offerIndex, targetCharacterId);
        }

        private void CloseTargetPicker()
        {
            _pendingOfferIndex = -1;
            if (_targetPicker != null) _targetPicker.gameObject.SetActive(false);
        }

        private void OnSellClicked(RunManager mgr, int slotIndex)
        {
            if (slotIndex >= _sellableItems.Count) return;
            var item = _sellableItems[slotIndex];

            if (item.CharacterId == null)
                mgr.TrySellTeamItem(item.ItemKey, item.OptionIndex);
            else
                mgr.TrySellItem(item.CharacterId, item.ItemKey, item.OptionIndex);
        }

        private void OnReroll()
        {
            RunManager.Instance?.TryReroll();
            ResetIllustrationZone(); // 리롤로 슬롯 구성이 통째로 바뀐다 — 보여주던 정보가 더는 유효하지 않다.
            RefreshUI();
        }

        private void OnNextRound()
        {
            UIScreenManager.ShowScreen("GamePlay");
            UIScreenManager.ShowPopup("StageIntro");
        }

        private static string OfferLabel(ShopOffer offer, RunManager mgr)
        {
            switch (offer.Kind)
            {
                case OfferKind.ActiveSkill:
                case OfferKind.SupportSkill:
                    var skill = mgr.Data.FindSkill(offer.Id);
                    return skill != null ? skill.Name : offer.Id;

                default:
                    return ItemNames.TryGetValue(offer.Id, out string name) ? name : offer.Id;
            }
        }    }
}
