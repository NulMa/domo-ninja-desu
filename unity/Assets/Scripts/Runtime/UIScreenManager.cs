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

            ShowScreen("StageSelect");
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
