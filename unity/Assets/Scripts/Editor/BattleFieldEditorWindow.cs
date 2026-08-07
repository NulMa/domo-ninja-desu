using System.Collections.Generic;
using System.IO;
using System.Linq;
using DomoNinja.Core.Domain;
using DomoNinja.Unity.View;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DomoNinja.Unity.Editor
{
    /// <summary>
    /// 전장 전용 편집기. <c>BattleField*.prefab</c> 을 더블클릭하면 여기서 열린다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 지시(사용자): *"별도 에디터 창에서 <c>BattleField_S1.prefab</c> 를 선택하면 열리고
    /// 편집할 수 있도록."*
    /// </para>
    /// <para>
    /// ★ <b>보드 방향 그대로 그린다.</b> 타일맵은 위로 갈수록 y 가 커지고 보드 좌표는
    /// 아래로 갈수록 <c>Y</c> 가 커진다 — 타일 팔레트로 칠할 때 이게 제일 헷갈리는 지점이고,
    /// "아군 진영에 칠했는데 적 진영에 나온다"가 여기서 나온다.
    /// 이 창은 <b>보드 좌표계로 보여주고</b> 저장할 때만 뒤집는다(<see cref="ToCell"/>).
    /// 화면에 보이는 위치가 게임에서 보이는 위치다.
    /// </para>
    /// <para>
    /// ★ <b>타일 에셋은 쓸 때 만든다.</b> 시트 6장을 통째로 <c>Tile</c> 로 뽑으면 2,600개가 넘는다.
    /// 커밋 히스토리가 심사 대상인 저장소라 쓰지도 않을 파일을 그만큼 올릴 이유가 없다 —
    /// 칠한 타일만 <c>Assets/Tilemaps/Tiles</c> 에 생긴다.
    /// </para>
    /// <para>
    /// 프리팹을 씬에 올리지 않고 <see cref="PrefabUtility.LoadPrefabContents"/> 로 직접 연다.
    /// 씬을 거치면 <b>실수로 씬에 남긴 채 저장</b>하는 사고가 나고, 그 씬은 2인이 같이 만진다(`19` §5.1).
    /// </para>
    /// </remarks>
    public sealed class BattleFieldEditorWindow : EditorWindow
    {
        private const string FieldFolder = "Assets/Resources";
        private const string SheetFolder = "Assets/Tilemaps/Textures";
        private const string TileFolder = "Assets/Tilemaps/Tiles";
        private const string FieldPrefix = "BattleField";

        /// <summary>
        /// 판 바깥으로 더 칠할 수 있는 여유 칸. <see cref="BoardView"/> 와 <b>같은 값을 쓴다.</b>
        /// </summary>
        /// <remarks>
        /// 여기서 따로 정하면 <b>편집기에서 칠한 자리가 게임에서 안 나오거나 그 반대</b>가 된다.
        /// 범위의 근거(화면비별 가시 범위)는 <see cref="BoardView.FieldMarginX"/> 에 적혀 있다.
        /// </remarks>
        private static int MarginX => BoardView.FieldMarginX;

        private static int MarginY => BoardView.FieldMarginY;

        private static readonly Color AllyZone = new Color(0.30f, 0.45f, 0.75f, 0.22f);
        private static readonly Color EnemyZone = new Color(0.75f, 0.32f, 0.32f, 0.22f);
        private static readonly Color OutsideZone = new Color(0f, 0f, 0f, 0.28f);
        private static readonly Color GridLine = new Color(1f, 1f, 1f, 0.14f);
        private static readonly Color BoardEdge = new Color(1f, 0.85f, 0.35f, 0.9f);

        // ── 열려 있는 전장
        private string _fieldPath;
        private GameObject _contents;
        private bool _dirty;

        /// <summary>
        /// 전장의 레이어들. <b>아래에서 위</b> 순서로 들고 있다(배경 → 오브젝트).
        /// </summary>
        /// <remarks>
        /// 칠하는 건 <see cref="_activeLayer"/> 한 장뿐이지만 <b>그리는 건 전부</b> 겹쳐 그린다 —
        /// 배경만 보이면 오브젝트를 어디에 놨는지 모르고, 오브젝트만 보이면 바닥과 안 맞는다.
        /// </remarks>
        private Tilemap[] _layers = new Tilemap[0];
        private int _activeLayer;

        private Tilemap ActiveMap =>
            _layers.Length > 0 && _activeLayer < _layers.Length ? _layers[_activeLayer] : null;

        // ── 팔레트
        private string _sheetPath;
        private Sprite[] _sheetSprites;
        private Sprite _brush;
        private Vector2 _paletteScroll;
        private float _zoom = 44f;

        [MenuItem("DomoNinja/전장 편집기")]
        public static void Open() => GetWindow<BattleFieldEditorWindow>("전장 편집기").minSize = new Vector2(900, 560);

        /// <summary>
        /// 프로젝트 창에서 전장 프리팹을 더블클릭하면 <b>프리팹 스테이지 대신</b> 이 창을 연다.
        /// </summary>
        /// <remarks>
        /// 이름이 <c>BattleField</c> 로 시작하는 것만 가로챈다 — 다른 프리팹까지 낚아채면
        /// <b>평범한 프리팹을 못 여는</b> 상태가 되고, 원인을 찾기 어렵다.
        /// </remarks>
        [OnOpenAsset]
        private static bool OnOpen(int instanceId, int line)
        {
            // Unity 6 에서 int 를 받는 옛 API 가 전부 폐기됐다(`EntityId` 로 교체) — 오브젝트를 거친다.
            string path = AssetDatabase.GetAssetPath(EditorUtility.EntityIdToObject(instanceId));
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab")) return false;
            if (!Path.GetFileNameWithoutExtension(path).StartsWith(FieldPrefix)) return false;

            var window = GetWindow<BattleFieldEditorWindow>("전장 편집기");
            window.minSize = new Vector2(900, 560);
            window.LoadField(path);
            return true;   // 우리가 처리했다 — 기본 동작(프리팹 스테이지)을 막는다
        }

        private void OnEnable()
        {
            if (_sheetPath == null) _sheetPath = FirstSheet();
            if (_fieldPath != null && _contents == null) LoadField(_fieldPath);
        }

        private void OnDisable() => CloseField(askToSave: true);

        // ─────────────────────────────────────────────── 전장 열기/저장

        private void LoadField(string path)
        {
            if (_fieldPath == path && _contents != null) return;

            CloseField(askToSave: true);

            _fieldPath = path;
            _contents = PrefabUtility.LoadPrefabContents(path);
            _layers = CollectLayers(_contents);
            _activeLayer = 0;
            _dirty = false;

            if (_layers.Length == 0)
                Debug.LogError($"[전장 편집기] {path} 에 Tilemap 이 없다. 'DomoNinja > 전장 타일맵 준비' 를 먼저 돌릴 것.");

            LoadSheet(_sheetPath);
            Repaint();
        }

        /// <summary>
        /// 전장의 타일맵들을 <b>렌더 순서(아래→위)</b>로 모은다.
        /// </summary>
        /// <remarks>
        /// 계층 순서가 아니라 <c>sortingOrder</c> 로 정렬한다 — 화면에 겹치는 순서를 정하는 건
        /// 그쪽이고, 계층 순서와 다를 수 있다. 편집기 미리보기가 게임과 다르게 보이면
        /// <b>편집기를 믿을 수 없게 된다.</b>
        /// </remarks>
        private static Tilemap[] CollectLayers(GameObject root) =>
            root.GetComponentsInChildren<Tilemap>(true)
                .OrderBy(m =>
                {
                    var r = m.GetComponent<TilemapRenderer>();
                    return r != null ? r.sortingOrder : 0;
                })
                .ToArray();

        private void CloseField(bool askToSave)
        {
            if (_contents == null) return;

            if (askToSave && _dirty &&
                EditorUtility.DisplayDialog("전장 편집기", $"{Path.GetFileName(_fieldPath)} 의 변경을 저장할까?", "저장", "버림"))
            {
                Save();
            }

            PrefabUtility.UnloadPrefabContents(_contents);
            _contents = null;
            _layers = new Tilemap[0];
            _dirty = false;
        }

        private void Save()
        {
            if (_contents == null || _fieldPath == null) return;

            PrefabUtility.SaveAsPrefabAsset(_contents, _fieldPath);
            AssetDatabase.SaveAssets();
            _dirty = false;
        }

        // ─────────────────────────────────────────────── 팔레트

        private static string FirstSheet() =>
            Directory.Exists(SheetFolder)
                ? Directory.GetFiles(SheetFolder, "*.png").Select(p => p.Replace('\\', '/')).FirstOrDefault()
                : null;

        private void LoadSheet(string path)
        {
            _sheetPath = path;
            _sheetSprites = string.IsNullOrEmpty(path)
                ? new Sprite[0]
                : AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
                    .OrderBy(s => s.name, System.StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// 이 스프라이트에 대응하는 <see cref="Tile"/> 을 가져오거나 <b>그 자리에서 만든다.</b>
        /// </summary>
        private static TileBase GetOrCreateTile(Sprite sprite)
        {
            string sheet = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(sprite));
            string dir = $"{TileFolder}/{sheet}";
            string path = $"{dir}/{sprite.name}.asset";

            var existing = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (existing != null) return existing;

            Directory.CreateDirectory(dir);
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            AssetDatabase.CreateAsset(tile, path);
            AssetDatabase.SaveAssets();
            return tile;
        }

        // ─────────────────────────────────────────────── 좌표 변환

        /// <summary>보드 좌표(위가 Y=0)를 타일맵 칸으로. <b>세로를 여기서만 뒤집는다.</b></summary>
        private static Vector3Int ToCell(int boardX, int boardY) =>
            new Vector3Int(boardX, Coord.BoardHeight - 1 - boardY, 0);

        // ─────────────────────────────────────────────── GUI

        private void OnGUI()
        {
            DrawToolbar();

            if (_contents == null || _layers.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "전장을 고르거나, 프로젝트 창에서 BattleField*.prefab 을 더블클릭하면 열린다.",
                    MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawPalette();
                DrawCanvas();
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var fields = Directory.Exists(FieldFolder)
                    ? Directory.GetFiles(FieldFolder, FieldPrefix + "*.prefab")
                        .Select(p => p.Replace('\\', '/')).ToArray()
                    : new string[0];

                int fieldIndex = Mathf.Max(0, System.Array.IndexOf(fields, _fieldPath));
                var names = fields.Select(Path.GetFileNameWithoutExtension).ToArray();

                if (names.Length > 0)
                {
                    int picked = EditorGUILayout.Popup(fieldIndex, names, EditorStyles.toolbarPopup, GUILayout.Width(200));
                    if (fields[picked] != _fieldPath) LoadField(fields[picked]);
                }

                var sheets = Directory.Exists(SheetFolder)
                    ? Directory.GetFiles(SheetFolder, "*.png").Select(p => p.Replace('\\', '/')).ToArray()
                    : new string[0];
                if (sheets.Length > 0)
                {
                    int si = Mathf.Max(0, System.Array.IndexOf(sheets, _sheetPath));
                    var sheetNames = sheets.Select(Path.GetFileNameWithoutExtension).ToArray();
                    int pickedSheet = EditorGUILayout.Popup(si, sheetNames, EditorStyles.toolbarPopup, GUILayout.Width(180));
                    if (sheets[pickedSheet] != _sheetPath) LoadSheet(sheets[pickedSheet]);
                }

                // ★ 어느 층에 칠하는지가 항상 보여야 한다 — 안 보이면 배경에 칠할 것을
                //   오브젝트 층에 칠해두고 나중에 배경만 지우려다 같이 지운다.
                if (_layers.Length > 1)
                {
                    GUILayout.Space(8);
                    var layerNames = _layers.Select(m => m.gameObject.name).ToArray();
                    _activeLayer = EditorGUILayout.Popup(_activeLayer, layerNames,
                                                         EditorStyles.toolbarPopup, GUILayout.Width(110));
                }

                GUILayout.Space(8);
                GUILayout.Label("확대", EditorStyles.miniLabel, GUILayout.Width(28));
                _zoom = GUILayout.HorizontalSlider(_zoom, 20f, 80f, GUILayout.Width(90));

                GUILayout.FlexibleSpace();

                GUILayout.Label(_brush != null ? $"붓: {_brush.name}" : "붓: 없음  (우클릭=지우기)",
                                EditorStyles.miniLabel);

                GUILayout.Space(8);
                if (GUILayout.Button("이 층 지우기", EditorStyles.toolbarButton))
                {
                    // 층 이름을 물음에 넣는다 — "전부 지울까?"만 뜨면 전장 통째로 지우는 줄 안다.
                    string layerName = ActiveMap != null ? ActiveMap.gameObject.name : "?";
                    if (EditorUtility.DisplayDialog("전장 편집기",
                            $"'{layerName}' 층의 타일을 전부 지울까? (다른 층은 그대로)", "지움", "취소"))
                    {
                        ActiveMap.ClearAllTiles();
                        _dirty = true;
                    }
                }

                using (new EditorGUI.DisabledScope(!_dirty))
                {
                    if (GUILayout.Button(_dirty ? "저장 *" : "저장", EditorStyles.toolbarButton, GUILayout.Width(70)))
                        Save();
                }
            }
        }

        private void DrawPalette()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(300)))
            {
                EditorGUILayout.LabelField("타일", EditorStyles.boldLabel);

                if (_sheetSprites == null || _sheetSprites.Length == 0)
                {
                    EditorGUILayout.HelpBox("시트가 없다. 'DomoNinja > 전장 타일맵 준비' 를 먼저 돌릴 것.",
                                            MessageType.Warning);
                    return;
                }

                _paletteScroll = EditorGUILayout.BeginScrollView(_paletteScroll);

                const float cell = 34f;
                int perRow = Mathf.Max(1, Mathf.FloorToInt(284f / cell));

                for (int i = 0; i < _sheetSprites.Length; i += perRow)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        for (int c = 0; c < perRow && i + c < _sheetSprites.Length; c++)
                        {
                            var sprite = _sheetSprites[i + c];
                            var rect = GUILayoutUtility.GetRect(cell, cell, GUILayout.Width(cell), GUILayout.Height(cell));

                            if (sprite == _brush) EditorGUI.DrawRect(rect, new Color(1f, 0.85f, 0.35f, 0.45f));
                            DrawSprite(rect, sprite);

                            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                            {
                                _brush = sprite;
                                Event.current.Use();
                                Repaint();
                            }
                        }
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawCanvas()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField(
                    $"{Path.GetFileNameWithoutExtension(_fieldPath)} — 왼쪽 파랑이 아군 진영, 화면 그대로가 게임 화면",
                    EditorStyles.boldLabel);

                int cols = Coord.BoardWidth + MarginX * 2;
                int rows = Coord.BoardHeight + MarginY * 2;

                var area = GUILayoutUtility.GetRect(cols * _zoom, rows * _zoom,
                                                    GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));

                for (int ry = 0; ry < rows; ry++)
                {
                    for (int rx = 0; rx < cols; rx++)
                    {
                        int boardX = rx - MarginX;
                        int boardY = ry - MarginY;
                        var rect = new Rect(area.x + rx * _zoom, area.y + ry * _zoom, _zoom, _zoom);

                        bool inBoard = boardX >= 0 && boardX < Coord.BoardWidth
                                       && boardY >= 0 && boardY < Coord.BoardHeight;

                        EditorGUI.DrawRect(rect, inBoard
                            ? (boardX <= Coord.AllyMaxX ? AllyZone : EnemyZone)
                            : OutsideZone);

                        // 레이어를 아래에서 위로 겹쳐 그린다 — 지금 칠하는 층만 보이면
                        // 배경과 오브젝트가 어떻게 맞물리는지 알 수 없다.
                        var cell = ToCell(boardX, boardY);
                        foreach (var map in _layers)
                        {
                            var tile = map.GetTile<Tile>(cell);
                            if (tile != null && tile.sprite != null) DrawSprite(rect, tile.sprite);
                        }

                        // 격자선
                        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), GridLine);
                        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), GridLine);

                        HandlePaint(rect, boardX, boardY);
                    }
                }

                // 보드 경계 강조 — 여기 안쪽이 실제로 유닛이 서는 칸이다.
                var board = new Rect(area.x + MarginX * _zoom, area.y + MarginY * _zoom,
                                     Coord.BoardWidth * _zoom, Coord.BoardHeight * _zoom);
                DrawOutline(board, BoardEdge);

                DrawAspectGuides(area);

                string active = ActiveMap != null ? ActiveMap.gameObject.name : "-";
                EditorGUILayout.HelpBox(
                    $"좌클릭 드래그 = 칠하기 · 우클릭 드래그 = 지우기   (지금 칠하는 층: {active})\n" +
                    "화면에는 모든 층이 겹쳐 보이지만 칠하는 건 고른 층 하나뿐이다.\n" +
                    "노란 테두리 = 8×6 보드(유닛이 서는 칸). 점선 = 화면비별로 실제 보이는 범위.\n" +
                    "16:9 선 안쪽은 반드시 칠할 것 — 심사자 창이 그보다 넓으면 21:9 선까지 보인다.",
                    MessageType.None);
            }
        }

        /// <summary>
        /// 화면비별로 <b>실제로 보이는 범위</b>를 겹쳐 그린다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 칠할 넓이를 눈대중으로 정하면 <b>넓은 창에서 가장자리가 빈다.</b> 실제로 그랬다 —
        /// 여유를 사방 한 줄로 뒀더니 16:9 에서도 좌우로 2.5칸씩 배경색이 남았다.
        /// </para>
        /// <para>
        /// 그래서 추측 대신 <see cref="BoardCamera.SizeFor"/> 를 <b>그대로 불러</b> 선을 긋는다.
        /// 카메라 공식이 바뀌면 이 선도 같이 움직인다 — 두 벌로 적으면 언젠가 어긋난다.
        /// </para>
        /// </remarks>
        private void DrawAspectGuides(Rect area)
        {
            // 왼쪽 위 칸의 화면 좌표가 보드 좌표 (−MarginX, −MarginY) 에 해당한다.
            // 월드 원점(판 중앙)은 거기서 MarginX·MarginY 칸만큼 안쪽이다.
            float originX = area.x + (MarginX + Coord.BoardWidth * 0.5f) * _zoom;
            float originY = area.y + (MarginY + Coord.BoardHeight * 0.5f) * _zoom;

            foreach (var (label, aspect, color) in new[]
            {
                ("16:9", 16f / 9f, new Color(0.45f, 0.85f, 1f, 0.85f)),
                ("21:9", 21f / 9f, new Color(0.55f, 1f, 0.6f, 0.7f)),
                ("9:16", 9f / 16f, new Color(1f, 0.6f, 0.9f, 0.7f)),
            })
            {
                float halfH = BoardCamera.SizeFor(aspect);
                float halfW = halfH * aspect;

                var rect = new Rect(originX - halfW * _zoom, originY - halfH * _zoom,
                                    halfW * 2f * _zoom, halfH * 2f * _zoom);
                DrawDashedOutline(rect, color);

                var labelRect = new Rect(rect.x + 3f, rect.y + 1f, 46f, 16f);
                var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = color } };
                GUI.Label(labelRect, label, style);
            }
        }

        /// <summary>칸 하나에 대한 마우스 처리. 드래그로 이어 칠할 수 있게 <c>MouseDrag</c> 도 받는다.</summary>
        private void HandlePaint(Rect rect, int boardX, int boardY)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown && e.type != EventType.MouseDrag) return;
            if (!rect.Contains(e.mousePosition)) return;

            var cell = ToCell(boardX, boardY);

            if (e.button == 1)
            {
                ActiveMap.SetTile(cell, null);
                _dirty = true;
            }
            else if (e.button == 0 && _brush != null)
            {
                ActiveMap.SetTile(cell, GetOrCreateTile(_brush));
                _dirty = true;
            }
            else return;

            e.Use();
            Repaint();
        }

        // ─────────────────────────────────────────────── 그리기 도우미

        /// <summary>아틀라스 안의 한 조각만 잘라 그린다. 스프라이트는 시트를 공유하므로 UV 로 집는다.</summary>
        private static void DrawSprite(Rect rect, Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return;

            var r = sprite.rect;
            var tex = sprite.texture;
            var uv = new Rect(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height);
            GUI.DrawTextureWithTexCoords(rect, tex, uv, alphaBlend: true);
        }

        /// <summary>점선 테두리. 실선(보드 경계)과 구분돼야 "판"과 "보이는 범위"가 안 헷갈린다.</summary>
        private static void DrawDashedOutline(Rect rect, Color color)
        {
            const float dash = 6f;
            const float gap = 5f;
            const float t = 1.5f;

            for (float x = rect.x; x < rect.xMax; x += dash + gap)
            {
                float w = Mathf.Min(dash, rect.xMax - x);
                EditorGUI.DrawRect(new Rect(x, rect.y, w, t), color);
                EditorGUI.DrawRect(new Rect(x, rect.yMax - t, w, t), color);
            }

            for (float y = rect.y; y < rect.yMax; y += dash + gap)
            {
                float h = Mathf.Min(dash, rect.yMax - y);
                EditorGUI.DrawRect(new Rect(rect.x, y, t, h), color);
                EditorGUI.DrawRect(new Rect(rect.xMax - t, y, t, h), color);
            }
        }

        private static void DrawOutline(Rect rect, Color color)
        {
            const float t = 2f;
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, t), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - t, rect.width, t), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, t, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - t, rect.y, t, rect.height), color);
        }
    }
}
