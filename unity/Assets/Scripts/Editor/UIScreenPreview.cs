using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DomoNinja.Unity.Editor
{
    /// <summary>
    /// <b>플레이 모드에 들어가지 않고</b> 화면 하나만 켜서 편집하기 위한 창.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 화면 11개(풀스크린 4 · 팝업 7)가 전부 같은 자리에 겹쳐 있고, 그 중 무엇을 보여줄지는
    /// <see cref="UIScreenManager"/> 가 <b>런타임에만</b> 정한다(<c>RuntimeInitializeOnLoadMethod</c>).
    /// 에디터에는 그 로직이 돌지 않으므로, 씬을 열면 <b>마지막으로 저장된 활성 상태</b>가 그대로 보인다 —
    /// 고치려는 화면이 꺼져 있거나, 캔버스 루트가 꺼져 있거나, 다른 팝업이 위를 덮고 있다.
    /// 그래서 지금까지는 화면 하나를 보려면 플레이 모드에 들어가야 했고, 플레이 중 편집은 저장되지 않는다.
    /// </para>
    /// <para>
    /// ★ <b>이 창이 바꾸는 건 활성 상태뿐이고, 그 값은 게임 동작에 영향을 주지 않는다.</b>
    /// <c>Bootstrap</c> 이 시작할 때 캔버스를 전부 켜고 화면을 전부 끈 뒤 첫 화면만 켜기 때문이다.
    /// 즉 <b>여기서 무엇을 켜두든 플레이 결과는 같다.</b> 마음 놓고 켜고 끄면 된다.
    /// </para>
    /// <para>
    /// ⚠️ 다만 씬 파일에는 diff 가 남는다. 씬은 손으로 병합할 수 없어서(`19` §5.1) 2인이 같은 씬을
    /// 건드릴 때 diff 를 줄이는 것 자체가 중요하다. <b>커밋 전에 "기준 상태로 정리"를 누를 것.</b>
    /// 그 버튼이 만드는 상태는 <c>Bootstrap</c> 직후와 정확히 같으므로, 기준이 사람의 취향이 아니라
    /// 코드에 이미 정의돼 있다.
    /// </para>
    /// </remarks>
    public sealed class UIScreenPreview : EditorWindow
    {
        private Vector2 _scroll;

        [MenuItem("DomoNinja/화면 미리보기", false, 0)]
        private static void Open()
        {
            var window = GetWindow<UIScreenPreview>();
            window.titleContent = new GUIContent("화면 미리보기");
            window.minSize = new Vector2(280f, 320f);
        }

        private void OnEnable() => EditorApplication.hierarchyChanged += Repaint;

        private void OnDisable() => EditorApplication.hierarchyChanged -= Repaint;

        private void OnGUI()
        {
            var screens = FindScreens();

            if (screens.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "열려 있는 씬에 UIScreen 이 없다.\nScenes/UI Test.unity 를 열었는지 확인할 것.",
                    MessageType.Info);
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox(
                    "플레이 중이다. 지금 켜고 끄는 건 씬에 남지 않는다 — 편집은 플레이를 멈추고 할 것.",
                    MessageType.Warning);
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("풀스크린 — 하나만 켜진다", EditorStyles.boldLabel);
            foreach (var screen in screens.Where(s => s.Kind == UIScreenKind.FullScreen))
                DrawFullScreenRow(screen);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("팝업 — 각각 따로 켠다", EditorStyles.boldLabel);
            foreach (var screen in screens.Where(s => s.Kind == UIScreenKind.Popup))
                DrawPopupRow(screen);

            EditorGUILayout.Space(12f);

            if (GUILayout.Button("기준 상태로 정리 (커밋 전)", GUILayout.Height(26f)))
                NormalizeToRuntimeStart();

            EditorGUILayout.HelpBox(
                $"정리 = 캔버스 전부 켜기 + 화면 전부 끄기 + '{UIScreenManager.FirstScreenKey}' 만 켜기.\n" +
                "게임 시작 직후와 같은 상태다. 여기서 뭘 켜두든 플레이 결과는 안 바뀌지만,\n" +
                "씬 diff 는 남으므로 커밋 전에 눌러서 되돌린다.",
                MessageType.None);

            EditorGUILayout.EndScrollView();
        }

        private void DrawFullScreenRow(UIScreen screen)
        {
            bool on = screen.gameObject.activeSelf && IsCanvasRootActive(screen);

            using (new EditorGUILayout.HorizontalScope())
            {
                // 라디오처럼 쓴다. 풀스크린은 서로 배타적이라 "켠다"가 곧 "나머지를 끈다"다.
                if (GUILayout.Toggle(on, screen.Key, EditorStyles.radioButton) && !on)
                    Solo(screen);

                if (GUILayout.Button("선택", EditorStyles.miniButton, GUILayout.Width(44f)))
                    Selection.activeGameObject = screen.gameObject;
            }
        }

        private void DrawPopupRow(UIScreen screen)
        {
            bool on = screen.gameObject.activeSelf && IsCanvasRootActive(screen);

            using (new EditorGUILayout.HorizontalScope())
            {
                bool next = EditorGUILayout.ToggleLeft(screen.Key, on);
                if (next != on) SetPopup(screen, next);

                if (GUILayout.Button("선택", EditorStyles.miniButton, GUILayout.Width(44f)))
                    Selection.activeGameObject = screen.gameObject;
            }
        }

        /// <summary>풀스크린 하나만 남기고 나머지 화면을 전부 끈다.</summary>
        /// <remarks>
        /// ★ 런타임 <c>ShowScreen</c> 은 팝업을 건드리지 않는데 여기서는 팝업까지 끈다.
        /// 편집할 때 방해가 되는 건 대개 <b>위에 떠 있는 팝업</b>이고, "이 화면만 보고 싶다"가
        /// 이 버튼을 누르는 이유이기 때문이다. 팝업이 필요하면 아래에서 따로 켠다.
        /// </remarks>
        public static void Solo(UIScreen target)
        {
            int group = Undo.GetCurrentGroup();

            ActivateAllCanvasRoots();
            foreach (var screen in FindScreens())
                SetActive(screen.gameObject, screen == target);

            Undo.SetCurrentGroupName($"화면 미리보기 — {target.Key}");
            Undo.CollapseUndoOperations(group);
            MarkDirty(target.gameObject);
        }

        public static void SetPopup(UIScreen target, bool on)
        {
            int group = Undo.GetCurrentGroup();

            if (on) ActivateAllCanvasRoots();
            SetActive(target.gameObject, on);

            Undo.SetCurrentGroupName($"팝업 {(on ? "켜기" : "끄기")} — {target.Key}");
            Undo.CollapseUndoOperations(group);
            MarkDirty(target.gameObject);
        }

        /// <summary>
        /// 씬을 <c>Bootstrap</c> 직후와 같은 상태로 되돌린다. <b>커밋 전 기준 상태다.</b>
        /// </summary>
        [MenuItem("DomoNinja/화면 상태를 기준으로 정리", false, 1)]
        public static void NormalizeToRuntimeStart()
        {
            var screens = FindScreens();
            if (screens.Count == 0) return;

            int group = Undo.GetCurrentGroup();

            ActivateAllCanvasRoots();
            foreach (var screen in screens)
                SetActive(screen.gameObject, screen.Key == UIScreenManager.FirstScreenKey);

            Undo.SetCurrentGroupName("화면 상태 정리");
            Undo.CollapseUndoOperations(group);
            MarkDirty(screens[0].gameObject);
        }

        private static List<UIScreen> FindScreens() =>
            Object.FindObjectsByType<UIScreen>(FindObjectsInactive.Include)
                  .OrderBy(s => s.Key)
                  .ToList();

        /// <summary>
        /// 캔버스 루트는 항상 켜둔다 — <c>Bootstrap</c> 이 하는 일과 같다.
        /// </summary>
        /// <remarks>
        /// 지금 씬에서 화면이 안 보이는 원인의 절반이 이것이다. 화면 오브젝트는 켜져 있는데
        /// 그 위의 <c>UI_Canvas_*</c> 가 꺼져 있으면 <b>하이어라키에서 화면만 봐서는 이유를 알 수 없다.</b>
        /// </remarks>
        private static void ActivateAllCanvasRoots()
        {
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
                SetActive(canvas.gameObject, true);
        }

        private static bool IsCanvasRootActive(UIScreen screen)
        {
            var canvas = screen.GetComponentInParent<Canvas>(true);
            return canvas == null || canvas.gameObject.activeSelf;
        }

        private static void SetActive(GameObject go, bool value)
        {
            if (go.activeSelf == value) return;

            Undo.RecordObject(go, "화면 활성 상태");
            go.SetActive(value);
            EditorUtility.SetDirty(go);
        }

        private static void MarkDirty(GameObject any)
        {
            if (!EditorApplication.isPlaying)
                EditorSceneManager.MarkSceneDirty(any.scene);
        }
    }
}
