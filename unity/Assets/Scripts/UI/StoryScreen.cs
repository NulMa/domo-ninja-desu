using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DomoNinja.Unity
{
    /// <summary>
    /// 타이틀 다음, 이 기기에서 처음 켠 경우에만 한 번 보여주는 오프닝 스토리.
    /// 탭하면 다음 문단으로, 다 보면(또는 건너뛰면) <paramref name="onDone"/>을 부른다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Pages"/> 는 `튜토리얼 스토리.txt` 원문 그대로다 — 문서의 한 줄이 곧 한 번의
    /// 탭에 나오는 대사 한 문단이라는 지시를 그대로 배열 한 항목에 매핑했다.
    /// </para>
    /// <para>
    /// <see cref="TutorialPopup"/>과 달리 이전 버튼이 없다 — 서사는 앞으로만 읽는다.
    /// 대신 건너뛰기 버튼을 따로 둔다.
    /// </para>
    /// </remarks>
    public sealed class StoryScreen : MonoBehaviour
    {
        private static readonly string[] Pages =
        {
            "왔어? 단장",
            "바쁜데 갑자기 불러서 미안해",
            "최근 갑자기 주변에 몬스터들이 늘어나서 주민들이 불안을 호소하고 있어",
            "이 사태의 원인은 마을 외각에 생성된 '다크존'에서 벌어지는 것 같아.",
            "그래서 이 사태를 해결해주기를 원해.",
            "내가 해결하면 되는 거 아니냐고?",
            "......",
            "그... 지난번에 내가 실험을 하다가 실수로...",
            "미안해! 제발 신고만은 하지 말아줘... 나 곧 신관취임식이란 말이야...",
            "취임식전에 움직이면 어떻게 되는지 너도 잘 알잖아...",
            "대신 도와주면 취임하고 확실하게 보상할게...",
            "도와준다고? 정말 고마워!",
            "'다크존' 까지 최단거리를 지도에 표시해 뒀으니 그대로 따라가면 되.",
            "그럼 부탁할게!",
        };

        private static StoryScreen _instance;

        private int _pageIndex;
        private TMP_Text _bodyLabel;
        private TMP_Text _pageLabel;
        private System.Action _onDone;

        /// <summary>스토리를 띄운다. <paramref name="onDone"/>은 다 보거나 건너뛰면 불린다.</summary>
        public static void Show(System.Action onDone)
        {
            if (_instance == null)
            {
                var go = new GameObject("StoryScreen");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<StoryScreen>();
            }

            _instance._pageIndex = 0;
            _instance._onDone = onDone;
            _instance.Refresh();
            _instance.gameObject.SetActive(true);
        }

        private void Awake() => Build();

        private void Build()
        {
            UITheme.SetupFullScreenCanvas(gameObject, UITheme.Layer.Story);

            // 배경 전체가 곧 "탭하여 계속" 버튼이다 — 서사는 화면 아무 곳이나 눌러 넘긴다.
            var bg = NewChild("Background", transform);
            bg.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.03f, 0.04f, 1f);
            Stretch(bg);

            var tapButton = UITheme.EnsureButton(bg.gameObject);
            tapButton.transition = Selectable.Transition.None;
            tapButton.onClick.AddListener(Advance);

            _bodyLabel = AddLabel(bg, "BodyLabel", "", 32f, new Color(0.92f, 0.92f, 0.92f),
                                   new Vector2(160f, -420f), new Vector2(1600f, 240f));
            _bodyLabel.textWrappingMode = TextWrappingModes.Normal;

            _pageLabel = AddLabel(bg, "PageLabel", "", 20f, new Color(0.55f, 0.55f, 0.55f),
                                   new Vector2(0f, -960f), new Vector2(1920f, 40f));

            AddLabel(bg, "PromptLabel", "탭하여 계속", 20f, new Color(0.5f, 0.5f, 0.5f),
                     new Vector2(0f, -1000f), new Vector2(1920f, 40f));

            var skipRt = NewChild("SkipButton", bg);
            skipRt.anchorMin = skipRt.anchorMax = new Vector2(1f, 1f);
            skipRt.pivot = new Vector2(1f, 1f);
            skipRt.sizeDelta = new Vector2(180f, 64f);
            skipRt.anchoredPosition = new Vector2(-40f, -40f);
            var skipImage = skipRt.gameObject.AddComponent<Image>();
            skipImage.sprite = UITheme.Find(UITheme.ButtonNormalKey);
            skipImage.type = Image.Type.Sliced;
            var skipButton = UITheme.EnsureButton(skipRt.gameObject);
            skipButton.onClick.AddListener(Finish);
            AddLabel(skipRt, "Label", "건너뛰기", 22f, Color.white, Vector2.zero, skipRt.sizeDelta);
        }

        private void Refresh()
        {
            _bodyLabel.text = Pages[_pageIndex];
            _pageLabel.text = $"{_pageIndex + 1} / {Pages.Length}";
        }

        private void Advance()
        {
            if (_pageIndex < Pages.Length - 1) { _pageIndex++; Refresh(); }
            else Finish();
        }

        private void Finish()
        {
            gameObject.SetActive(false);
            var callback = _onDone;
            _onDone = null;
            callback?.Invoke();
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
