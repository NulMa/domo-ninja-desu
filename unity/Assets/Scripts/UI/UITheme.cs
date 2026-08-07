using DomoNinja.Unity.View;
using UnityEngine;
using UnityEngine.UI;

namespace DomoNinja.Unity
{
    /// <summary>
    /// 버튼의 <b>눌린 느낌</b>을 한 곳에서 만든다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 화면 컨트롤러 8개가 <c>EnsureButton</c> 을 <b>글자 하나 다르지 않게 각자 들고 있었다.</b>
    /// 그 상태로 버튼 연출을 바꾸려면 8곳을 같이 고쳐야 하고, 한 곳을 빠뜨리면
    /// <b>화면 하나만 반응이 다른</b> 상태가 된다 — 그건 눈으로 찾기 어렵다.
    /// </para>
    /// <para>
    /// ★ <b>스프라이트 교체와 색 틴트를 나눈다.</b>
    /// 나무 버튼(<c>button_normal</c>)에는 팩이 같이 준 <c>_hover</c>·<c>_pressed</c>·<c>_disabled</c> 를 쓴다.
    /// 그런데 슬롯(<c>inventory_cell</c>)이나 판에까지 같은 규칙을 걸면
    /// <b>칸이 눌릴 때 버튼 모양으로 바뀐다.</b> 그래서 그림이 버튼일 때만 교체하고,
    /// 나머지는 밝기만 움직인다.
    /// </para>
    /// <para>
    /// 스프라이트는 <see cref="SpriteCatalog"/> 에서 꺼낸다. 런타임은 경로로 파일을 찾을 수 없고,
    /// 이미 <c>Assets/Sprite/**</c> 전체를 색인하는 표가 있다.
    /// </para>
    /// </remarks>
    public static class UITheme
    {
        public const string ButtonNormalKey = "UI/Theme/button_normal";
        private const string ButtonHoverKey = "UI/Theme/button_hover";
        private const string ButtonPressedKey = "UI/Theme/button_pressed";
        private const string ButtonDisabledKey = "UI/Theme/button_disabled";

        private static SpriteCatalog _catalog;
        private static bool _catalogLoaded;

        private static SpriteCatalog Catalog
        {
            get
            {
                if (!_catalogLoaded)
                {
                    _catalog = Resources.Load<SpriteCatalog>(SpriteCatalog.ResourceName);
                    _catalogLoaded = true;
                }
                return _catalog;
            }
        }

        public static Sprite Find(string key) => Catalog != null ? Catalog.Find(key) : null;

        /// <summary>
        /// 코드로 세우는 캔버스의 <b>겹침 순서</b>. 숫자를 각자 들고 있지 않게 한 곳에 모은다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 씬의 팝업은 <see cref="UIScreenManager.ShowPopup"/> 이 열 때마다 10 부터 하나씩 올려 발급한다.
        /// 코드로 세우는 캔버스는 그 경로를 타지 않으므로 여기서 직접 정한다 —
        /// <b>다만 값을 파일마다 적으면 같은 수가 두 번 나온다.</b>
        /// </para>
        /// <para>
        /// ★ 실제로 그렇게 됐다. <c>BattleVictoryPopup</c>(팀원) 과 <c>UIConfirmPopup</c>(나) 이
        /// <b>둘 다 8000</b> 이었다. sortingOrder 가 동률이면 어느 캔버스가 클릭을 먼저 받는지
        /// 화면 계층과 무관하게 정해진다 — <c>b98448d</c> 가 방금 고친 것이 정확히 그 문제다.
        /// 같은 함정을 코드 캔버스 쪽에서 다시 만들 뻔했다.
        /// </para>
        /// </remarks>
        public static class Layer
        {
            /// <summary>일반 팝업. 씬 팝업(10~)보다 위.</summary>
            public const int Popup = 8000;

            /// <summary>오프닝 스토리. 타이틀 위에서 한 번 재생되고 끝난다.</summary>
            public const int Story = 8100;

            /// <summary>튜토리얼. <b>스토리보다 위</b> — 스토리가 끝나며 스테이지 선택으로 넘어갈 때 겹칠 수 있다.</summary>
            public const int Tutorial = 8200;

            /// <summary>확인창. <b>어떤 팝업보다 위여야 한다</b> — 팝업 위에서 물어보는 창이다.</summary>
            public const int Confirm = 8500;

            /// <summary>로딩 덮개. 전부 위. 이게 덮여 있으면 아무것도 누르면 안 된다.</summary>
            public const int Loading = 9000;
        }

        /// <summary>
        /// 코드로 만드는 전체화면 캔버스를 씬의 다른 캔버스와 같은 방식으로 세운다.
        /// </summary>
        /// <remarks>
        /// ★ <c>ScreenSpaceOverlay</c> 가 아니라 <c>ScreenSpaceCamera</c> 를 쓴다.
        /// Overlay 는 <b>카메라 렌더에 안 잡혀서</b> 우리가 쓰는 확인 방법(카메라를 RenderTexture 로 찍기)에
        /// 걸리지 않는다. 실제로 확인창을 Overlay 로 만들었다가 <b>화면에 떠 있는데 캡처는 비어 있는</b>
        /// 상태를 겪었다. <b>확인할 수 없는 화면은 결국 확인하지 않게 된다.</b>
        /// 씬의 캔버스가 전부 ScreenSpaceCamera 라 방식도 이쪽이 일관된다.
        /// </remarks>
        public static void SetupFullScreenCanvas(GameObject host, int sortingOrder)
        {
            var canvas = host.GetComponent<Canvas>();
            if (canvas == null) canvas = host.AddComponent<Canvas>();

            var camera = Camera.main;
            if (camera != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 0.5f;
            }
            else
            {
                // 카메라가 없으면 그려지지 않는 것보다 Overlay 로라도 뜨는 편이 낫다.
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            canvas.sortingOrder = sortingOrder;

            var scaler = host.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = host.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            if (host.GetComponent<GraphicRaycaster>() == null) host.AddComponent<GraphicRaycaster>();
        }

        /// <summary>
        /// <b>이로운 것과 손해를 색으로 가른다.</b> 스킬·아이템 설명이 쓰는 유일한 표기 규칙이다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 전에는 「얻는 것: …」「버리는 것: …」 이라고 <b>글자로 적었다.</b> 그런데 이 게임의 스킬은
        /// 전부 <b>주고받는 거래</b>라(`08` §2.6) 두 줄이 항상 같이 나온다 — 그러면 라벨 네 글자가
        /// 스킬 30종 × 두 줄만큼 반복되면서, 정작 읽어야 할 <b>수치</b>를 밀어낸다.
        /// 색이 그 역할을 대신하면 같은 자리에 내용만 남는다.
        /// </para>
        /// <para>
        /// ★ <b>색만으로 가르지는 않는다.</b> 초록/빨강은 하필 적록색약이 가장 못 가르는 짝이라,
        /// 색이 유일한 단서면 그 사람에게는 <b>구분이 아예 사라진다.</b> 그래서 <c>+</c>/<c>−</c>
        /// 한 글자를 같이 붙인다 — 라벨처럼 길지 않으면서 색이 안 보여도 방향이 남는다.
        /// </para>
        /// <para>
        /// 손해 쪽은 <b>어두운 벽돌빛</b>이다. 순수한 빨강은 나무 배경 위에서 경고문처럼 튀어
        /// "이 스킬은 고르면 안 된다"로 읽힌다 — 손해는 <b>대가</b>지 경고가 아니다.
        /// </para>
        /// </remarks>
        public static class Semantic
        {
            /// <summary>이로운 효과. 어두운 판 위에서 읽히도록 순수 초록보다 밝고 노랗다.</summary>
            public const string GainHex = "7FC96B";

            /// <summary>손해(대가). 검붉은 벽돌빛 — 어두운 판 위에서 읽히는 하한선까지만 낮췄다.</summary>
            public const string CostHex = "C0524A";

            /// <summary>이로운 쪽으로 칠한다. 앞에 <c>+</c> 를 붙여 색이 안 보여도 방향이 남게 한다.</summary>
            public static string Gain(string text) => $"<color=#{GainHex}>+ {text}</color>";

            /// <summary>손해 쪽으로 칠한다. 앞에 <c>−</c>(U+2212) 를 붙인다 — 하이픈과 달리 숫자와 안 붙어 보인다.</summary>
            public static string Cost(string text) => $"<color=#{CostHex}>− {text}</color>";

            /// <summary>곁들이는 정보. 본문보다 흐리고 작다.</summary>
            public const string MutedHex = "9A9188";

            /// <summary>
            /// 본문에 딸린 부가 정보(누구 스킬인지 등). <b>작고 흐리게</b> — 이름과 같은 무게로 쓰면
            /// 무엇이 스킬 이름인지가 안 갈린다.
            /// </summary>
            public static string Muted(string text) => $"<size=80%><color=#{MutedHex}>{text}</color></size>";

            /// <summary>지금은 못 사는 값. <see cref="CostHex"/> 를 그대로 쓴다 — 「손해」와 「못 삼」은
            /// 둘 다 <b>빨간 쪽</b>이고, 색을 하나 더 늘리면 화면에서 무엇이 무엇인지 안 갈린다.</summary>
            public static string Unaffordable(string text) => $"<color=#{CostHex}>{text}</color>";
        }

        /// <summary>버튼 컴포넌트를 붙이고 연출을 입힌다. 이미 있으면 그대로 쓴다.</summary>
        public static Button EnsureButton(GameObject go)
        {
            var btn = go.GetComponent<Button>();
            if (btn == null) btn = go.AddComponent<Button>();

            var img = go.GetComponent<Image>();
            if (img != null) btn.targetGraphic = img;

            Apply(btn);
            UIAudioHooks.Attach(btn);
            return btn;
        }

        /// <summary>이미 있는 버튼에 연출만 입힌다. 에디터 일괄 적용도 이 함수를 쓴다.</summary>
        public static void Apply(Button btn)
        {
            if (btn == null) return;

            var img = btn.targetGraphic as Image;
            bool isWoodButton = img != null && img.sprite != null && img.sprite.name == "button_normal";

            if (isWoodButton)
            {
                var hover = Find(ButtonHoverKey);
                var pressed = Find(ButtonPressedKey);
                var disabled = Find(ButtonDisabledKey);

                // 표가 아직 안 만들어졌으면 조용히 색 틴트로 떨어진다.
                // 여기서 예외를 던지면 표를 다시 굽기 전까지 화면이 아예 안 뜬다.
                if (hover != null && pressed != null && disabled != null)
                {
                    btn.transition = Selectable.Transition.SpriteSwap;
                    btn.spriteState = new SpriteState
                    {
                        highlightedSprite = hover,
                        pressedSprite = pressed,
                        // ★ selected 에는 hover 를 넣지 않는다.
                        //   `UIFocusRing` 이 화면이 바뀔 때마다 첫 버튼을 자동으로 선택하는데,
                        //   selected 를 hover 로 두면 **손도 안 댄 버튼 하나가 늘 눌린 것처럼 보인다.**
                        //   초점은 링이 이미 그리고 있으므로 그림까지 바꿀 이유가 없다.
                        selectedSprite = null,
                        disabledSprite = disabled,
                    };
                    return;
                }
            }

            btn.transition = Selectable.Transition.ColorTint;
            btn.colors = TintColors;
        }

        /// <summary>
        /// 그림 위에 곱해지는 값이라 흰색이 원본 그대로다.
        /// </summary>
        /// <remarks>
        /// 기본값은 눌림이 0.78 이고 비활성이 알파 0.5 인데, 픽셀 그림 위에서는 둘 다 잘 안 보인다.
        /// 알파를 깎으면 <b>뒤의 판이 비쳐서</b> 흐려진 것인지 반투명한 것인지 구분되지 않는다 — 밝기만 움직인다.
        /// </remarks>
        private static readonly ColorBlock TintColors = new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = new Color(1.00f, 0.94f, 0.82f),
            pressedColor = new Color(0.72f, 0.68f, 0.62f),
            // 선택은 기본과 같게. 초점 표시는 링이 맡는다 — 여기서 밝히면
            // 자동 선택된 칸 하나가 이유 없이 밝아 보인다.
            selectedColor = Color.white,
            disabledColor = new Color(0.48f, 0.46f, 0.44f, 1f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f,
        };
    }
}
