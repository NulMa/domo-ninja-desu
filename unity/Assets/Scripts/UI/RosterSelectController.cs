using System.Collections.Generic;
using System.Text;
using DomoNinja.Core.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UImage = UnityEngine.UI.Image;
using DomoNinja.Unity.View;

namespace DomoNinja.Unity
{
    /// <summary>용병 선택 화면. 최대 3명 토글 선택 → 트레이/정보패널 갱신 → 전투 진입.</summary>
    public sealed class RosterSelectController : MonoBehaviour
    {
        private const int DeployCount = 3;
        private const string PortraitPrefix = "Portrait_";

        // ★ 이 값들은 이제 **틴트**다. 예전엔 스프라이트가 흰 사각형이라 색이 곧 칠이었지만,
        //    지금은 그림이 깔려 있어서 색을 그대로 곱하면 그림이 사라진다.
        //    그래서 기본이 흰색(=원본 그대로)이고, 상태 구분은 밝기·색조를 살짝 얹어서 낸다.
        private static readonly Color SelectedColor = new Color(1.00f, 0.82f, 0.45f);
        private static readonly Color UnselectedColor = Color.white;
        private static readonly Color TrayFilledColor = Color.white;
        private static readonly Color TrayEmptyColor = new Color(0.55f, 0.55f, 0.55f);

        private Transform _portraitGrid;
        private Transform _confirmedTray;
        private Button _enterBattleButton;
        private TMP_Text _infoLabel;
        private TMP_Text _skillTitleLabel;
        private readonly UImage[] _skillIcons = new UImage[2];
        private readonly TMP_Text[] _skillNameLabels = new TMP_Text[2];
        /// <summary>스킬 카드마다 캐릭터가 그 스킬을 두고 하는 한마디. `SkillDef.Flavor` — 없는 스킬도 있다(보조 18개).</summary>
        private readonly TMP_Text[] _skillFlavorLabels = new TMP_Text[2];
        private SpriteCatalog _catalog;

        private readonly List<string> _selected = new List<string>();

        private void Awake()
        {
            var board = transform.Find("Board");
            _portraitGrid = board.Find("PortraitGrid");
            _confirmedTray = board.Find("ConfirmedTray");
            _enterBattleButton = UITheme.EnsureButton(board.Find("EnterBattleButton").gameObject);
            var infoPanel = board.Find("InfoPanel");
            _infoLabel = infoPanel.Find("Label").GetComponent<TMP_Text>();
            _skillTitleLabel = infoPanel.Find("SkillTitleLabel").GetComponent<TMP_Text>();
            for (int i = 0; i < 2; i++)
            {
                var card = (RectTransform)infoPanel.Find("SkillCard" + (i + 1));
                _skillIcons[i] = card.Find("Icon").GetComponent<UImage>();
                _skillNameLabels[i] = card.Find("NameLabel").GetComponent<TMP_Text>();
                _skillFlavorLabels[i] = BuildFlavorLabel(card);
            }

            _catalog = Resources.Load<SpriteCatalog>(SpriteCatalog.ResourceName);

            foreach (Transform cell in _portraitGrid)
            {
                if (!cell.name.StartsWith(PortraitPrefix)) continue;
                string characterId = cell.name.Substring(PortraitPrefix.Length);

                var btn = UITheme.EnsureButton(cell.gameObject);
                btn.onClick.AddListener(() => OnPortraitClicked(characterId));
            }

            _enterBattleButton.onClick.AddListener(OnEnterBattle);

            RefreshUI();
        }

        private void OnPortraitClicked(string characterId)
        {
            if (_selected.Contains(characterId))
                _selected.Remove(characterId);
            else if (_selected.Count < DeployCount)
                _selected.Add(characterId);

            RefreshUI();
        }

        private void RefreshUI()
        {
            foreach (Transform cell in _portraitGrid)
            {
                if (!cell.name.StartsWith(PortraitPrefix)) continue;
                string characterId = cell.name.Substring(PortraitPrefix.Length);
                var img = cell.GetComponent<UImage>();
                if (img != null) img.color = _selected.Contains(characterId) ? SelectedColor : UnselectedColor;
            }

            for (int i = 0; i < _confirmedTray.childCount; i++)
                RefreshTraySlot(_confirmedTray.GetChild(i), i < _selected.Count ? _selected[i] : null);

            RefreshInfoLabel();

            _enterBattleButton.interactable = _selected.Count == DeployCount;
        }

        /// <summary>
        /// 출전 트레이 한 칸. <b>칸을 눌러도 선택이 풀린다.</b>
        /// </summary>
        /// <remarks>
        /// 전에는 아래 로스터 격자에서만 토글이 됐다. 그런데 <b>방금 올린 초상화는 트레이에 있고</b>
        /// 격자에 있는 같은 얼굴은 선택 표시만 바뀌어 있어서, "빼려면 원래 있던 자리를 다시 찾아야"
        /// 했다 — 올린 곳과 내리는 곳이 다르면 되돌리는 조작이 한 박자 늦는다.
        /// </remarks>
        private void RefreshTraySlot(Transform slot, string characterId)
        {
            var slotImg = slot.GetComponent<UImage>();
            var face = slot.Find("Face");

            // ★ 리스너를 매번 지우고 다시 건다. 칸과 캐릭터의 짝은 선택할 때마다 바뀌므로
            //   (2번을 빼면 3번이 2번 칸으로 당겨온다) 한 번 걸어두면 엉뚱한 얼굴이 빠진다.
            var slotButton = UITheme.EnsureButton(slot.gameObject);
            slotButton.onClick.RemoveAllListeners();

            if (characterId == null)
            {
                if (slotImg != null) slotImg.color = TrayEmptyColor;
                if (face != null) face.gameObject.SetActive(false);
                slotButton.interactable = false;
                return;
            }

            slotButton.interactable = true;
            slotButton.onClick.AddListener(() => OnPortraitClicked(characterId));

            if (slotImg != null) slotImg.color = TrayFilledColor;

            if (face == null)
            {
                var faceGo = new GameObject("Face", typeof(RectTransform));
                faceGo.transform.SetParent(slot, false);
                var rt = faceGo.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(4, 4);
                rt.offsetMax = new Vector2(-4, -4);
                faceGo.AddComponent<UImage>().preserveAspect = true;
                face = faceGo.transform;
            }

            face.gameObject.SetActive(true);
            face.GetComponent<UImage>().sprite = FindSprite(characterId);
        }

        private void RefreshInfoLabel()
        {
            if (_selected.Count == 0)
            {
                _infoLabel.text = "용병을 선택하면 정보가 표시됩니다";
                _skillTitleLabel.text = "";
                SetSkillCard(0, null);
                SetSkillCard(1, null);
                return;
            }

            var mgr = RunManager.Instance;
            var def = mgr != null && mgr.Data != null ? mgr.Data.FindCharacter(_selected[_selected.Count - 1]) : null;
            if (def == null)
            {
                _infoLabel.text = "정보를 찾지 못했다";
            }
            else
            {
                // 플레이버(성격·배경)가 있으면 이름 바로 아래, 없으면 그 줄만 빠진다 — 필수 필드가 아니다.
                string flavorLine = string.IsNullOrEmpty(def.Flavor) ? "" : $"{def.Flavor}\n";
                _infoLabel.text = $"{def.Name}\n{flavorLine}\n공격력 {def.Attack}   체력 {def.Hp}   공격간격 {def.AttackInterval}";
            }

            RefreshSkillCards(mgr, def);
        }

        /// <summary>
        /// 액티브 스킬은 이 화면에서 아직 "확정된 하나"가 아니다 — 상점에서 하나를 사는 순간
        /// 나머지가 배타되는 2택 후보라(`08` §2.2), 로스터 선택 단계에선 후보 둘 다 보여준다.
        /// </summary>
        private void RefreshSkillCards(RunManager mgr, CharacterDef def)
        {
            if (def == null || mgr == null || mgr.Data == null)
            {
                _skillTitleLabel.text = "";
                SetSkillCard(0, null);
                SetSkillCard(1, null);
                return;
            }

            _skillTitleLabel.text = "액티브 스킬 (상점에서 2택)";
            for (int i = 0; i < 2; i++)
            {
                var skill = i < def.SkillIds.Count ? mgr.Data.FindSkill(def.SkillIds[i]) : null;
                SetSkillCard(i, skill);
            }
        }

        private void SetSkillCard(int index, SkillDef skill)
        {
            var icon = _skillIcons[index];
            var label = _skillNameLabels[index];
            var flavorLabel = _skillFlavorLabels[index];

            icon.sprite = skill?.Icon != null ? _catalog?.Find(skill.Icon) : null;
            icon.enabled = icon.sprite != null;
            label.text = skill != null ? skill.Name : "-";
            flavorLabel.text = DescribeSkillCard(skill);
        }

        /// <summary>
        /// 카드 본문 — <b>이득/대가가 먼저, 한마디는 그 아래.</b>
        /// </summary>
        /// <remarks>
        /// 전엔 한마디(<c>Flavor</c>)만 있었는데 <b>보조 18개는 값이 없어서 카드가 통째로 비었다.</b>
        /// 고를 근거가 되는 건 어차피 수치 쪽이라, 항상 있는 것을 위에 두고 없을 수 있는 것을 아래로 뺀다.
        /// 색 규칙은 <see cref="InfoPopupController.FormatSkill"/> 과 같은 출처를 쓴다.
        /// </remarks>
        private static string DescribeSkillCard(SkillDef skill)
        {
            if (skill == null) return "";

            var sb = new StringBuilder();
            if (skill.TextGain != null) sb.Append(UITheme.Semantic.Gain(skill.TextGain));
            if (skill.TextCost != null)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(UITheme.Semantic.Cost(skill.TextCost));
            }
            if (!string.IsNullOrEmpty(skill.Flavor))
            {
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append("<i>“").Append(skill.Flavor).Append("”</i>");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 스킬 카드의 <c>NameLabel</c> 아래에 캐릭터가 그 스킬을 두고 하는 한마디를 둘 자리를 만든다.
        /// 씬에 없던 조각이라 <c>ShopController.BuildIllustrationZone</c>과 같은 방식으로 코드로 채운다
        /// (`19` §5.1 — 씬 파일은 손으로 병합이 안 된다).
        /// </summary>
        private static TMP_Text BuildFlavorLabel(RectTransform card)
        {
            var rt = NewChild("FlavorLabel", card);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(4f, -300f);
            rt.offsetMax = new Vector2(-4f, -168f); // NameLabel(y=-108, h=60) 바로 아래부터 카드 하단까지

            var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
            label.fontSize = 18f;
            label.alignment = TextAlignmentOptions.Top;
            label.color = new Color(0.86f, 0.84f, 0.80f);
            // ★ 라벨 전체를 이탤릭으로 두지 않는다. 이제 본문이 수치(이득/대가)고 한마디는 아래
            //   한 덩어리라, 기울일 곳은 그 한 덩어리뿐이다 — <i> 로 그 자리만 감싼다.
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            return label;
        }

        private static RectTransform NewChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private Sprite FindSprite(string characterId)
        {
            var mgr = RunManager.Instance;
            if (mgr == null || mgr.Data == null || _catalog == null) return null;
            var def = mgr.Data.FindCharacter(characterId);
            return def != null ? _catalog.Find(def.Sprite) : null;
        }

        private void OnEnterBattle()
        {
            var mgr = RunManager.Instance;
            if (mgr == null || _selected.Count != DeployCount) return;

            mgr.StartNewRun(mgr.SelectedStageId, _selected);
            UIScreenManager.ShowScreen("GamePlay");
            UIScreenManager.ShowPopup("StageIntro");
        }    }
}
