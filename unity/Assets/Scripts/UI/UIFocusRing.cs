using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DomoNinja.Unity
{
    /// <summary>
    /// 지금 <b>키보드 초점이 어디에 있는지</b>를 테두리로 보여준다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>웹 빌드라서 필요하다.</b> 브라우저에서 게임을 여는 사람은 탭·방향키를 자연스럽게 누르고,
    /// 마우스를 쓰지 않는 심사자도 있다. 그런데 지금은 초점이 어디 있는지 화면에 아무 표시가 없어
    /// <b>키를 눌러도 아무 일도 안 일어나는 것처럼 보인다.</b>
    /// </para>
    /// <para>
    /// 링은 하나만 만들어 선택된 요소 위로 옮겨 다닌다. 요소마다 링을 붙이면
    /// 런타임에 만들어지는 버튼(<see cref="UITheme.EnsureButton"/>)에는 붙일 수가 없다.
    /// </para>
    /// <para>
    /// 팝업이 열리고 닫히면서 선택된 오브젝트가 <b>꺼진 채로 남을 수</b> 있어서,
    /// 활성 상태를 매 프레임 확인한다 — 링만 남아 떠 있는 것이 초점이 없는 것보다 나쁘다.
    /// </para>
    /// </remarks>
    public sealed class UIFocusRing : MonoBehaviour
    {
        private const string RingSpriteKey = "UI/Theme/nine_path_focus";

        /// <summary>선택된 요소보다 이만큼 바깥으로 나온다.</summary>
        private const float Padding = 6f;

        private static UIFocusRing _instance;

        private Image _ring;
        private RectTransform _ringRect;
        private GameObject _current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("UIFocusRing");
            _instance = go.AddComponent<UIFocusRing>();
        }

        private void Awake()
        {
            var sprite = UITheme.Find(RingSpriteKey);

            var ringGo = new GameObject("Ring", typeof(RectTransform), typeof(Image));
            _ringRect = (RectTransform)ringGo.transform;
            _ring = ringGo.GetComponent<Image>();
            _ring.sprite = sprite;
            _ring.type = Image.Type.Sliced;
            _ring.pixelsPerUnitMultiplier = 1f;
            _ring.raycastTarget = false;
            _ring.enabled = sprite != null;

            ringGo.SetActive(false);
        }

        private void LateUpdate()
        {
            var system = EventSystem.current;
            if (system == null) return;

            var selected = system.currentSelectedGameObject;

            if (selected != null && (!selected.activeInHierarchy || !IsRingable(selected.GetComponent<Selectable>())))
                selected = null;

            // ★ 위에 새 캔버스가 뜨면 초점도 따라 올라가야 한다.
            //   전에는 "선택이 비었을 때"만 다시 골라서, 팝업이 떠도 초점은 **뒤 화면에 남아 있었다** —
            //   눈에는 팝업이 떠 있는데 방향키는 아래를 돌아다닌다. 마우스 쪽에서 같은 문제를
            //   `b98448d` 가 고쳤고, 타이틀 → 튜토리얼 흐름에서 실제로 재현됐다.
            if (selected != null && OrderOf(selected.transform) < TopOrder())
                selected = null;

            // ★ 아무것도 선택돼 있지 않으면 방향키가 **아무 일도 하지 않는다.**
            //   입력 모듈은 현재 선택이 있을 때만 다음 대상을 찾기 때문이다.
            //   그래서 화면이 바뀌면 그 화면의 첫 버튼을 잡아준다 — 링이 같이 보이므로
            //   "이 화면의 기본 동작은 여기"라는 표시도 겸한다.
            if (selected == null)
            {
                var first = FindFirstSelectable();
                if (first != null)
                {
                    system.SetSelectedGameObject(first.gameObject);
                    selected = first.gameObject;
                }
            }

            if (selected != _current)
            {
                _current = selected;
                Attach(selected);
            }

            if (_current != null) Fit((RectTransform)_current.transform);
        }

        /// <summary>
        /// <b>가장 위에 있는 캔버스</b>에서 처음 만나는 조작 대상.
        /// </summary>
        /// <remarks>
        /// ★ 처음에는 계층 순서만 보고 골랐다. 그러면 팝업이 열려 있어도
        /// <b>그 아래 깔린 화면의 버튼에 초점이 잡힌다</b> — 눈에는 팝업이 떠 있는데
        /// 방향키는 뒤 화면을 돌아다닌다. 마우스 쪽에서 같은 문제를 <c>b98448d</c> 가 고쳤고
        /// (팝업이 보이는 것과 클릭을 먼저 받는 것은 별개), 키보드 쪽도 같은 규칙을 따라야 한다.
        /// <para>
        /// <see cref="UIScreenManager.ShowPopup"/> 이 팝업을 열 때마다 sortingOrder 를 올려 발급하므로,
        /// <b>가장 큰 sortingOrder 가 곧 지금 사용자가 보고 있는 화면</b>이다.
        /// </para>
        /// </remarks>
        private static Selectable FindFirstSelectable()
        {
            Selectable best = null;
            int bestOrder = int.MinValue;

            foreach (var s in Object.FindObjectsByType<Selectable>(FindObjectsInactive.Exclude))
            {
                if (!IsRingable(s)) continue;

                int order = OrderOf(s.transform);

                if (order > bestOrder ||
                    (order == bestOrder && best != null &&
                     s.transform.GetSiblingIndex() < best.transform.GetSiblingIndex()))
                {
                    best = s;
                    bestOrder = order;
                }
            }
            return best;
        }

        /// <summary>
        /// 링을 두를 만한 대상인가.
        /// </summary>
        /// <remarks>
        /// ★ <b>화면 전체가 하나의 버튼</b>인 경우가 있다 — 타이틀의 "탭하여 시작" 처럼.
        /// 거기에 링을 두르면 <b>화면 테두리에 주황 액자가 생긴다.</b> 실제로 그렇게 나왔다.
        /// 두 가지로 거른다 —
        /// <c>transition == None</c> 은 <b>보이는 버튼이 아니라는 선언</b>이고(전면 탭 대상이 그렇게 만들어져 있다),
        /// 캔버스를 거의 다 덮는 것도 초점을 표시할 대상이 아니다.
        /// </remarks>
        private static bool IsRingable(Selectable s)
        {
            if (s == null || !s.IsInteractable() || s.navigation.mode == Navigation.Mode.None) return false;
            if (s.transition == Selectable.Transition.None) return false;

            var rt = s.transform as RectTransform;
            var canvas = s.GetComponentInParent<Canvas>();
            if (rt == null || canvas == null) return true;

            var canvasSize = ((RectTransform)canvas.rootCanvas.transform).rect.size;
            return rt.rect.width < canvasSize.x * 0.9f || rt.rect.height < canvasSize.y * 0.9f;
        }

        private static int OrderOf(Transform t)
        {
            var canvas = t.GetComponentInParent<Canvas>();
            return canvas != null ? canvas.rootCanvas.sortingOrder : 0;
        }

        /// <summary>지금 켜져 있는 캔버스 중 가장 위. 링을 둘 대상이 있는 것만 센다.</summary>
        private static int TopOrder()
        {
            int top = int.MinValue;
            foreach (var s in Object.FindObjectsByType<Selectable>(FindObjectsInactive.Exclude))
            {
                if (!IsRingable(s)) continue;
                int order = OrderOf(s.transform);
                if (order > top) top = order;
            }
            return top;
        }

        private void Attach(GameObject target)
        {
            if (target == null || _ringRect == null)
            {
                if (_ringRect != null) _ringRect.gameObject.SetActive(false);
                return;
            }

            // 같은 부모 밑 맨 뒤에 두어 대상 위에 그려지게 한다.
            _ringRect.SetParent(target.transform.parent, false);
            _ringRect.SetAsLastSibling();
            _ringRect.gameObject.SetActive(true);
        }

        /// <summary>
        /// 대상 <b>사각형의 한가운데</b>에 링을 맞춘다.
        /// </summary>
        /// <remarks>
        /// ★ 처음에는 대상의 앵커·피벗·위치를 그대로 베끼고 <c>sizeDelta</c> 만 키웠다.
        /// 그러면 <b>피벗이 가운데가 아닌 요소에서 링이 한쪽으로 쏠린다</b> —
        /// 크기는 피벗 반대쪽으로만 자라기 때문이다. 이 프로젝트의 UI 는 대부분 피벗이
        /// 좌상단(0,1)이라 링이 오른쪽·아래로 여백만큼 밀려 있었다.
        /// <para>
        /// 그래서 피벗을 베끼지 않고 <b>가운데를 직접 계산해</b> 놓는다.
        /// <c>rect.center</c> 는 피벗 기준 오프셋이므로 <c>localPosition</c> 에 더하면
        /// 부모 좌표계에서의 실제 중심이 나온다 — 앵커가 늘어난(stretch) 요소에도 그대로 성립한다.
        /// </para>
        /// </remarks>
        private void Fit(RectTransform target)
        {
            var parent = target.parent as RectTransform;
            if (parent == null) return;

            _ringRect.anchorMin = _ringRect.anchorMax = _ringRect.pivot = new Vector2(0.5f, 0.5f);
            _ringRect.localScale = target.localScale;
            _ringRect.sizeDelta = target.rect.size + new Vector2(Padding * 2f, Padding * 2f);

            Vector2 centerInParent = (Vector2)target.localPosition + target.rect.center;
            _ringRect.anchoredPosition = centerInParent - parent.rect.center;
        }
    }
}
