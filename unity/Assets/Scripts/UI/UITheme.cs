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

        /// <summary>버튼 컴포넌트를 붙이고 연출을 입힌다. 이미 있으면 그대로 쓴다.</summary>
        public static Button EnsureButton(GameObject go)
        {
            var btn = go.GetComponent<Button>();
            if (btn == null) btn = go.AddComponent<Button>();

            var img = go.GetComponent<Image>();
            if (img != null) btn.targetGraphic = img;

            Apply(btn);
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
                        selectedSprite = hover,
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
            selectedColor = new Color(1.00f, 0.94f, 0.82f),
            disabledColor = new Color(0.48f, 0.46f, 0.44f, 1f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f,
        };
    }
}
