using System.Collections.Generic;
using System.Text;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Unity.View;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using UImage = UnityEngine.UI.Image;

namespace DomoNinja.Unity
{
    /// <summary>
    /// 정보 팝업. 탭 전환(캐릭터/아이템) + 출전 로스터·보유 아이템 실데이터 표시.
    /// </summary>
    /// <remarks>GamePlay·Shop 화면에서 열리므로 항상 진행 중인 런을 전제한다 — 런 밖에서 열 일이 없다.</remarks>
    public sealed class InfoPopupController : MonoBehaviour
    {
        private readonly struct PortraitSlot
        {
            public readonly Button Button;
            public readonly UImage Face;
            public readonly Text NameLabel;
            public readonly Text StatusLabel;

            public PortraitSlot(Button button, UImage face, Text nameLabel, Text statusLabel)
            {
                Button = button; Face = face; NameLabel = nameLabel; StatusLabel = statusLabel;
            }
        }

        private readonly struct ItemEntry
        {
            /// <summary>null 이면 팀 전체 아이템.</summary>
            public readonly string CharacterId;
            public readonly string ItemKey;
            public readonly int OptionIndex;

            public ItemEntry(string characterId, string itemKey, int optionIndex)
            {
                CharacterId = characterId; ItemKey = itemKey; OptionIndex = optionIndex;
            }
        }

        private static readonly Color SelectedColor = new Color(0.85f, 0.55f, 0.20f);
        private static readonly Color UnselectedColor = new Color(0.22f, 0.23f, 0.25f);
        private static readonly Color EmptyColor = new Color(0.14f, 0.14f, 0.15f);

        private GameObject _characterTab;
        private GameObject _itemTab;
        private SpriteCatalog _catalog;

        private PortraitSlot[] _portraitSlots;
        private Text _profileLabel;
        private Text _statsLabel;
        private UImage _activeSkillIcon;
        private Text _activeSkillLabel;
        private Text _supportSkillLabel;

        private Button[] _itemSlotButtons;
        private UImage[] _itemSlotIcons;
        private UImage _detailIcon;
        private Text _detailNameLabel;
        private Text _detailDescLabel;

        private int _selectedCharacterIndex;
        private int _selectedItemIndex;
        private readonly List<ItemEntry> _items = new List<ItemEntry>();

        private void Awake()
        {
            var board = transform.Find("Board");
            _characterTab = board.Find("CharacterTab").gameObject;
            _itemTab = board.Find("ItemTab").gameObject;

            var tabCharacter = EnsureButton(board.Find("TabCharacter").gameObject);
            var tabItem = EnsureButton(board.Find("TabItem").gameObject);
            var closeButton = EnsureButton(board.Find("CloseButton").gameObject);

            tabCharacter.onClick.AddListener(() => ShowTab(character: true));
            tabItem.onClick.AddListener(() => ShowTab(character: false));
            closeButton.onClick.AddListener(() => UIScreenManager.HidePopup("InfoPopup"));

            _catalog = Resources.Load<SpriteCatalog>(SpriteCatalog.ResourceName);

            BindCharacterTab();
            BindItemTab();
        }

        private void BindCharacterTab()
        {
            var characterTab = _characterTab.transform;
            _portraitSlots = new PortraitSlot[3];

            for (int i = 0; i < 3; i++)
            {
                var slot = characterTab.Find("PortraitSlot" + i);
                var btn = EnsureButton(slot.gameObject);
                var face = slot.Find("Face").GetComponent<UImage>();
                var nameLabel = slot.Find("NameLabel").GetComponent<Text>();
                var statusLabel = slot.Find("StatusLabel").GetComponent<Text>();
                _portraitSlots[i] = new PortraitSlot(btn, face, nameLabel, statusLabel);

                int captured = i;
                btn.onClick.AddListener(() => { _selectedCharacterIndex = captured; RefreshCharacterTab(); });
            }

            _profileLabel = characterTab.Find("ProfileBody/Label").GetComponent<Text>();
            _statsLabel = characterTab.Find("StatsBody/Label").GetComponent<Text>();
            _activeSkillIcon = characterTab.Find("ActiveSkillIcon").GetComponent<UImage>();
            _activeSkillLabel = characterTab.Find("ActiveSkillBody/Label").GetComponent<Text>();
            _supportSkillLabel = characterTab.Find("SupportSkillBody/Label").GetComponent<Text>();
        }

        private void BindItemTab()
        {
            var itemTab = _itemTab.transform;
            _itemSlotButtons = new Button[4];
            _itemSlotIcons = new UImage[4];

            for (int i = 0; i < 4; i++)
            {
                var slot = itemTab.Find("OwnedItemSlot" + i);
                var btn = EnsureButton(slot.gameObject);
                _itemSlotButtons[i] = btn;
                _itemSlotIcons[i] = slot.Find("Icon").GetComponent<UImage>();

                int captured = i;
                btn.onClick.AddListener(() => { _selectedItemIndex = captured; RefreshItemDetail(); });
            }

            _detailIcon = itemTab.Find("DetailIcon").GetComponent<UImage>();
            _detailNameLabel = itemTab.Find("DetailNameBody/Label").GetComponent<Text>();
            _detailDescLabel = itemTab.Find("DetailDescBody/Label").GetComponent<Text>();
        }

        private void OnEnable()
        {
            _selectedCharacterIndex = 0;
            _selectedItemIndex = 0;
            ShowTab(character: true);
        }

        private void ShowTab(bool character)
        {
            _characterTab.SetActive(character);
            _itemTab.SetActive(!character);

            if (character) RefreshCharacterTab();
            else RefreshItemTab();
        }

        // ────────────────────────────── 캐릭터 탭

        private void RefreshCharacterTab()
        {
            var mgr = RunManager.Instance;
            var run = mgr != null ? mgr.CurrentRun : null;
            if (mgr == null || run == null) return;

            var deployed = run.Deployed;
            if (_selectedCharacterIndex >= deployed.Count) _selectedCharacterIndex = 0;

            for (int i = 0; i < _portraitSlots.Length; i++)
            {
                bool has = i < deployed.Count;
                var entry = has ? deployed[i] : null;
                var def = entry != null ? mgr.Data.FindCharacter(entry.CharacterId) : null;

                var slotImg = _portraitSlots[i].Button.GetComponent<UImage>();
                bool selected = has && i == _selectedCharacterIndex;
                if (slotImg != null) slotImg.color = selected ? SelectedColor : (has ? UnselectedColor : EmptyColor);
                _portraitSlots[i].Button.interactable = has;

                _portraitSlots[i].Face.gameObject.SetActive(has);
                _portraitSlots[i].Face.sprite = def != null ? _catalog?.Find(def.Sprite) : null;
                _portraitSlots[i].NameLabel.text = def != null ? def.Name : "-";
                _portraitSlots[i].StatusLabel.text = entry == null ? ""
                    : entry.IsAlive ? $"HP {entry.Hp}/{entry.MaxHp}" : "사망";
            }

            if (deployed.Count == 0)
            {
                _profileLabel.text = "-";
                _statsLabel.text = "-";
                _activeSkillIcon.sprite = null;
                _activeSkillLabel.text = "-";
                _supportSkillLabel.text = "-";
                return;
            }

            var selected1 = deployed[_selectedCharacterIndex];
            var def1 = mgr.Data.FindCharacter(selected1.CharacterId);

            _profileLabel.text = def1 != null ? def1.Name : selected1.CharacterId;
            _statsLabel.text = def1 != null
                ? $"공격력 {def1.Attack}   공격 간격 {def1.AttackInterval}   사거리 {def1.Range}   HP {selected1.Hp}/{selected1.MaxHp}"
                : $"HP {selected1.Hp}/{selected1.MaxHp}";

            if (selected1.ActiveSkillId != null)
            {
                var skill = mgr.Data.FindSkill(selected1.ActiveSkillId);
                _activeSkillIcon.sprite = skill != null && skill.Icon != null ? _catalog?.Find(skill.Icon) : null;
                _activeSkillLabel.text = skill != null ? FormatSkill(skill) : selected1.ActiveSkillId;
            }
            else
            {
                _activeSkillIcon.sprite = null;
                _activeSkillLabel.text = "미선택";
            }

            if (selected1.SupportSkillIds.Count == 0)
            {
                _supportSkillLabel.text = "없음";
            }
            else
            {
                var sb = new StringBuilder();
                foreach (var id in selected1.SupportSkillIds)
                {
                    var skill = mgr.Data.FindSkill(id);
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(skill != null ? FormatSkill(skill) : id);
                }
                _supportSkillLabel.text = sb.ToString();
            }
        }

        /// <summary>스킬 표기 규칙(`08` §2.6) — 이름 + 「얻는 것 / 버리는 것」 2줄.</summary>
        private static string FormatSkill(SkillDef skill)
        {
            var sb = new StringBuilder(skill.Name);
            if (skill.TextGain != null) sb.Append("\n얻는 것: ").Append(skill.TextGain);
            if (skill.TextCost != null) sb.Append("\n버리는 것: ").Append(skill.TextCost);
            return sb.ToString();
        }

        // ────────────────────────────── 아이템 탭

        private void RefreshItemTab()
        {
            var mgr = RunManager.Instance;
            var run = mgr != null ? mgr.CurrentRun : null;
            if (mgr == null || run == null) return;

            _items.Clear();
            foreach (var owned in run.TeamItems)
                _items.Add(new ItemEntry(null, owned.Key, owned.OptionIndex));

            foreach (var entry in run.Deployed)
                foreach (var owned in entry.Items)
                    _items.Add(new ItemEntry(entry.CharacterId, owned.Key, owned.OptionIndex));

            if (_selectedItemIndex >= _items.Count) _selectedItemIndex = 0;

            for (int i = 0; i < _itemSlotButtons.Length; i++)
            {
                bool has = i < _items.Count;
                _itemSlotButtons[i].interactable = has;

                var slotImg = _itemSlotButtons[i].GetComponent<UImage>();
                bool selected = has && i == _selectedItemIndex;
                if (slotImg != null) slotImg.color = selected ? SelectedColor : (has ? UnselectedColor : EmptyColor);

                _itemSlotIcons[i].gameObject.SetActive(has);
                _itemSlotIcons[i].sprite = has ? FindItemIcon(mgr, _items[i].ItemKey) : null;
            }

            RefreshItemDetail();
        }

        private void RefreshItemDetail()
        {
            var mgr = RunManager.Instance;
            if (mgr == null || _selectedItemIndex >= _items.Count || _items.Count == 0)
            {
                _detailIcon.sprite = null;
                _detailNameLabel.text = "-";
                _detailDescLabel.text = "보유한 아이템이 없다";
                return;
            }

            var item = _items[_selectedItemIndex];
            string name = ShopController.ItemNames.TryGetValue(item.ItemKey, out string n) ? n : item.ItemKey;
            string owner = item.CharacterId != null
                ? mgr.Data.FindCharacter(item.CharacterId)?.Name ?? item.CharacterId
                : "팀 전체";

            _detailNameLabel.text = $"{name}\n({owner})";
            _detailIcon.sprite = FindItemIcon(mgr, item.ItemKey);
            _detailDescLabel.text = DescribeItem(mgr, item.ItemKey, item.OptionIndex);
        }

        private Sprite FindItemIcon(RunManager mgr, string itemKey)
        {
            var item = (mgr.Data.Economy.Raw["items"] as JObject)?[itemKey] as JObject;
            string icon = (string)item?["icon"];
            return icon != null ? _catalog?.Find(icon) : null;
        }

        /// <summary>아이템의 실제 수치 효과를 데이터에서 그대로 읽어 문장으로 만든다. 표시용 상수를 코드에 박지 않는다.</summary>
        private static string DescribeItem(RunManager mgr, string itemKey, int optionIndex)
        {
            var item = (mgr.Data.Economy.Raw["items"] as JObject)?[itemKey] as JObject;
            if (item == null) return "-";

            if (itemKey == "healItem")
            {
                int permille = (int?)item["healPermille"] ?? 0;
                return $"구매 즉시 체력 {permille / 10f:0.#}% 회복";
            }

            if (!(item["options"] is JArray options) || optionIndex < 0 || optionIndex >= options.Count)
                return "-";

            if (!(options[optionIndex] is JObject option)) return "-";

            return itemKey == "conditionalBoost" ? DescribeConditional(option) : DescribeStatOption(option);
        }

        private static string DescribeStatOption(JObject option)
        {
            string stat = StatName((string)option["stat"]);
            double value = (double?)option["value"] ?? 0;
            bool isMultiplier = (bool?)option["isMultiplier"] ?? false;
            return isMultiplier ? $"{stat} ×{1 + value:0.##}" : $"{stat} +{value:0.##}";
        }

        private static string DescribeConditional(JObject option)
        {
            string stat = StatName((string)option["stat"]);
            double mult = (double?)option["mult"] ?? 0;

            switch ((string)option["condition"])
            {
                case "hp_below":
                    double hpBelow = (double?)option["value"] ?? 0;
                    return $"HP {hpBelow * 100:0}% 이하일 때 {stat} ×{1 + mult:0.##}";
                case "enemies_above":
                    int count = (int?)option["value"] ?? 0;
                    return $"적이 {count}체 이상일 때 {stat} ×{1 + mult:0.##}";
                case "is_last_alive":
                    return $"팀의 마지막 생존자일 때 {stat} ×{1 + mult:0.##}";
                default:
                    return (string)option["condition"] ?? "-";
            }
        }

        private static string StatName(string stat)
        {
            switch (stat)
            {
                case "attack": return "공격력";
                case "hp": return "체력";
                case "attackInterval": return "공격 간격";
                case "damageTaken": return "받는 피해";
                default: return stat;
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
