using UnityEngine;
using UnityEngine.UI;
using UImage = UnityEngine.UI.Image;

namespace DomoNinja.Unity
{
    /// <summary>
    /// 메뉴 화면 뒤에 <b>배경 그림</b>을 깐다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 지시(사용자): "메인메뉴나 인게임 배틀페이즈 보면 배경에 아무 에셋이 없어서 빈약해 보임.
    /// 메인메뉴는 시작씬에 쓰던 이미지를 약간 어둡고 채도를 빼서 뒤에 깔아두고."
    /// </para>
    /// <para>
    /// ★ <b>채도·밝기를 런타임 틴트로 낮추지 않는다.</b> <c>Image.color</c> 는 곱셈이라
    /// 어둡게는 할 수 있어도 <b>채도는 못 뺀다</b> — 빨간 옷은 어두운 빨강이 될 뿐이다.
    /// 그래서 <c>TitleBackdrop.png</c> 를 <b>미리 처리해 별도 에셋으로</b> 두고 그대로 깐다
    /// (`14` 에 출처 기록). 셰이더를 새로 쓰는 것보다 웹 빌드 용량에도 유리하다.
    /// </para>
    /// <para>
    /// 씬을 건드리지 않고 코드로 붙인다 (`19` §5.1 — 씬 파일은 손으로 병합이 안 된다).
    /// 화면마다 손으로 넣으면 <b>새 화면이 생길 때 빠뜨린다.</b>
    /// </para>
    /// </remarks>
    public static class ScreenBackdrop
    {
        private const string BackdropKey = "UI/TitleBackdrop";
        private const string ChildName = "ScreenBackdrop";

        /// <summary>
        /// 배경을 깔 화면들.
        /// </summary>
        /// <remarks>
        /// 전투 화면(<c>GamePlay</c>)은 <b>여기 없다</b> — 그쪽은 판 자체가 배경이고
        /// (<c>BoardView</c> 가 바닥 타일을 깐다), UI 배경까지 깔면 판이 안 읽힌다.
        /// <c>Title</c> 도 없다 — 거기는 원본 그림이 이미 주인공이다.
        /// </remarks>
        private static readonly string[] Screens =
        {
            "StageSelect", "RosterSelect", "Shop", "StageIntro", "MetaUpgrade", "Result",
        };

        /// <remarks>
        /// ★ <c>UIScreenManager</c> 의 등록표를 안 쓰고 <see cref="UIScreen"/> 을 직접 훑는다.
        /// 그쪽 <c>Bootstrap</c> 도 <c>AfterSceneLoad</c> 라 <b>둘 중 어느 것이 먼저 도는지 정해져
        /// 있지 않다</b> — 등록표를 먼저 읽으면 비어 있어서 배경이 조용히 안 깔린다.
        /// 컴포넌트를 직접 찾으면 순서와 무관하다.
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallAll()
        {
            var sprite = UITheme.Find(BackdropKey);
            if (sprite == null) return;

            foreach (var screen in Object.FindObjectsByType<UIScreen>(FindObjectsInactive.Include))
            {
                if (string.IsNullOrEmpty(screen.Key)) continue;
                if (System.Array.IndexOf(Screens, screen.Key) < 0) continue;
                Install(screen.gameObject, sprite);
            }
        }

        /// <summary>
        /// 화면 루트의 <b>맨 앞 자식</b>으로 전체 화면 그림을 넣는다 — 그래야 내용 뒤에 깔린다.
        /// </summary>
        private static void Install(GameObject screen, Sprite sprite)
        {
            if (screen.transform.Find(ChildName) != null) return;

            var go = new GameObject(ChildName, typeof(RectTransform));
            go.transform.SetParent(screen.transform, false);
            go.transform.SetSiblingIndex(0);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.AddComponent<UImage>();
            img.sprite = sprite;

            // ★ 클릭을 먹으면 안 된다. 화면 전체를 덮으므로 raycast 를 켜두면
            //   뒤에 깔린 판이 아니라 이 그림이 눌린다.
            img.raycastTarget = false;

            // 화면비가 달라도 잘리는 쪽을 택한다 — 늘어난 그림은 도트가 뭉개져 보인다.
            img.preserveAspect = true;
        }
    }
}
