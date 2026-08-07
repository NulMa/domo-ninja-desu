using System.Collections.Generic;
using System.Text;
using DomoNinja.Core.Data;
using DomoNinja.Core.Economy;
using DomoNinja.Unity.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UImage = UnityEngine.UI.Image;

namespace DomoNinja.Unity
{
    /// <summary>
    /// 이번 라운드에 <b>무엇이 몇 마리 나오는지</b> 보여준다. 초상화에 올리면 스펙이 뜬다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 전에는 스테이지의 <b>라운드 8개를 가로로 늘어놓은 경로도</b>였다. 문제가 둘이었다 —
    /// (1) 각 칸이 보여주는 게 <c>AxisTested</c>("공격 리듬" 같은 <b>설계 메모</b>)라
    /// 플레이어가 할 수 있는 판단과 아무 상관이 없었고,
    /// (2) 칸이 화면 밖으로 넘쳐 가로 스크롤이 붙었는데 <b>스크롤이 있다는 표시가 없어서</b>
    /// 넘겨볼 수 있다는 걸 아무도 몰랐다.
    /// </para>
    /// <para>
    /// ★ <b>적 구성을 이제 보여줄 수 있다.</b> 예전 주석은 <c>PlayRound</c> 가 추첨과 전투를
    /// 한 덩어리로 갖고 있어 "미리 훔쳐보면 전투가 같이 끝난다"고 적어뒀는데,
    /// `D-75` 가 <see cref="RunEngine.PeekVariant"/> 로 그 둘을 갈라놨다.
    /// <see cref="RunManager.PeekVariant"/> 는 라운드당 한 번만 RNG 를 소비하고 캐시하므로
    /// 이 화면이 먼저 물어봐도 배치 화면이 같은 편성을 받는다.
    /// </para>
    /// <para>
    /// 라운드 진행도는 경로도 대신 <b>머리줄 한 줄</b>("라운드 3 / 8")로 줄였다.
    /// 8칸을 늘어놓아도 플레이어가 고를 수 있는 갈래가 없어 <b>지도가 아니라 눈금</b>이었다.
    /// </para>
    /// </remarks>
    public sealed class StageIntroController : MonoBehaviour
    {
        /// <summary>적 한 종류 카드의 고정 폭. 종류 수에 맞춰 줄어들지 않는다 — 초상화가 뭉개진다.</summary>
        private const float CardWidth = 200f;
        private const float NodeGap = 16f;

        // ★ 이 화면만 **단색 사각형**으로 그려져 있어서 나머지 UI 와 톤이 어긋났다.
        //   여기 값들은 이제 나무 테마 스프라이트 **위에 곱해지는 틴트**다.
        //   `Upcoming` 을 0.18 짜리 검정으로 두면 그림이 그냥 사라진다 — 밝기만 낮춘다.
        /// <summary>강조 — 보스 카드와 마릿수. 경로도 시절엔 "현재 라운드" 표시였다.</summary>
        private static readonly Color CurrentColor = new Color(1.00f, 0.85f, 0.55f);
        private static readonly Color UpcomingColor = new Color(0.62f, 0.60f, 0.56f);
        private static readonly Color TextMain = new Color(0.95f, 0.93f, 0.88f);
        private static readonly Color TextDim = new Color(0.74f, 0.71f, 0.66f);

        private const string NodeSpriteKey = "UI/Theme/inventory_cell";

        private RectTransform _container;
        private ScrollRect _scrollRect;
        private Button _startBattleButton;
        private SpriteCatalog _catalog;
        private TMP_FontAsset _fontAsset;

        /// <summary>"라운드 3 / 8   적 5체" 줄. 경로도 8칸을 대신한다 — 씬에 없어서 코드로 만든다.</summary>
        private TMP_Text _headerLabel;

        private void Awake()
        {
            var board = transform.Find("Board");
            _container = SetupScrollableContainer(board.Find("NodeContainer").GetComponent<RectTransform>(),
                                                    out _scrollRect);
            _startBattleButton = UITheme.EnsureButton(board.Find("StartBattleButton").gameObject);
            _startBattleButton.onClick.AddListener(OnStartBattle);

            _catalog = Resources.Load<SpriteCatalog>(SpriteCatalog.ResourceName);
            _fontAsset = TMP_Settings.defaultFontAsset;

            _headerLabel = BuildHeaderLabel(board);
        }

        /// <summary>뷰포트 바로 위에 머리줄을 얹는다. 씬 파일을 건드리지 않으려고 코드로 만든다(`19` §5.1).</summary>
        private TMP_Text BuildHeaderLabel(Transform board)
        {
            var viewport = (RectTransform)_container.parent;
            var rt = MakeRT("RoundHeader", board);
            rt.anchorMin = new Vector2(viewport.anchorMin.x, viewport.anchorMax.y);
            rt.anchorMax = new Vector2(viewport.anchorMax.x, viewport.anchorMax.y);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(viewport.anchoredPosition.x, viewport.anchoredPosition.y + 10f);
            rt.sizeDelta = new Vector2(viewport.sizeDelta.x, 36f);
            return AddText(rt, "", 22, TextMain, TextAlignmentOptions.Center);
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
            int totalRounds = mgr.Data.RoundsFor(stageId).Count;
            int currentRound = mgr.CurrentRun != null ? mgr.CurrentRun.Round : 1;

            // ★ 이 화면이 먼저 물어봐도 안전하다 — 라운드당 한 번만 뽑고 캐시한다(`D-75`).
            //   배치 화면이 뒤이어 물어보면 같은 편성을 받는다.
            var variant = mgr.PeekVariant();
            var lineup = Tally(variant, mgr.Data);

            SetHeader(currentRound, totalRounds, lineup);

            float rowHeight = _container.sizeDelta.y;
            float contentWidth = lineup.Count * CardWidth + Mathf.Max(0, lineup.Count - 1) * NodeGap;
            _container.sizeDelta = new Vector2(contentWidth, rowHeight);

            // 카드가 뷰포트보다 좁으면 가운데로 모은다 — 두세 종류뿐일 때 왼쪽에 몰려 있으면
            // "잘린 게 아닌가" 로 읽힌다.
            if (_scrollRect != null)
            {
                float viewportWidth = ((RectTransform)_scrollRect.transform).rect.width;
                _container.anchoredPosition = contentWidth < viewportWidth
                    ? new Vector2((viewportWidth - contentWidth) * 0.5f, _container.anchoredPosition.y)
                    : new Vector2(0f, _container.anchoredPosition.y);
                _scrollRect.horizontalNormalizedPosition = 0f;
            }

            for (int i = 0; i < lineup.Count; i++)
                BuildEnemyCard(lineup[i], i * (CardWidth + NodeGap), rowHeight);
        }

        /// <summary>같은 종류를 <b>한 칸으로 묶고 마릿수를 센다.</b> 등장 순서는 유지한다.</summary>
        /// <remarks>
        /// 슬라임 3마리를 칸 3개로 늘어놓으면 <b>종류가 셋인 것처럼 읽힌다.</b>
        /// 플레이어가 세는 단위는 "무엇이 몇"이지 "몇 번째 자리에 무엇"이 아니다 —
        /// 시작 좌표는 어차피 고정 위치가 아니다(`A5`).
        /// </remarks>
        private static List<EnemyGroup> Tally(VariantDef variant, GameData data)
        {
            var list = new List<EnemyGroup>();
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

        /// <summary>머리줄 — 라운드 눈금과 보스 여부. 경로도 8칸을 대신한다.</summary>
        private void SetHeader(int round, int totalRounds, List<EnemyGroup> lineup)
        {
            if (_headerLabel == null) return;

            bool boss = lineup.Exists(g => g.Def.IsBoss);
            int total = 0;
            foreach (var g in lineup) total += g.Count;

            var sb = new StringBuilder();
            sb.Append("라운드 ").Append(round);
            if (totalRounds > 0) sb.Append(" / ").Append(totalRounds);
            if (total > 0) sb.Append("      적 ").Append(total).Append("체");
            if (boss) sb.Append("      <color=#E0B252>보스</color>");
            _headerLabel.text = sb.ToString();
        }

        /// <summary>적 한 종류 카드 — 초상화 + <c>×마릿수</c>. 올리면 스펙이 뜬다.</summary>
        private void BuildEnemyCard(EnemyGroup group, float x, float height)
        {
            var def = group.Def;

            var card = MakeRT("Enemy_" + def.Type, _container);
            SetRect(card, x, 0, CardWidth, height);

            var cardImage = card.gameObject.AddComponent<UImage>();
            cardImage.sprite = UITheme.Find(NodeSpriteKey);
            cardImage.type = UImage.Type.Sliced;
            cardImage.pixelsPerUnitMultiplier = 1f;
            cardImage.color = def.IsBoss ? CurrentColor : UpcomingColor;

            // 초상화. 몸통 그림보다 초상(Faceset)이 칸 안에서 알아보기 쉽다.
            var icon = MakeRT("Icon", card);
            icon.anchorMin = new Vector2(0.5f, 1f);
            icon.anchorMax = new Vector2(0.5f, 1f);
            icon.pivot = new Vector2(0.5f, 1f);
            icon.anchoredPosition = new Vector2(0, -14);
            icon.sizeDelta = new Vector2(96, 96);
            var iconImg = icon.gameObject.AddComponent<UImage>();
            iconImg.sprite = FindPortrait(def);
            iconImg.preserveAspect = true;

            var nameLabel = MakeRT("NameLabel", card);
            nameLabel.anchorMin = new Vector2(0, 1);
            nameLabel.anchorMax = new Vector2(1, 1);
            nameLabel.pivot = new Vector2(0.5f, 1f);
            nameLabel.anchoredPosition = new Vector2(0, -116);
            nameLabel.sizeDelta = new Vector2(0, 26);
            AddText(nameLabel, def.Name, 16, def.IsBoss ? Color.white : TextMain, TextAlignmentOptions.Center);

            var countLabel = MakeRT("CountLabel", card);
            countLabel.anchorMin = new Vector2(0, 1);
            countLabel.anchorMax = new Vector2(1, 1);
            countLabel.pivot = new Vector2(0.5f, 1f);
            countLabel.anchoredPosition = new Vector2(0, -144);
            countLabel.sizeDelta = new Vector2(0, 30);
            AddText(countLabel, "×" + group.Count, 22, CurrentColor, TextAlignmentOptions.Center);

            // ★ 스펙은 카드에 적지 않고 툴팁으로 뺀다. 다섯 줄을 카드마다 박으면
            //   칸이 숫자로 가득 차서 "무엇이 몇 마리"라는 첫 질문이 도로 안 보인다.
            var hover = card.gameObject.AddComponent<HoverTooltipTrigger>();
            hover.Describe = () => UnitStatText.ForEnemy(def);

            var hint = MakeRT("HoverHint", card);
            hint.anchorMin = new Vector2(0, 0);
            hint.anchorMax = new Vector2(1, 0);
            hint.pivot = new Vector2(0.5f, 0f);
            hint.anchoredPosition = new Vector2(0, 8);
            hint.sizeDelta = new Vector2(0, 20);
            AddText(hint, "올려서 스펙 보기", 12, TextDim, TextAlignmentOptions.Center);
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
            public readonly int Count;

            public EnemyGroup(EnemyTypeDef def, int count)
            {
                Def = def; Count = count;
            }
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
