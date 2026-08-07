using System.Text;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UImage = UnityEngine.UI.Image;

namespace DomoNinja.Unity
{
    /// <summary>
    /// 마우스를 올린 것의 <b>스펙을 그 자리에 띄운다.</b> 화면당 하나만 존재한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 스테이지 인트로(적 편성)와 전투 화면(양쪽 유닛)이 <b>같은 것을 물어본다</b> —
    /// "저건 뭐고 얼마나 센가". 두 화면이 각자 툴팁을 그리면 같은 정보가 다른 모양으로
    /// 두 번 나오고, 스탯 항목을 하나 늘릴 때 한쪽만 고치게 된다.
    /// </para>
    /// <para>
    /// ★ 팝업이 아니라 <b>따라다니는 판</b>이다. <see cref="UIScreenManager"/> 에 등록하지 않는다 —
    /// 등록하면 초점·뒤로가기 대상이 되는데, 툴팁은 <b>끄는 조작이 없는</b> 표시물이다.
    /// 마우스가 벗어나면 사라지는 것이 유일한 수명 규칙이다.
    /// </para>
    /// <para>
    /// 레이어는 확인창(8500)보다 위, 로딩 덮개(9000)보다 아래다. 확인창 위에 뜬 항목에
    /// 마우스를 올려도 가려지지 않아야 하지만, 로딩 중에는 아무것도 안 보여야 한다.
    /// </para>
    /// </remarks>
    public sealed class UnitTooltip : MonoBehaviour
    {
        private const int SortingOrder = 8700;
        private const float MaxWidth = 320f;
        private const float Padding = 12f;

        /// <summary>커서와 판 사이 간격. 0 이면 판이 커서를 덮어 <b>다음 항목으로 못 넘어간다.</b></summary>
        private static readonly Vector2 CursorOffset = new Vector2(18f, -18f);

        private static UnitTooltip _instance;

        private RectTransform _panel;
        private TMP_Text _label;
        private Canvas _canvas;
        private bool _visible;

        private static UnitTooltip Instance
        {
            get
            {
                if (_instance == null) _instance = Create();
                return _instance;
            }
        }

        /// <summary>내용을 채우고 띄운다. 빈 문자열이면 <see cref="Hide"/> 와 같다.</summary>
        public static void Show(string richText)
        {
            if (string.IsNullOrEmpty(richText)) { Hide(); return; }

            var t = Instance;
            t._label.text = richText;
            t._panel.gameObject.SetActive(true);
            t._visible = true;
            t.Reposition();
        }

        public static void Hide()
        {
            if (_instance == null) return;
            _instance._visible = false;
            _instance._panel.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_visible) Reposition();
        }

        /// <summary>
        /// 커서 옆에 두되 <b>화면 밖으로 나가지 않게</b> 접는다.
        /// </summary>
        /// <remarks>
        /// 오른쪽 끝 항목에 올렸을 때 판이 화면 밖으로 나가면 <b>글자가 잘린 채로 뜬다</b> —
        /// 그건 정보가 없는 것보다 나쁘다(있는 줄 알고 읽으려 하게 된다). 넘칠 때는 커서 왼쪽으로 넘긴다.
        /// </remarks>
        private void Reposition()
        {
            // 이 프로젝트는 입력 처리를 Input System 패키지로 바꿔놨다 — 옛 `UnityEngine.Input` 은 예외를 던진다.
            if (Mouse.current == null) return;

            var canvasRect = (RectTransform)_canvas.transform;
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, Mouse.current.position.ReadValue(),
                _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera,
                out local);

            // 레이아웃이 아직 안 돌았으면 크기가 0 이라 접기 판정이 틀린다 — 강제로 한 번 돌린다.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_panel);
            var size = _panel.rect.size;
            var half = canvasRect.rect.size * 0.5f;

            float x = local.x + CursorOffset.x;
            float y = local.y + CursorOffset.y;

            if (x + size.x > half.x) x = local.x - CursorOffset.x - size.x;
            if (y - size.y < -half.y) y = local.y - CursorOffset.y + size.y;

            _panel.anchoredPosition = new Vector2(x, y);
        }

        private static UnitTooltip Create()
        {
            var host = new GameObject("UnitTooltip");
            DontDestroyOnLoad(host);
            UITheme.SetupFullScreenCanvas(host, SortingOrder);

            var self = host.AddComponent<UnitTooltip>();
            self._canvas = host.GetComponent<Canvas>();

            // ★ 툴팁 자신은 클릭을 먹으면 안 된다. 먹으면 커서 아래 버튼이 안 눌린다.
            var raycaster = host.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            if (raycaster != null) raycaster.enabled = false;

            var panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(host.transform, false);
            self._panel = (RectTransform)panelGo.transform;
            self._panel.anchorMin = new Vector2(0.5f, 0.5f);
            self._panel.anchorMax = new Vector2(0.5f, 0.5f);
            self._panel.pivot = new Vector2(0f, 1f);

            var bg = panelGo.AddComponent<UImage>();
            bg.sprite = UITheme.Find("UI/Theme/nine_path_panel");
            bg.type = UImage.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 1f;
            bg.raycastTarget = false;

            var fitter = panelGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset((int)Padding, (int)Padding, (int)Padding, (int)Padding);
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(panelGo.transform, false);
            self._label = labelGo.AddComponent<TextMeshProUGUI>();
            self._label.fontSize = 20f;
            self._label.color = new Color(0.95f, 0.93f, 0.88f);
            self._label.raycastTarget = false;
            self._label.textWrappingMode = TextWrappingModes.Normal;

            var element = labelGo.AddComponent<LayoutElement>();
            element.preferredWidth = MaxWidth;

            panelGo.SetActive(false);
            return self;
        }
    }

    /// <summary>
    /// UI 조각 하나에 <see cref="UnitTooltip"/> 을 물린다. 내용은 <b>부를 때 만든다.</b>
    /// </summary>
    /// <remarks>
    /// 문자열을 미리 만들어 들고 있지 않고 <see cref="Describe"/> 대리자를 받는 이유 —
    /// 전투 중에는 <b>체력이 계속 변한다.</b> 붙일 때 만든 문자열을 들고 있으면
    /// 툴팁이 전투 시작 시점의 체력을 계속 보여준다.
    /// </remarks>
    public sealed class HoverTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public System.Func<string> Describe;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Describe != null) UnitTooltip.Show(Describe());
        }

        public void OnPointerExit(PointerEventData eventData) => UnitTooltip.Hide();

        private void OnDisable() => UnitTooltip.Hide();
    }

    /// <summary>
    /// 유닛 스펙을 <b>한 가지 모양으로만</b> 적는다. 인트로·전투·상점이 같이 쓴다.
    /// </summary>
    /// <remarks>
    /// 적과 아군은 들고 있는 타입이 다르지만(<see cref="EnemyTypeDef"/> / <see cref="CharacterDef"/>)
    /// 플레이어가 비교하는 축은 같다 — 체력·공격력·공격 간격·사거리·이동.
    /// 두 곳에서 각자 적으면 <b>항목 순서가 달라져서 비교가 안 된다.</b>
    /// </remarks>
    public static class UnitStatText
    {
        /// <summary>초당 틱(`A-2`). 간격을 초로 환산해 보여줄 때 쓴다.</summary>
        private const float TicksPerSecond = 20f;

        public static string ForEnemy(EnemyTypeDef def, int? currentHp = null)
        {
            var sb = new StringBuilder();
            sb.Append("<b>").Append(def.Name).Append("</b>");
            if (def.IsBoss) sb.Append("  <color=#E0B252>보스</color>");
            sb.Append('\n');
            AppendStats(sb, currentHp, def.Hp, def.Attack, def.AttackInterval, def.Range,
                        def.Immobile ? (int?)null : def.MoveInterval);
            if (def.Immobile) sb.Append("\n<color=#9A948C>고정 — 움직이지 않는다</color>");
            return sb.ToString();
        }

        public static string ForCharacter(CharacterDef def, int? currentHp = null)
        {
            var sb = new StringBuilder();
            sb.Append("<b>").Append(def.Name).Append("</b>\n");
            AppendStats(sb, currentHp, def.Hp, def.Attack, def.AttackInterval, def.Range, def.MoveInterval);
            return sb.ToString();
        }

        /// <summary>
        /// 출전 중인 아군 — 스펙에 <b>지금까지 산 것</b>을 붙인다.
        /// </summary>
        /// <remarks>
        /// 적과 달리 아군은 <b>런 도중 변한다.</b> 액티브 스킬·보조 스킬·아이템이 붙은 뒤에도
        /// 기본 스탯만 보여주면 "왜 이 캐릭터가 세졌는지"가 화면 어디에도 안 남는다.
        /// <para>
        /// ⚠️ 스탯 숫자는 <b>데이터 원본값</b>이다 — 아이템 배율이 곱해진 실제 전투값이 아니다.
        /// 그 계산은 <c>BattleSetup</c> 이 전투 시작 시점에 하고 뷰까지 안 내려온다.
        /// 그래서 산 것을 <b>목록으로</b> 같이 보여준다 — 곱해진 값을 흉내내면 실제와 어긋난다.
        /// </para>
        /// </remarks>
        public static string ForDeployedAlly(CharacterDef def, RosterEntry entry, GameData data,
                                             int? currentHp = null)
        {
            var sb = new StringBuilder();
            sb.Append("<b>").Append(def.Name).Append("</b>\n");
            AppendStats(sb, currentHp ?? entry.Hp, entry.MaxHp, def.Attack,
                        def.AttackInterval, def.Range, def.MoveInterval);

            var active = entry.ActiveSkillId != null ? data.FindSkill(entry.ActiveSkillId) : null;
            sb.Append("\n\n<color=#C8BFAE>액티브</color> ")
              .Append(active != null ? active.Name : "<color=#9A948C>아직 없음</color>");

            sb.Append("\n<color=#C8BFAE>보조</color> ");
            if (entry.SupportSkillIds.Count == 0) sb.Append("<color=#9A948C>없음</color>");
            else
            {
                for (int i = 0; i < entry.SupportSkillIds.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    var s = data.FindSkill(entry.SupportSkillIds[i]);
                    sb.Append(s != null ? s.Name : entry.SupportSkillIds[i]);
                }
            }

            sb.Append("\n<color=#C8BFAE>강화</color> ");
            if (entry.Items.Count == 0) sb.Append("<color=#9A948C>없음</color>");
            else
            {
                for (int i = 0; i < entry.Items.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(ShopController.ItemNames.TryGetValue(entry.Items[i].Key, out string n)
                        ? n : entry.Items[i].Key);
                }
            }

            return sb.ToString();
        }

        private static void AppendStats(StringBuilder sb, int? currentHp, int maxHp, int attack,
                                        int attackInterval, int range, int? moveInterval)
        {
            sb.Append("체력 ").Append(currentHp.HasValue ? $"{currentHp.Value} / {maxHp}" : maxHp.ToString());
            sb.Append("\n공격력 ").Append(attack);
            sb.Append("\n공격 간격 ").Append(attackInterval).Append("틱 (")
              .Append((attackInterval / TicksPerSecond).ToString("0.0#")).Append("초)");
            sb.Append("\n사거리 ").Append(range).Append("칸");
            if (moveInterval.HasValue)
                sb.Append("\n이동 간격 ").Append(moveInterval.Value).Append("틱");
        }
    }
}
