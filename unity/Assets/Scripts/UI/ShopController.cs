using System.Collections.Generic;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Economy;
using UnityEngine;
using UnityEngine.UI;
using UImage = UnityEngine.UI.Image;

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
        private static readonly Dictionary<string, string> ItemNames = new Dictionary<string, string>
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

        private Transform _board;
        private Text _currencyLabel;
        private Button[] _slotButtons;
        private Text[] _slotLabels;
        private Button _buyTab;
        private Button _sellTab;
        private Button _rerollButton;
        private Button _nextRoundButton;

        private bool _sellMode;
        private readonly List<SellEntry> _sellableItems = new List<SellEntry>();

        private void Awake()
        {
            _board = transform.Find("Board");
            _currencyLabel = _board.Find("CurrencyDisplay/Label").GetComponent<Text>();

            _slotButtons = new Button[5];
            _slotLabels = new Text[5];

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
        }

        private void BindSlot(int index, string childName)
        {
            var slot = _board.Find(childName);
            var button = EnsureButton(slot.gameObject);
            _slotButtons[index] = button;
            _slotLabels[index] = slot.Find("Label").GetComponent<Text>();
            button.onClick.AddListener(() => OnSlotClicked(index));
        }

        private void OnEnable()
        {
            RunManager.Instance?.EnsureShopRestocked();
            _sellMode = false;
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
            mgr.TryBuy(offerIndex, TargetForOffer(mgr, offer));
        }

        /// <summary>
        /// 대상 지정이 필요한 품목(`statBoost`·`conditionalBoost`·`healItem`)은
        /// 아직 대상 선택 UI가 없어 <b>생존한 첫 번째 출전 캐릭터로 자동 지정한다.</b>
        /// </summary>
        private static string TargetForOffer(RunManager mgr, ShopOffer offer)
        {
            if (offer.Kind != OfferKind.Item || offer.Id == "teamBoost") return null;

            foreach (var entry in mgr.CurrentRun.Deployed)
                if (entry.IsAlive) return entry.CharacterId;

            return null;
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
