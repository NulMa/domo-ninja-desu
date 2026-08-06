using System;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine;
using UnityEngine.UI;

namespace DomoNinja.Unity
{
    /// <summary>
    /// <b>되돌릴 수 없는 동작</b> 앞에 한 번 묻는다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 처음 필요해진 곳은 퇴각이다 — 누르는 순간 진행 중인 런(로스터·배치·상점 구매)이
    /// 확인 없이 사라졌다. 다른 팝업 버튼과 <b>같은 톤으로 놓여 있는데 혼자만 파괴적</b>이었다.
    /// </para>
    /// <para>
    /// ★ 화면을 코드로 만든다. 씬에 팝업을 하나 더 늘리지 않는 이유는,
    /// 씬 파일이 손으로 병합할 수 없는 크기라 <b>2인이 같은 씬을 건드릴수록 비싸지기</b> 때문이다(`19` §5.1).
    /// 확인창은 화면마다 다르게 생길 이유가 없어서 코드 한 벌이 오히려 맞다.
    /// </para>
    /// <para>
    /// 팩의 <c>YesButton</c>/<c>NoButton</c> 은 쓰지 않았다 — <b>영문이 그림에 박혀 있다.</b>
    /// 나머지 UI 가 전부 한글이라 거기만 "Yes/No" 가 되고, 글자는 그림이 아니라 폰트가 그려야 한다.
    /// </para>
    /// </remarks>
    public sealed class UIConfirmPopup : MonoBehaviour
    {
        private const int SortingOrder = 8000;

        private static UIConfirmPopup _instance;

        private TMP_Text _message;
        private TMP_Text _confirmLabel;
        private Button _cancelButton;
        private Action _onConfirm;

        /// <summary>확인창을 띄운다. <paramref name="onConfirm"/> 은 사용자가 승인했을 때만 불린다.</summary>
        public static void Show(string message, string confirmText, Action onConfirm)
        {
            if (_instance == null)
            {
                var go = new GameObject("UIConfirmPopup");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<UIConfirmPopup>();
            }

            _instance._message.text = message;
            _instance._confirmLabel.text = confirmText;
            _instance._onConfirm = onConfirm;
            _instance.gameObject.SetActive(true);

            // ★ 초점은 **취소**에 둔다.
            //   `UIFocusRing` 이 "화면의 첫 조작 대상"을 자동으로 잡는데, 그걸 그대로 두면
            //   확인창이 뜨자마자 초점이 실행 버튼에 걸린다 — **엔터 한 번에 런이 날아간다.**
            //   위험한 쪽이 기본값이 되면 확인창을 세운 의미가 없다.
            if (EventSystem.current != null && _instance._cancelButton != null)
                EventSystem.current.SetSelectedGameObject(_instance._cancelButton.gameObject);
        }

        private void Awake() => Build();

        private void Build()
        {
            UITheme.SetupFullScreenCanvas(gameObject, SortingOrder);

            // 뒤를 덮는다. 덮지 않으면 뒤 버튼이 계속 눌려서 "물어보는 중"이 아니게 된다.
            var scrim = NewChild("Scrim", transform);
            var scrimImage = scrim.gameObject.AddComponent<Image>();
            scrimImage.color = new Color(0f, 0f, 0f, 0.55f);
            Stretch(scrim);

            var panel = NewChild("Panel", transform);
            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.sprite = UITheme.Find("UI/Theme/nine_path_panel_2");
            panelImage.type = Image.Type.Sliced;
            panelImage.pixelsPerUnitMultiplier = 0.75f;
            panel.sizeDelta = new Vector2(760f, 340f);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;

            var msg = NewChild("Message", panel);
            _message = msg.gameObject.AddComponent<TextMeshProUGUI>();
            _message.fontSize = 30f;
            _message.alignment = TextAlignmentOptions.Center;
            _message.color = Color.white;
            _message.raycastTarget = false;
            msg.anchorMin = new Vector2(0f, 1f);
            msg.anchorMax = new Vector2(1f, 1f);
            msg.pivot = new Vector2(0.5f, 1f);
            msg.offsetMin = new Vector2(48f, 0f);
            msg.offsetMax = new Vector2(-48f, 0f);
            msg.sizeDelta = new Vector2(msg.sizeDelta.x, 150f);
            msg.anchoredPosition = new Vector2(0f, -56f);

            // 취소가 왼쪽, 실행이 오른쪽. 진행 동작을 오른쪽 아래에 두는 규칙과 같다(`19` §6.6b).
            MakeButton(panel, "CancelButton", "취소", new Vector2(-150f, 56f), Cancel, secondary: true, out _cancelButton);
            _confirmLabel = MakeButton(panel, "ConfirmButton", "확인", new Vector2(150f, 56f), Confirm, secondary: false, out _);
        }

        private TMP_Text MakeButton(RectTransform parent, string name, string text,
                                    Vector2 position, Action onClick, bool secondary, out Button button)
        {
            var rt = NewChild(name, parent);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(260f, 88f);
            rt.anchoredPosition = position;

            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = UITheme.Find(UITheme.ButtonNormalKey);
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;
            image.color = secondary ? new Color(0.78f, 0.76f, 0.72f) : Color.white;

            button = UITheme.EnsureButton(rt.gameObject);
            button.onClick.AddListener(() => onClick());

            var labelRt = NewChild("Label", rt);
            var label = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 26f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            Stretch(labelRt);
            return label;
        }

        private void Confirm()
        {
            var action = _onConfirm;
            _onConfirm = null;
            gameObject.SetActive(false);
            action?.Invoke();
        }

        private void Cancel()
        {
            _onConfirm = null;
            gameObject.SetActive(false);
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
