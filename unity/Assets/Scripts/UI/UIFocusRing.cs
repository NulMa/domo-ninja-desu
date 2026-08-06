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

            if (selected != null && (!selected.activeInHierarchy || selected.GetComponent<Selectable>() == null))
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

        /// <summary>켜져 있는 화면에서 처음 만나는 조작 대상.</summary>
        private static Selectable FindFirstSelectable()
        {
            Selectable best = null;
            foreach (var s in Object.FindObjectsByType<Selectable>(FindObjectsInactive.Exclude))
            {
                if (!s.IsInteractable() || s.navigation.mode == Navigation.Mode.None) continue;
                if (best == null || s.transform.GetSiblingIndex() < best.transform.GetSiblingIndex()) best = s;
            }
            return best;
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

        private void Fit(RectTransform target)
        {
            _ringRect.anchorMin = target.anchorMin;
            _ringRect.anchorMax = target.anchorMax;
            _ringRect.pivot = target.pivot;
            _ringRect.anchoredPosition = target.anchoredPosition;
            _ringRect.sizeDelta = target.sizeDelta + new Vector2(Padding * 2f, Padding * 2f);
            _ringRect.localScale = target.localScale;
        }
    }
}
