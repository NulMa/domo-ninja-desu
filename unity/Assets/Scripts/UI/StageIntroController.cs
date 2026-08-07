using System.Collections.Generic;
using DomoNinja.Core.Data;
using DomoNinja.Unity.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UImage = UnityEngine.UI.Image;

namespace DomoNinja.Unity
{
    /// <summary>
    /// 라운드 경로 미리보기. 스테이지의 라운드 수만큼 노드를 동적으로 만든다.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>실제 적 구성은 아직 못 보여준다.</b> <c>RunEngine.PlayRound</c>가 변형 선택(RNG)과
    /// 전투 실행을 한 메서드에 붙여놔서, 미리 훔쳐보면 전투가 같이 실행돼버린다.
    /// 그래서 지금은 라운드가 <b>시험하는 축</b>(<see cref="RoundDef.AxisTested"/>, 데이터에 정적으로 박혀
    /// RNG 없이도 읽을 수 있다)과 <b>보스 여부</b>만 보여준다. 보스 여부는 그 라운드에 저작된 변형 중
    /// 하나라도 <c>isBoss</c> 적을 포함하면 참이다 — 이것도 어느 변형이 뽑힐지와 무관한 정적 정보다.
    /// </remarks>
    public sealed class StageIntroController : MonoBehaviour
    {
        private const string SkullIcon = "Actor/Monster/Skull";
        private const string BossIcon = "Actor/Boss/TenguRed";

        /// <summary>노드 한 칸의 고정 크기 — 더 이상 라운드 수에 맞춰 줄어들지 않는다.</summary>
        private const float NodeWidth = 260f;
        private const float NodeGap = 16f;

        // ★ 이 화면만 **단색 사각형**으로 그려져 있어서 나머지 UI 와 톤이 어긋났다.
        //   여기 값들은 이제 나무 테마 스프라이트 **위에 곱해지는 틴트**다.
        //   `Upcoming` 을 0.18 짜리 검정으로 두면 그림이 그냥 사라진다 — 밝기만 낮춘다.
        private static readonly Color CurrentColor = new Color(1.00f, 0.85f, 0.55f);
        private static readonly Color ClearedColor = new Color(0.72f, 0.82f, 0.72f);
        private static readonly Color UpcomingColor = new Color(0.62f, 0.60f, 0.56f);
        private static readonly Color ConnectorColor = new Color(0.52f, 0.44f, 0.36f);
        private static readonly Color TextMain = new Color(0.95f, 0.93f, 0.88f);
        private static readonly Color TextDim = new Color(0.74f, 0.71f, 0.66f);

        private const string NodeSpriteKey = "UI/Theme/inventory_cell";

        private RectTransform _container;
        private ScrollRect _scrollRect;
        private Button _startBattleButton;
        private SpriteCatalog _catalog;
        private TMP_FontAsset _fontAsset;

        private void Awake()
        {
            var board = transform.Find("Board");
            _container = SetupScrollableContainer(board.Find("NodeContainer").GetComponent<RectTransform>(),
                                                    out _scrollRect);
            _startBattleButton = UITheme.EnsureButton(board.Find("StartBattleButton").gameObject);
            _startBattleButton.onClick.AddListener(OnStartBattle);

            _catalog = Resources.Load<SpriteCatalog>(SpriteCatalog.ResourceName);
            _fontAsset = TMP_Settings.defaultFontAsset;
        }

        /// <summary>
        /// 원래 <c>NodeContainer</c> 자리를 뷰포트로 삼고, 그 안에 콘텐츠를 넣어 <see cref="ScrollRect"/>
        /// 로 감싼다. 노드가 고정 크기라 라운드가 많아지면 뷰포트 밖으로 넘치는데, 그걸 잘라내지 않고
        /// 마우스 드래그로 넘겨볼 수 있게 하는 게 목적이다.
        /// </summary>
        private static RectTransform SetupScrollableContainer(RectTransform original, out ScrollRect scrollRect)
        {
            var viewportGo = new GameObject("NodeViewport", typeof(RectTransform));
            var viewport = (RectTransform)viewportGo.transform;
            viewport.SetParent(original.parent, false);
            viewport.anchorMin = original.anchorMin;
            viewport.anchorMax = original.anchorMax;
            viewport.pivot = original.pivot;
            viewport.anchoredPosition = original.anchoredPosition;
            viewport.sizeDelta = original.sizeDelta;
            viewport.SetSiblingIndex(original.GetSiblingIndex());
            viewportGo.AddComponent<RectMask2D>();

            // 완전 투명이지만 raycast는 받는다 — 노드 사이 빈 공간에서도 드래그가 시작되게.
            var catcher = viewportGo.AddComponent<UImage>();
            catcher.color = Color.clear;

            original.SetParent(viewport, false);
            original.anchorMin = new Vector2(0f, original.anchorMin.y);
            original.anchorMax = new Vector2(0f, original.anchorMax.y);
            original.pivot = new Vector2(0f, original.pivot.y);
            original.anchoredPosition = Vector2.zero;

            scrollRect = viewportGo.AddComponent<ScrollRect>();
            scrollRect.content = original;
            scrollRect.horizontal = true;
            scrollRect.vertical = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            return original;
        }

        private void OnEnable() => Rebuild();

        private void Rebuild()
        {
            // Destroy() 는 프레임 끝에야 지워진다 — 바로 이어서 같은 이름의 자식을 새로 만들면
            // 옛 오브젝트가 그대로 남아있는 채로 겹쳐서, Find() 가 옛 것을 먼저 찾는 문제가 생긴다.
            var old = new System.Collections.Generic.List<GameObject>();
            foreach (Transform child in _container) old.Add(child.gameObject);
            foreach (var go in old) DestroyImmediate(go);

            var mgr = RunManager.Instance;
            if (mgr == null || mgr.Data == null) return;

            string stageId = mgr.CurrentRun != null ? mgr.CurrentRun.StageId : mgr.SelectedStageId;
            var rounds = mgr.Data.RoundsFor(stageId);
            int count = rounds.Count;
            if (count == 0) return;

            int currentRound = mgr.CurrentRun != null ? mgr.CurrentRun.Round : 1;

            float nodeHeight = _container.sizeDelta.y;
            float contentWidth = count * NodeWidth + (count - 1) * NodeGap;
            _container.sizeDelta = new Vector2(contentWidth, nodeHeight);

            for (int i = 0; i < count; i++)
            {
                float x = i * (NodeWidth + NodeGap);
                if (i > 0) BuildConnector(x - NodeGap, nodeHeight, NodeGap);
                BuildNode(rounds[i], x, NodeWidth, nodeHeight, mgr.Data, i + 1, currentRound);
            }

            ScrollToRound(currentRound, count, contentWidth);
        }

        /// <summary>현재 라운드 노드가 뷰포트 안에 보이도록 스크롤 위치를 맞춘다.</summary>
        private void ScrollToRound(int currentRound, int count, float contentWidth)
        {
            if (_scrollRect == null) return;

            float viewportWidth = ((RectTransform)_scrollRect.transform).rect.width;
            float overflow = contentWidth - viewportWidth;
            if (overflow <= 0f)
            {
                _scrollRect.horizontalNormalizedPosition = 0f;
                return;
            }

            int index = Mathf.Clamp(currentRound - 1, 0, count - 1);
            float nodeCenterX = index * (NodeWidth + NodeGap) + NodeWidth / 2f;
            _scrollRect.horizontalNormalizedPosition =
                Mathf.Clamp01((nodeCenterX - viewportWidth / 2f) / overflow);
        }

        private void BuildConnector(float x, float nodeHeight, float gap)
        {
            var rt = MakeRT("Connector", _container);
            SetRect(rt, x, nodeHeight / 2f - 4f, gap, 8f);
            var img = rt.gameObject.AddComponent<UImage>();
            img.sprite = UITheme.Find("UI/Theme/nine_path_bg");
            img.type = UImage.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;
            img.color = ConnectorColor;
        }

        private void BuildNode(RoundDef round, float x, float width, float height, GameData data,
                                int roundNumber, int currentRound)
        {
            bool boss = RoundHasBoss(round, data);
            bool current = roundNumber == currentRound;
            bool cleared = roundNumber < currentRound;

            var node = MakeRT("Node" + roundNumber, _container);
            SetRect(node, x, 0, width, height);

            var nodeImage = node.gameObject.AddComponent<UImage>();
            nodeImage.sprite = UITheme.Find(NodeSpriteKey);
            nodeImage.type = UImage.Type.Sliced;
            nodeImage.pixelsPerUnitMultiplier = 1f;
            nodeImage.color = current ? CurrentColor : cleared ? ClearedColor : UpcomingColor;

            // 아이콘 — 중앙 기준 상단.
            var icon = MakeRT("Icon", node);
            icon.anchorMin = new Vector2(0.5f, 1f);
            icon.anchorMax = new Vector2(0.5f, 1f);
            icon.pivot = new Vector2(0.5f, 1f);
            icon.anchoredPosition = new Vector2(0, -12);
            icon.sizeDelta = new Vector2(72, 72);
            var iconImg = icon.gameObject.AddComponent<UImage>();
            iconImg.sprite = _catalog != null ? _catalog.Find(boss ? BossIcon : SkullIcon) : null;
            iconImg.preserveAspect = true;

            var roundLabel = MakeRT("RoundLabel", node);
            roundLabel.anchorMin = new Vector2(0, 1);
            roundLabel.anchorMax = new Vector2(1, 1);
            roundLabel.pivot = new Vector2(0.5f, 1f);
            roundLabel.anchoredPosition = new Vector2(0, -92);
            roundLabel.sizeDelta = new Vector2(0, 24);
            AddText(roundLabel, boss ? $"R{roundNumber} 보스" : $"R{roundNumber}", 14,
                    current ? Color.white : TextMain, TextAlignmentOptions.Center);

            BuildEnemyRow(node, round, data, current);

            if (current)
            {
                var marker = MakeRT("CurrentMarker", node);
                marker.anchorMin = new Vector2(0.5f, 1f);
                marker.anchorMax = new Vector2(0.5f, 1f);
                marker.pivot = new Vector2(0.5f, 0f);
                marker.anchoredPosition = new Vector2(0, 6);
                marker.sizeDelta = new Vector2(30, 22);
                AddText(marker, "▼", 18, CurrentColor, TextAlignmentOptions.Center);
            }
        }

        /// <summary>
        /// 패널 안쪽 — <b>적 초상화 줄.</b> 예전에 <c>AxisTested</c> 문구가 있던 자리다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 지시(사용자): *"문구보다 해당 스테이지 몬스터 초상화 및 개체수를 띄우는 게 나아 보임.
        /// 기존의 아이콘과 패널 구조는 이어가면서 패널 내부에 적 초상화를."*
        /// <c>AxisTested</c>("공격 리듬" 같은 <b>설계 메모</b>)는 플레이어가 할 수 있는 판단과
        /// 상관이 없어서 그 자리만 초상화로 바꾼다 — 패널·아이콘·라운드 라벨·현재 표시는 그대로다.
        /// </para>
        /// <para>
        /// ★ <b>현재 라운드만 마릿수를 적는다.</b> 이번 라운드 편성은 <see cref="RunManager.PeekVariant"/>
        /// 로 확정돼 있지만(`D-75`), <b>다음 라운드들은 아직 어느 변형이 뽑힐지 정해지지 않았다.</b>
        /// 그래서 뒤 칸은 "나올 수 있는 종류"만 흐리게 보여주고 숫자를 안 적는다 —
        /// 거기에 마릿수를 적으면 <b>정해지지 않은 것을 정해진 것처럼</b> 보여주게 된다.
        /// </para>
        /// </remarks>
        private void BuildEnemyRow(RectTransform node, RoundDef round, GameData data, bool current)
        {
            var groups = current ? CurrentLineup(data) : PossibleTypes(round, data);
            if (groups.Count == 0) return;

            var row = MakeRT("EnemyRow", node);
            row.anchorMin = new Vector2(0, 0);
            row.anchorMax = new Vector2(1, 1);
            row.offsetMin = new Vector2(8, 8);
            row.offsetMax = new Vector2(-8, -120);

            const float iconSize = 48f;
            const float gap = 6f;
            int shown = Mathf.Min(groups.Count, 4);
            float totalWidth = shown * iconSize + (shown - 1) * gap;

            for (int i = 0; i < shown; i++)
            {
                var g = groups[i];

                var cell = MakeRT("Enemy_" + g.Def.Type, row);
                cell.anchorMin = new Vector2(0.5f, 1f);
                cell.anchorMax = new Vector2(0.5f, 1f);
                cell.pivot = new Vector2(0f, 1f);
                cell.anchoredPosition = new Vector2(-totalWidth * 0.5f + i * (iconSize + gap), 0f);
                cell.sizeDelta = new Vector2(iconSize, iconSize + 18f);

                var portrait = MakeRT("Portrait", cell);
                portrait.anchorMin = new Vector2(0.5f, 1f);
                portrait.anchorMax = new Vector2(0.5f, 1f);
                portrait.pivot = new Vector2(0.5f, 1f);
                portrait.anchoredPosition = Vector2.zero;
                portrait.sizeDelta = new Vector2(iconSize, iconSize);

                var img = portrait.gameObject.AddComponent<UImage>();
                img.sprite = FindPortrait(g.Def);
                img.preserveAspect = true;
                // 아직 안 정해진 라운드는 흐리게 — "확정된 편성"과 눈으로 구분돼야 한다.
                img.color = current ? Color.white : new Color(1f, 1f, 1f, 0.45f);

                if (current)
                {
                    var countLabel = MakeRT("CountLabel", cell);
                    countLabel.anchorMin = new Vector2(0, 0);
                    countLabel.anchorMax = new Vector2(1, 0);
                    countLabel.pivot = new Vector2(0.5f, 0f);
                    countLabel.anchoredPosition = Vector2.zero;
                    countLabel.sizeDelta = new Vector2(0, 18);
                    AddText(countLabel, "×" + g.Count, 13, CurrentColor, TextAlignmentOptions.Center);
                }

                // ★ 초상화만으로는 "저게 얼마나 센가"를 모른다. 스펙은 툴팁으로 뺀다 —
                //   칸 안에 다섯 줄을 박으면 초상화가 도로 안 보인다.
                var def = g.Def;
                var hover = cell.gameObject.AddComponent<HoverTooltipTrigger>();
                hover.Describe = () => UnitStatText.ForEnemy(def);
            }

            // 네 종류를 넘으면 마지막 칸 옆에 "+n" 을 둔다. 칸을 좁히면 초상화가 뭉개진다.
            if (groups.Count > shown)
            {
                var more = MakeRT("MoreLabel", row);
                more.anchorMin = new Vector2(0.5f, 1f);
                more.anchorMax = new Vector2(0.5f, 1f);
                more.pivot = new Vector2(0f, 1f);
                more.anchoredPosition = new Vector2(totalWidth * 0.5f + gap, -14f);
                more.sizeDelta = new Vector2(40, 20);
                AddText(more, "+" + (groups.Count - shown), 13, TextDim, TextAlignmentOptions.Left);
            }
        }

        /// <summary>이번 라운드 확정 편성 — 같은 종류를 묶고 마릿수를 센다.</summary>
        /// <remarks>
        /// 슬라임 3마리를 칸 3개로 늘어놓으면 <b>종류가 셋인 것처럼 읽힌다.</b> 플레이어가 세는
        /// 단위는 "무엇이 몇"이지 "몇 번째 자리에 무엇"이 아니다 — 시작 좌표는 고정 위치가 아니다(`A5`).
        /// </remarks>
        private static List<EnemyGroup> CurrentLineup(GameData data)
        {
            var list = new List<EnemyGroup>();
            var mgr = RunManager.Instance;
            var variant = mgr != null ? mgr.PeekVariant() : null;
            if (variant == null) return list;

            var indexByType = new Dictionary<string, int>();
            foreach (var unit in variant.Units)
            {
                if (indexByType.TryGetValue(unit.Type, out int at))
                {
                    list[at] = new EnemyGroup(list[at].Def, list[at].Count + 1);
                    continue;
                }

                if (!data.EnemyTypes.TryGetValue(unit.Type, out var def)) continue;
                indexByType[unit.Type] = list.Count;
                list.Add(new EnemyGroup(def, 1));
            }

            return list;
        }

        /// <summary>아직 안 뽑힌 라운드 — 변형 전체에서 <b>나올 수 있는 종류</b>만. 마릿수는 없다.</summary>
        private static List<EnemyGroup> PossibleTypes(RoundDef round, GameData data)
        {
            var list = new List<EnemyGroup>();
            var seen = new HashSet<string>();

            foreach (var variant in round.Variants)
                foreach (var unit in variant.Units)
                {
                    if (!seen.Add(unit.Type)) continue;
                    if (data.EnemyTypes.TryGetValue(unit.Type, out var def))
                        list.Add(new EnemyGroup(def, 0));
                }

            return list;
        }

        /// <summary>초상이 색인돼 있으면 그걸, 없으면 몸통 그림을 쓴다.</summary>
        private Sprite FindPortrait(EnemyTypeDef def)
        {
            if (_catalog == null) return null;
            return _catalog.Find(def.Sprite + "/Faceset") ?? _catalog.Find(def.Sprite);
        }

        private readonly struct EnemyGroup
        {
            public readonly EnemyTypeDef Def;

            /// <summary>확정 편성일 때만 의미 있다. 미확정 라운드는 0.</summary>
            public readonly int Count;

            public EnemyGroup(EnemyTypeDef def, int count)
            {
                Def = def; Count = count;
            }
        }

        /// <summary>이 라운드에 저작된 변형 중 하나라도 보스 적을 포함하면 참. RNG 없이 정적으로 판단한다.</summary>
        private static bool RoundHasBoss(RoundDef round, GameData data)
        {
            foreach (var variant in round.Variants)
                foreach (var unit in variant.Units)
                    if (data.EnemyTypes.TryGetValue(unit.Type, out var def) && def.IsBoss)
                        return true;

            return false;
        }

        private void OnStartBattle()
        {
            UIScreenManager.HidePopup("StageIntro");
            UIScreenManager.ShowScreen("GamePlay");
        }

        private static RectTransform MakeRT(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static void SetRect(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);
        }

        private TMP_Text AddText(RectTransform rt, string text, int size, Color color, TextAlignmentOptions align)
        {
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = _fontAsset; t.fontSize = size; t.color = color; t.alignment = align; t.text = text;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Truncate;
            return t;
        }    }
}
