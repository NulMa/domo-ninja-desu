using System.Collections.Generic;
using System.Text;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Unity.View;
using Newtonsoft.Json.Linq;
using TMPro;
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
            public readonly TMP_Text NameLabel;
            public readonly TMP_Text StatusLabel;

            public PortraitSlot(Button button, UImage face, TMP_Text nameLabel, TMP_Text statusLabel)
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

        // 스프라이트 위에 곱해지는 틴트다. 흰색이 원본 그대로다.
        private static readonly Color SelectedColor = new Color(1.00f, 0.82f, 0.45f);
        private static readonly Color UnselectedColor = Color.white;
        private static readonly Color EmptyColor = new Color(0.50f, 0.50f, 0.50f);

        private GameObject _characterTab;
        private GameObject _itemTab;
        private SpriteCatalog _catalog;

        private PortraitSlot[] _portraitSlots;
        private TMP_Text _profileLabel;
        private TMP_Text _statsLabel;
        private UImage _activeSkillIcon;
        private TMP_Text _activeSkillLabel;
        private TMP_Text _supportSkillLabel;

        private Button[] _itemSlotButtons;
        private UImage[] _itemSlotIcons;
        private UImage _detailIcon;
        private TMP_Text _detailNameLabel;
        private TMP_Text _detailDescLabel;

        private int _selectedCharacterIndex;
        private int _selectedItemIndex;
        private readonly List<ItemEntry> _items = new List<ItemEntry>();

        private void Awake()
        {
            var board = transform.Find("Board");
            _characterTab = board.Find("CharacterTab").gameObject;
            _itemTab = board.Find("ItemTab").gameObject;

            var tabCharacter = UITheme.EnsureButton(board.Find("TabCharacter").gameObject);
            var tabItem = UITheme.EnsureButton(board.Find("TabItem").gameObject);
            var closeButton = UITheme.EnsureButton(board.Find("CloseButton").gameObject);

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
                var btn = UITheme.EnsureButton(slot.gameObject);
                var face = slot.Find("Face").GetComponent<UImage>();
                var nameLabel = slot.Find("NameLabel").GetComponent<TMP_Text>();
                var statusLabel = slot.Find("StatusLabel").GetComponent<TMP_Text>();
                _portraitSlots[i] = new PortraitSlot(btn, face, nameLabel, statusLabel);

                int captured = i;
                btn.onClick.AddListener(() => { _selectedCharacterIndex = captured; RefreshCharacterTab(); });
            }

            _profileLabel = characterTab.Find("ProfileBody/Label").GetComponent<TMP_Text>();
            _statsLabel = characterTab.Find("StatsBody/Label").GetComponent<TMP_Text>();
            _activeSkillIcon = characterTab.Find("ActiveSkillIcon").GetComponent<UImage>();
            _activeSkillLabel = characterTab.Find("ActiveSkillBody/Label").GetComponent<TMP_Text>();
            _supportSkillLabel = characterTab.Find("SupportSkillBody/Label").GetComponent<TMP_Text>();
        }

        private void BindItemTab()
        {
            var itemTab = _itemTab.transform;
            _itemSlotButtons = new Button[4];
            _itemSlotIcons = new UImage[4];

            for (int i = 0; i < 4; i++)
            {
                var slot = itemTab.Find("OwnedItemSlot" + i);
                var btn = UITheme.EnsureButton(slot.gameObject);
                _itemSlotButtons[i] = btn;
                _itemSlotIcons[i] = slot.Find("Icon").GetComponent<UImage>();

                int captured = i;
                btn.onClick.AddListener(() => { _selectedItemIndex = captured; RefreshItemDetail(); });
            }

            _detailIcon = itemTab.Find("DetailIcon").GetComponent<UImage>();
            _detailNameLabel = itemTab.Find("DetailNameBody/Label").GetComponent<TMP_Text>();
            _detailDescLabel = itemTab.Find("DetailDescBody/Label").GetComponent<TMP_Text>();
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
                _activeSkillLabel.text = skill != null ? FormatSkill(skill, mgr.Data) : selected1.ActiveSkillId;
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
                    sb.Append(skill != null ? FormatSkill(skill, mgr.Data) : id);
                }
                _supportSkillLabel.text = sb.ToString();
            }
        }

        /// <summary>스킬 표기 규칙(`08` §2.6) — 이름 + 이득/대가 2줄. 두 줄을 가르는 건
        /// <b>글자가 아니라 색</b>이다(<see cref="UITheme.Semantic"/>). <c>ShopController</c>·
        /// <c>RosterSelectController</c>도 같이 쓴다 — 표기 규칙이 여러 군데서 갈리면 언젠가 어긋난다.</summary>
        /// <param name="data">
        /// 있으면 <b>누구 스킬인지</b>를 이름 옆에 붙인다. 없으면 예전처럼 이름만 나온다.
        /// </param>
        /// <remarks>
        /// 지시(사용자): *"스킬 정보에서 누구 스킬인지 떴으면 좋겠고."*
        /// <para>
        /// ★ <b>상점에서 특히 필요하다.</b> 스킬 이름만 보고는 <c>C4-B</c> 난사가 사냥꾼 것인지
        /// 알 수 없는데, <b>내 팀에 없는 캐릭터의 스킬은 사도 쓸 데가 없다.</b>
        /// 아이콘 그림으로 구분되기를 기대했지만 30종을 그림만으로 외우게 하는 셈이었다.
        /// </para>
        /// <para>
        /// <see cref="SkillDef.CharacterId"/> 는 <c>"C4"</c> 같은 id 라 그대로 띄우면 못 읽는다.
        /// 이름은 <c>characters.json</c> 에만 있으므로 <paramref name="data"/> 가 필요하다.
        /// </para>
        /// </remarks>
        internal static string FormatSkill(SkillDef skill, GameData data = null)
        {
            var sb = new StringBuilder(skill.Name);

            string owner = OwnerNameOf(skill, data);
            if (owner != null) sb.Append("  ").Append(UITheme.Semantic.Muted(owner));

            if (skill.TextGain != null) sb.Append('\n').Append(UITheme.Semantic.Gain(skill.TextGain));
            if (skill.TextCost != null) sb.Append('\n').Append(UITheme.Semantic.Cost(skill.TextCost));
            return sb.ToString();
        }

        /// <summary>스킬 주인의 <b>표시 이름</b>. 못 찾으면 <c>null</c> — 그러면 아무것도 안 붙는다.</summary>
        private static string OwnerNameOf(SkillDef skill, GameData data)
        {
            if (data == null || string.IsNullOrEmpty(skill.CharacterId)) return null;

            var owner = data.FindCharacter(skill.CharacterId);
            // 이름을 못 찾으면 id 라도 낸다 — 조용히 비면 "주인이 없는 스킬"로 읽힌다.
            return string.IsNullOrEmpty(owner?.Name) ? skill.CharacterId : owner.Name;
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

        /// <summary>아이템의 실제 수치 효과를 데이터에서 그대로 읽어 문장으로 만든다. 표시용 상수를 코드에 박지 않는다.
        /// <c>ShopController</c>도 같이 쓴다(상점 일러스트 영역) — <c>ShopOffer.OptionIndex</c>가 이미
        /// 추첨 시점에 정해져 있어 여기 그대로 넘기면 된다.</summary>
        internal static string DescribeItem(RunManager mgr, string itemKey, int optionIndex)
        {
            var item = (mgr.Data.Economy.Raw["items"] as JObject)?[itemKey] as JObject;
            if (item == null) return "-";

            if (itemKey == "healItem")
            {
                int permille = (int?)item["healPermille"] ?? 0;
                return Tint($"구매 즉시 체력 {permille / 10f:0.#}% 회복", beneficial: true);
            }

            if (!(item["options"] is JArray options) || optionIndex < 0 || optionIndex >= options.Count)
                return "-";

            if (!(options[optionIndex] is JObject option)) return "-";

            return itemKey == "conditionalBoost" ? DescribeConditional(option) : DescribeStatOption(option);
        }

        private static string DescribeStatOption(JObject option)
        {
            string statKey = (string)option["stat"];
            string stat = StatName(statKey);
            double value = (double?)option["value"] ?? 0;
            bool isMultiplier = (bool?)option["isMultiplier"] ?? false;
            string text = isMultiplier ? $"{stat} ×{1 + value:0.##}" : $"{stat} +{value:0.##}";
            return Tint(text, IsBeneficial(statKey, value));
        }

        private static string DescribeConditional(JObject option)
        {
            string statKey = (string)option["stat"];
            string stat = StatName(statKey);
            double mult = (double?)option["mult"] ?? 0;

            // ★ 조건절("HP 30% 이하일 때")은 칠하지 않는다 — 조건은 이득도 손해도 아니다.
            //   효과 쪽만 칠해야 "언제"와 "무엇이"가 눈으로 갈린다.
            string effect = Tint($"{stat} ×{1 + mult:0.##}", IsBeneficial(statKey, mult));

            switch ((string)option["condition"])
            {
                case "hp_below":
                    double hpBelow = (double?)option["value"] ?? 0;
                    return $"HP {hpBelow * 100:0}% 이하일 때 {effect}";
                case "enemies_above":
                    int count = (int?)option["value"] ?? 0;
                    return $"적이 {count}체 이상일 때 {effect}";
                case "is_last_alive":
                    return $"팀의 마지막 생존자일 때 {effect}";
                default:
                    return (string)option["condition"] ?? "-";
            }
        }

        /// <summary>
        /// 그 변화가 <b>플레이어에게 이로운가</b>. 부호만 보면 틀린다.
        /// </summary>
        /// <remarks>
        /// <c>공격 간격</c>과 <c>받는 피해</c>는 <b>낮을수록 좋은 값</b>이라 <c>+</c> 가 손해다.
        /// "값이 오르면 초록"으로 짜면 <c>받는 피해 ×1.3</c> 이 이득처럼 칠해진다 —
        /// 색이 정보를 주는 게 아니라 <b>거짓말을 하게 되는</b> 경우다.
        /// </remarks>
        private static bool IsBeneficial(string stat, double value)
        {
            bool lowerIsBetter = stat == "attackInterval" || stat == "damageTaken";
            return lowerIsBetter ? value < 0 : value > 0;
        }

        /// <summary>이득/손해에 따라 칠한다. <see cref="UITheme.Semantic"/> 이 유일한 색 출처다.</summary>
        private static string Tint(string text, bool beneficial) =>
            beneficial ? UITheme.Semantic.Gain(text) : UITheme.Semantic.Cost(text);

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
        }    }
}
