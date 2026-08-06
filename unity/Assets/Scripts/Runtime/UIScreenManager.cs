using System.Collections.Generic;
using UnityEngine;

namespace DomoNinja.Unity
{
    /// <summary>
    /// 화면 전환 공용 로직. <see cref="UIScreen.Key"/> 로 화면/팝업을 켜고 끈다.
    /// </summary>
    /// <remarks>
    /// 캔버스 루트(각 <c>UI_Canvas_*</c>)는 항상 켜둔다. 그 밑의 개별 화면(<see cref="UIScreen"/>)만
    /// 켜고 끄면, 캔버스가 자식 하나짜리든 여러 개짜리든(<c>UI_Canvas</c> 는 3개) 규칙이 하나로 통일된다.
    /// </remarks>
    public static class UIScreenManager
    {
        /// <summary>게임을 켰을 때 처음 뜨는 풀스크린.</summary>
        /// <remarks>
        /// 에디터의 화면 미리보기 도구가 "기준 상태"로 되돌릴 때도 이 값을 쓴다.
        /// 문자열을 양쪽에 각각 적으면 첫 화면을 바꿨을 때 한쪽만 따라오고,
        /// 그 어긋남은 <b>에디터에서 보던 화면과 실제 첫 화면이 다르다</b>는 형태로만 드러난다.
        /// </remarks>
        public const string FirstScreenKey = "StageSelect";

        private static readonly Dictionary<string, UIScreen> Screens = new Dictionary<string, UIScreen>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            Screens.Clear();

            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
                canvas.gameObject.SetActive(true);

            foreach (var screen in Object.FindObjectsByType<UIScreen>(FindObjectsInactive.Include))
            {
                if (string.IsNullOrEmpty(screen.Key))
                {
                    Debug.LogError($"[UIScreenManager] Key 없는 UIScreen: {screen.gameObject.name}");
                    continue;
                }

                Screens[screen.Key] = screen;
                screen.gameObject.SetActive(false);
            }

            ShowScreen(FirstScreenKey);
        }

        /// <summary>풀스크린 하나를 켜고 나머지 풀스크린은 전부 끈다. 팝업 상태는 안 건드린다.</summary>
        public static void ShowScreen(string key)
        {
            if (!Screens.TryGetValue(key, out var target) || target.Kind != UIScreenKind.FullScreen)
            {
                Debug.LogError($"[UIScreenManager] 풀스크린 '{key}' 을 찾지 못했다.");
                return;
            }

            foreach (var kv in Screens)
                if (kv.Value.Kind == UIScreenKind.FullScreen)
                    kv.Value.gameObject.SetActive(kv.Key == key);
        }

        public static void ShowPopup(string key)
        {
            if (Screens.TryGetValue(key, out var target) && target.Kind == UIScreenKind.Popup)
                target.gameObject.SetActive(true);
            else
                Debug.LogError($"[UIScreenManager] 팝업 '{key}' 을 찾지 못했다.");
        }

        public static void HidePopup(string key)
        {
            if (Screens.TryGetValue(key, out var target) && target.Kind == UIScreenKind.Popup)
                target.gameObject.SetActive(false);
        }

        /// <summary>풀스크린이든 팝업이든 상관없이 현재 켜져 있는지. 화면 트리 밖(씬 루트) 컴포넌트가
        /// 자기 입력을 켜야 할지 판단할 때 쓴다 — <see cref="View.PlacementController"/> 처럼.</summary>
        public static bool IsActive(string key) =>
            Screens.TryGetValue(key, out var screen) && screen.gameObject.activeSelf;
    }
}
