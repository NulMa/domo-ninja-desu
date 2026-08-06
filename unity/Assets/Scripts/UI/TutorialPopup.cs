using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DomoNinja.Unity
{
    /// <summary>
    /// 핵심 3가지(용병 선택·배치·상점 배타 선택)를 카드 3장으로 넘겨보는 정적 튜토리얼 팝업.
    /// </summary>
    /// <remarks>
    /// 화면을 코드로 만든다 — <see cref="UIConfirmPopup"/>·<see cref="BattleVictoryPopup"/>와 같은 이유
    /// (`19` §5.1): 특정 화면에 속하지 않고 아무 데서나(타이틀 첫 진입·StageSelect 도움말 버튼) 띄우는
    /// 팝업이라 씬에 자리를 정해 둘 필요가 없다.
    /// </remarks>
    public sealed class TutorialPopup : MonoBehaviour
    {
        private const string PanelSpriteKey = "UI/Theme/nine_path_panel_2";

        private static readonly (string Title, string Body)[] Pages =
        {
            ("① 용병 선택", "로스터 중 3명을 골라 이번 런에 데려갑니다."),
            ("② 배치", "보드 위 아군을 드래그하거나, 클릭으로 선택한 뒤 다른 칸을 클릭해\n자리를 바꿀 수 있습니다."),
            ("③ 상점", "하나를 사는 순간 같이 떴던 다른 선택지는 사라집니다 — 신중하게 고르세요."),
        };

        private static TutorialPopup _instance;

        private int _pageIndex;
        private TMP_Text _titleLabel;
        private TMP_Text _bodyLabel;
        private TMP_Text _pageLabel;
        private TMP_Text _nextLabel;
        private Button _prevButton;

        public static void Show()
        {
            if (_instance == null)
            {
                var go = new GameObject("TutorialPopup");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<TutorialPopup>();
            }

            _instance._pageIndex = 0;
            _instance.Refresh();
            _instance.gameObject.SetActive(true);
        }

        private void Awake() => Build();

        private void Build()
        {
            UITheme.SetupFullScreenCanvas(gameObject, UITheme.Layer.Tutorial);

            // 스크림은 뒤 클릭만 막는다 — 여기서는 아무 곳 터치로 안 닫는다.
            // 카드를 넘겨보는 도중 실수로 배경을 눌러 통째로 닫히면 안 되기 때문이다.
            var scrim = NewChild("Scrim", transform);
            scrim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            Stretch(scrim);

            var panel = NewChild("Panel", transform);
            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.sprite = UITheme.Find(PanelSpriteKey);
            panelImage.type = Image.Type.Sliced;
            panelImage.raycastTarget = false;
            panel.sizeDelta = new Vector2(1100f, 680f);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;

            var closeRt = NewChild("CloseButton", panel);
            var closeImage = closeRt.gameObject.AddComponent<Image>();
            closeImage.sprite = UITheme.Find(UITheme.ButtonNormalKey);
            closeImage.type = Image.Type.Sliced;
            closeRt.anchorMin = closeRt.anchorMax = new Vector2(1f, 1f);
            closeRt.pivot = new Vector2(1f, 1f);
            closeRt.sizeDelta = new Vector2(72f, 72f);
            closeRt.anchoredPosition = new Vector2(-30f, -30f);
            var closeButton = UITheme.EnsureButton(closeRt.gameObject);
            closeButton.onClick.AddListener(Close);
            AddLabel(closeRt, "Label", "X", 26f, Color.white, Vector2.zero, closeRt.sizeDelta);

            _titleLabel = AddLabel(panel, "TitleLabel", "", 36f,
                                    new Color(0.85f, 0.68f, 0.30f), new Vector2(0f, -70f), new Vector2(1100f, 60f));

            _bodyLabel = AddLabel(panel, "BodyLabel", "", 24f,
                                   new Color(0.88f, 0.88f, 0.88f), new Vector2(60f, -200f), new Vector2(980f, 220f));
            _bodyLabel.textWrappingMode = TextWrappingModes.Normal;

            _pageLabel = AddLabel(panel, "PageLabel", "", 20f,
                                   new Color(0.6f, 0.6f, 0.6f), new Vector2(0f, -560f), new Vector2(1100f, 40f));

            MakeNavButton(panel, "PrevButton", "이전", new Vector2(-280f, 60f), Prev, out _prevButton);
            _nextLabel = MakeNavButton(panel, "NextButton", "다음", new Vector2(280f, 60f), Next, out _);
        }

        private void Refresh()
        {
            var page = Pages[_pageIndex];
            _titleLabel.text = page.Title;
            _bodyLabel.text = page.Body;
            _pageLabel.text = $"{_pageIndex + 1} / {Pages.Length}";
            _prevButton.interactable = _pageIndex > 0;
            _nextLabel.text = _pageIndex == Pages.Length - 1 ? "확인" : "다음";
        }

        private void Prev()
        {
            if (_pageIndex > 0) { _pageIndex--; Refresh(); }
        }

        private void Next()
        {
            if (_pageIndex < Pages.Length - 1) { _pageIndex++; Refresh(); }
            else Close();
        }

        private void Close() => gameObject.SetActive(false);

        private TMP_Text MakeNavButton(RectTransform parent, string name, string text, Vector2 position,
                                        System.Action onClick, out Button button)
        {
            var rt = NewChild(name, parent);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(260f, 88f);
            rt.anchoredPosition = position;

            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = UITheme.Find(UITheme.ButtonNormalKey);
            image.type = Image.Type.Sliced;

            button = UITheme.EnsureButton(rt.gameObject);
            button.onClick.AddListener(() => onClick());

            return AddLabel(rt, "Label", text, 26f, Color.white, Vector2.zero, rt.sizeDelta);
        }

        private static TMP_Text AddLabel(RectTransform parent, string name, string text, float fontSize,
                                          Color color, Vector2 position, Vector2 size)
        {
            var rt = NewChild(name, parent);
            var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = position;
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
    }
}
