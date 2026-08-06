using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DomoNinja.Unity
{
    /// <summary>
    /// 라운드 전투 승리 직후, 상점 진입 전에 뜨는 보상 인터스티셜. 화면 아무 곳이나 터치하면 닫힌다.
    /// </summary>
    /// <remarks>
    /// 화면을 코드로 만든다 — <see cref="UIConfirmPopup"/> 과 같은 이유(`19` §5.1):
    /// 씬 파일은 손으로 병합할 수 없어 건드릴수록 비싸지고, 이 화면은 라운드마다 획득 재화 숫자만
    /// 바뀔 뿐 어느 화면에서 떠도 같은 모양이라 씬에 하나 더 둘 이유가 없다.
    /// </remarks>
    public sealed class BattleVictoryPopup : MonoBehaviour
    {
        private const int SortingOrder = UITheme.Layer.Popup;
        private const string PanelSpriteKey = "UI/Theme/nine_path_panel_2";
        /// <summary>라운드 보상은 런 전용 재화(`RunState.Currency`)다 — 메타 재화(`Meta/M-GOLD_재화`)와
        /// 다른 화폐라 섞지 않는다(`CLAUDE.md` "화폐가 둘이다"). 상점의 런 재화 아이콘과 같은 것을 쓴다.</summary>
        private const string CurrencyIconKey = "UI/RunCurrency_재화";

        private static BattleVictoryPopup _instance;

        private TMP_Text _rewardLabel;
        private Action _onContinue;

        /// <summary>팝업을 띄운다. <paramref name="onContinue"/>는 화면을 터치해 닫을 때만 불린다.</summary>
        public static void Show(int currencyGained, Action onContinue)
        {
            if (_instance == null)
            {
                var go = new GameObject("BattleVictoryPopup");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<BattleVictoryPopup>();
            }

            _instance._rewardLabel.text = $"+{currencyGained}";
            _instance._onContinue = onContinue;
            _instance.gameObject.SetActive(true);
        }

        private void Awake() => Build();

        private void Build()
        {
            UITheme.SetupFullScreenCanvas(gameObject, SortingOrder);

            // 스크림이 화면 전체를 덮고, 동시에 "아무 곳이나 터치" 버튼 역할을 한다.
            // 패널·글자는 전부 raycastTarget=false 라 그 위를 눌러도 클릭이 스크림까지 그대로 샌다.
            var scrim = NewChild("Scrim", transform);
            var scrimImage = scrim.gameObject.AddComponent<Image>();
            scrimImage.color = new Color(0f, 0f, 0f, 0.55f);
            Stretch(scrim);

            var tapButton = UITheme.EnsureButton(scrim.gameObject);
            tapButton.transition = Selectable.Transition.None; // 스크림 자체가 눌린 티를 낼 필요는 없다.
            tapButton.onClick.AddListener(Continue);

            var panel = NewChild("Panel", transform);
            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.sprite = UITheme.Find(PanelSpriteKey);
            panelImage.type = Image.Type.Sliced;
            panelImage.raycastTarget = false;
            panel.sizeDelta = new Vector2(1000f, 620f);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;

            AddLabel(panel, "TitleLabel", "전투 승리", 48f,
                     new Color(0.85f, 0.68f, 0.30f), new Vector2(0f, -60f), new Vector2(1000f, 100f));

            AddLabel(panel, "SubtitleLabel", "재화 획득", 24f,
                     new Color(0.88f, 0.88f, 0.88f), new Vector2(0f, -190f), new Vector2(1000f, 50f));

            var icon = NewChild("RewardIcon", panel);
            var iconImage = icon.gameObject.AddComponent<Image>();
            iconImage.sprite = UITheme.Find(CurrencyIconKey);
            iconImage.raycastTarget = false;
            icon.sizeDelta = new Vector2(64f, 64f);
            icon.anchorMin = icon.anchorMax = new Vector2(0f, 1f);
            icon.pivot = new Vector2(0f, 1f);
            icon.anchoredPosition = new Vector2(430f, -280f);

            _rewardLabel = AddLabel(panel, "RewardLabel", "+0", 28f,
                                     new Color(0.88f, 0.88f, 0.88f), new Vector2(510f, -292f), new Vector2(200f, 50f),
                                     TextAlignmentOptions.Left);

            AddLabel(panel, "PromptLabel", "아무 곳을 터치해 상점 진입", 20f,
                     new Color(0.65f, 0.65f, 0.65f), new Vector2(0f, -560f), new Vector2(1000f, 40f));
        }

        private static TMP_Text AddLabel(RectTransform parent, string name, string text, float fontSize,
                                          Color color, Vector2 position, Vector2 size,
                                          TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var rt = NewChild(name, parent);
            var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = align;
            label.raycastTarget = false;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = position;
            return label;
        }

        private void Continue()
        {
            var action = _onContinue;
            _onContinue = null;
            gameObject.SetActive(false);
            action?.Invoke();
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
    }
}
