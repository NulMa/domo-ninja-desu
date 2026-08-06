using System.Collections.Generic;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Economy;
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

        private void Awake()
        {
            _board = transform.Find("Board");
            _currencyLabel = _board.Find("CurrencyDisplay/Label").GetComponent<TMP_Text>();

            _slotButtons = new Button[5];
            _slotLabels = new TMP_Text[5];

            for (int i = 0; i < 3; i++) BindSlot(i, "SkillSlot" + (i + 1));
            for (int i = 0; i < 2; i++) BindSlot(3 + i, "ItemSlot" + (i + 1));

            _buyTab = EnsureButton(_board.Find("BuyTab").gameObject);
            _sellTab = EnsureButton(_board.Find("SellTab").gameObject);
            _rerollButton = EnsureButton(_board.Find("RerollButton").gameObject);
            _nextRoundButton = EnsureButton(_board.Find("NextRoundButton").gameObject);

            _buyTab.onClick.AddListener(() => SetSellMode(false));
            _sellTab.onClick.AddListener(() => SetSellMode(true));
            _rerollButton.onClick.AddListener(OnReroll);
            _nextRoundButton.onClick.AddListener(OnNextRound);

            _catalog = Resources.Load<SpriteCatalog>(SpriteCatalog.ResourceName);
            BindTargetPicker();
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
                var button = EnsureButton(slot.gameObject);
                var face = slot.Find("Face").GetComponent<UImage>();
                var nameLabel = slot.Find("NameLabel").GetComponent<TMP_Text>();
                var statusLabel = slot.Find("StatusLabel").GetComponent<TMP_Text>();
                _targetSlots[i] = new TargetSlot(button, face, nameLabel, statusLabel);

                int captured = i;
                button.onClick.AddListener(() => OnTargetSlotClicked(captured));
            }

            _targetCancelButton = EnsureButton(panel.Find("CancelButton").gameObject);
            _targetCancelButton.onClick.AddListener(CloseTargetPicker);

            _targetPicker.gameObject.SetActive(false);
        }

        private void BindSlot(int index, string childName)
        {
            var slot = _board.Find(childName);
            var button = EnsureButton(slot.gameObject);
            _slotButtons[index] = button;
            _slotLabels[index] = slot.Find("Label").GetComponent<TMP_Text>();
            button.onClick.AddListener(() => OnSlotClicked(index));
        }

        private void OnEnable()
        {
            RunManager.Instance?.EnsureShopRestocked();
            _sellMode = false;
            CloseTargetPicker();
            RefreshUI();
        }

        private void SetSellMode(bool sell)
        {
            _sellMode = sell;
            RefreshUI();
        }

        private void RefreshUI()
        {
            var mgr = RunManager.Instance;
            var run = mgr != null ? mgr.CurrentRun : null;
            if (mgr == null || run == null) return;

            _currencyLabel.text = $"재화 {run.Currency}";

            if (_sellMode) RefreshSellSlots(run);
            else RefreshBuySlots(mgr, run);
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
                    continue;
                }

                var offer = offers[i];
                _slotLabels[i].text = $"{OfferLabel(offer, mgr)}\n{offer.Price}";
                _slotButtons[i].interactable = run.Currency >= offer.Price;
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
                    continue;
                }

                var item = _sellableItems[i];
                string name = ItemNames.TryGetValue(item.ItemKey, out string label) ? label : item.ItemKey;
                _slotLabels[i].text = $"{name}\n판매";
                _slotButtons[i].interactable = true;
            }
        }

        private void OnSlotClicked(int slotIndex)
        {
            var mgr = RunManager.Instance;
            if (mgr == null) return;

            if (_sellMode) OnSellClicked(mgr, slotIndex);
            else OnBuyClicked(mgr, slotIndex);

            RefreshUI();
        }

        private void OnBuyClicked(RunManager mgr, int offerIndex)
        {
            var offers = mgr.CurrentShop?.Offers;
            if (offers == null || offerIndex >= offers.Count) return;

            var offer = offers[offerIndex];
            if (!NeedsTarget(offer))
            {
                mgr.TryBuy(offerIndex, null);
                return;
            }

            // 대상 선택 UI가 씬에 없으면(구버전 씬 등) 생존한 첫 캐릭터로 자동 지정해 폴백한다.
            if (_targetPicker == null)
            {
                mgr.TryBuy(offerIndex, FirstAliveCharacterId(mgr));
                return;
            }

            OpenTargetPicker(mgr, offerIndex);
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
            }

            _targetPicker.gameObject.SetActive(true);
        }

        private void OnTargetSlotClicked(int slotIndex)
        {
            var mgr = RunManager.Instance;
            if (mgr == null || _pendingOfferIndex < 0) return;

            var deployed = mgr.CurrentRun.Deployed;
            if (slotIndex >= deployed.Count || !deployed[slotIndex].IsAlive) return;

            mgr.TryBuy(_pendingOfferIndex, deployed[slotIndex].CharacterId);
            CloseTargetPicker();
            RefreshUI();
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
