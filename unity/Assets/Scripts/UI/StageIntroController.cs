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

        private static readonly Color CurrentColor = new Color(0.85f, 0.55f, 0.20f);
        private static readonly Color ClearedColor = new Color(0.28f, 0.40f, 0.30f);
        private static readonly Color UpcomingColor = new Color(0.18f, 0.19f, 0.21f);
        private static readonly Color ConnectorColor = new Color(0.45f, 0.45f, 0.48f);
        private static readonly Color TextMain = new Color(0.95f, 0.95f, 0.95f);
        private static readonly Color TextDim = new Color(0.72f, 0.72f, 0.72f);

        private RectTransform _container;
        private Button _startBattleButton;
        private SpriteCatalog _catalog;
        private TMP_FontAsset _fontAsset;

        private void Awake()
        {
            var board = transform.Find("Board");
            _container = board.Find("NodeContainer").GetComponent<RectTransform>();
            _startBattleButton = EnsureButton(board.Find("StartBattleButton").gameObject);
            _startBattleButton.onClick.AddListener(OnStartBattle);

            _catalog = Resources.Load<SpriteCatalog>(SpriteCatalog.ResourceName);
            _fontAsset = TMP_Settings.defaultFontAsset;
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

            float totalWidth = _container.sizeDelta.x;
            float nodeHeight = _container.sizeDelta.y;
            const float gap = 16f;
            float nodeWidth = (totalWidth - gap * (count - 1)) / count;

            for (int i = 0; i < count; i++)
            {
                float x = i * (nodeWidth + gap);
                if (i > 0) BuildConnector(x - gap, nodeHeight, gap);
                BuildNode(rounds[i], x, nodeWidth, nodeHeight, mgr.Data, i + 1, currentRound);
            }
        }

        private void BuildConnector(float x, float nodeHeight, float gap)
        {
            var rt = MakeRT("Connector", _container);
            SetRect(rt, x, nodeHeight / 2f - 2.5f, gap, 5f);
            rt.gameObject.AddComponent<UImage>().color = ConnectorColor;
        }

        private void BuildNode(RoundDef round, float x, float width, float height, GameData data,
                                int roundNumber, int currentRound)
        {
            bool boss = RoundHasBoss(round, data);
            bool current = roundNumber == currentRound;
            bool cleared = roundNumber < currentRound;

            var node = MakeRT("Node" + roundNumber, _container);
            SetRect(node, x, 0, width, height);
            node.gameObject.AddComponent<UImage>().color = current ? CurrentColor : cleared ? ClearedColor : UpcomingColor;

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

            var axisLabel = MakeRT("AxisLabel", node);
            axisLabel.anchorMin = new Vector2(0, 0);
            axisLabel.anchorMax = new Vector2(1, 1);
            axisLabel.offsetMin = new Vector2(8, 8);
            axisLabel.offsetMax = new Vector2(-8, -120);
            AddText(axisLabel, round.AxisTested, 11, TextDim, TextAlignmentOptions.Top);

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
        }

        private static Button EnsureButton(GameObject go)
        {
            var btn = go.GetComponent<Button>();
            if (btn == null) btn = go.AddComponent<Button>();
            var img = go.GetComponent<UImage>();
            if (img != null) btn.targetGraphic = img;
            return btn;
        }
    }
}
