using System.Collections.Generic;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Events;
using UnityEngine;

namespace DomoNinja.Unity.View
{
    /// <summary>
    /// 8×6 보드와 그 위의 유닛을 그린다. <b>코드로 만든다 — 씬에 미리 배치하지 않는다.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// `19` §5.1 이 "빈 씬 1개 + 코드 생성"으로 정해뒀다.
    /// 씬 파일에 오브젝트를 늘어놓으면 <b>병합할 수 없는 바이너리가 커지고</b>,
    /// 2인이 같은 씬을 건드리는 순간 충돌이 나는데 씬 충돌은 손으로 못 푼다.
    /// </para>
    /// <para>
    /// ★ <b>이 클래스는 게임 규칙을 하나도 모른다.</b>
    /// 좌표를 받아 놓고, 체력 숫자를 받아 바를 그린다. 계산은 core 가 이미 끝냈다 —
    /// View 가 다시 계산하면 그게 규칙의 두 번째 사본이 되고 언젠가 갈라진다(`23` §2.1).
    /// </para>
    /// </remarks>
    public sealed class BoardView : MonoBehaviour
    {
        /// <summary>칸 하나의 월드 크기.</summary>
        private const float CellSize = 1f;

        private readonly Dictionary<int, UnitView> _units = new Dictionary<int, UnitView>();
        private SpriteCatalog _catalog;
        private Transform _unitRoot;

        /// <summary>
        /// 유닛 종류 → 스프라이트 경로. <b>데이터가 준 그대로다</b> — View 가 추측하지 않는다.
        /// </summary>
        private IReadOnlyDictionary<string, string> _spritePaths;

        /// <summary>화면에 올라와 있는 유닛 1체.</summary>
        private sealed class UnitView
        {
            public GameObject Root;
            public SpriteRenderer Sprite;
            public Transform HpFill;
            public int MaxHp;
            public bool IsAlly;
        }

        /// <param name="spritePaths">
        /// 유닛 종류 → 스프라이트 경로. <see cref="SpritePathsFrom"/> 로 만든다.
        /// <b><c>null</c> 이면 이름으로 추측하는 옛 규칙으로 떨어진다</b> — 그 경로는 적 4종을 놓친다.
        /// </param>
        public void Initialize(SpriteCatalog catalog,
                               IReadOnlyDictionary<string, string> spritePaths = null)
        {
            _catalog = catalog;
            _spritePaths = spritePaths;
            BuildGrid();

            _unitRoot = new GameObject("Units").transform;
            _unitRoot.SetParent(transform, false);
        }

        /// <summary>
        /// <see cref="GameData"/> 가 들고 있는 스프라이트 경로를 그대로 뽑는다.
        /// </summary>
        /// <remarks>
        /// ★ <b>이 함수가 있어야 하는 이유가 D+4 에 실측으로 드러났다.</b>
        /// 전에는 View 가 <c>typeId</c> 첫 글자를 대문자로 바꿔 경로를 <b>추측</b>했는데,
        /// 데이터의 실제 이름과 <b>적 4종이 어긋난다</b> —
        /// <c>bat→BlueBat</c> · <c>kappa→KappaGreen</c> · <c>lantern→LanternRed</c> · <c>trex→TRex</c>.
        /// <para>
        /// 그 유닛들은 <b>자리표시자로 떴고 콘솔에는 아무 에러도 안 났다.</b>
        /// 화면을 실제로 띄워보기 전에는 안 드러나는 종류이고,
        /// 매핑을 <c>encounters.json</c> 에 둔 이유가 바로 <b>이 추측을 없애는 것</b>이었다.
        /// </para>
        /// </remarks>
        public static Dictionary<string, string> SpritePathsFrom(GameData data)
        {
            var map = new Dictionary<string, string>();
            if (data == null) return map;

            foreach (var c in data.Characters)
                if (!string.IsNullOrEmpty(c.Sprite)) map[c.Id] = c.Sprite;

            foreach (var kv in data.EnemyTypes)
                if (!string.IsNullOrEmpty(kv.Value.Sprite)) map[kv.Key] = kv.Value.Sprite;

            return map;
        }

        /// <summary>격자. 아군 진영과 적 진영을 색으로 나눈다.</summary>
        private void BuildGrid()
        {
            var root = new GameObject("Grid").transform;
            root.SetParent(transform, false);

            for (int y = 0; y < Coord.BoardHeight; y++)
            {
                for (int x = 0; x < Coord.BoardWidth; x++)
                {
                    var cell = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    cell.name = $"Cell_{x}_{y}";
                    cell.transform.SetParent(root, false);
                    cell.transform.position = ToWorld(new Coord(x, y)) + new Vector3(0, 0, 1f);
                    cell.transform.localScale = Vector3.one * (CellSize * 0.96f);

                    Object.Destroy(cell.GetComponent<Collider>());

                    // 아군 진영(x <= AllyMaxX)과 적 진영을 눈으로 구분한다.
                    // 배치 규칙이 화면에 안 보이면 "왜 여기 못 놓지"가 버그처럼 보인다.
                    bool ally = x <= Coord.AllyMaxX;
                    bool dark = (x + y) % 2 == 0;
                    var color = ally
                        ? new Color(0.16f, 0.20f, 0.28f)
                        : new Color(0.26f, 0.17f, 0.19f);
                    if (dark) color *= 0.82f;

                    var renderer = cell.GetComponent<MeshRenderer>();
                    renderer.material = new Material(Shader.Find("Sprites/Default")) { color = color };
                }
            }
        }

        /// <summary>전투 시작. 헤더의 유닛 명세로 보드를 채운다.</summary>
        public void Setup(BattleLog log)
        {
            Clear();

            foreach (var spec in log.Units)
            {
                _units[spec.UnitId] = CreateUnit(spec);
            }
        }

        public void Clear()
        {
            foreach (var unit in _units.Values)
            {
                if (unit.Root != null) Object.Destroy(unit.Root);
            }
            _units.Clear();
        }

        private UnitView CreateUnit(UnitSpec spec)
        {
            var root = new GameObject($"{spec.TypeId}#{spec.UnitId}");
            root.transform.SetParent(_unitRoot, false);
            root.transform.position = ToWorld(FromKey(spec.StartCoordKey));

            var spriteObject = new GameObject("Sprite");
            spriteObject.transform.SetParent(root.transform, false);

            var renderer = spriteObject.AddComponent<SpriteRenderer>();
            renderer.sprite = _catalog != null ? _catalog.Find(ResolveSpritePath(spec.TypeId)) : null;
            renderer.sortingOrder = 10;

            if (renderer.sprite == null)
            {
                // ★ 안 보이게 두면 "스프라이트가 없는 것"과 "유닛이 안 만들어진 것"이 구분되지 않는다.
                spriteObject.AddComponent<SpriteRenderer>();
                var placeholder = GameObject.CreatePrimitive(PrimitiveType.Quad);
                placeholder.transform.SetParent(root.transform, false);
                placeholder.transform.localScale = Vector3.one * 0.5f;
                Object.Destroy(placeholder.GetComponent<Collider>());
                placeholder.GetComponent<MeshRenderer>().material =
                    new Material(Shader.Find("Sprites/Default")) { color = Color.magenta };
            }
            else
            {
                // 초상 크기가 제각각이라 칸에 맞춘다.
                var size = renderer.sprite.bounds.size;
                float scale = size.x > 0 ? CellSize * 0.8f / Mathf.Max(size.x, size.y) : 1f;
                spriteObject.transform.localScale = Vector3.one * scale;
            }

            return new UnitView
            {
                Root = root,
                Sprite = renderer,
                HpFill = CreateHpBar(root.transform, spec.Team == 0),
                MaxHp = spec.MaxHp,
                IsAlly = spec.Team == 0,
            };
        }

        private static Transform CreateHpBar(Transform parent, bool ally)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bar.name = "HpFill";
            bar.transform.SetParent(parent, false);
            bar.transform.localPosition = new Vector3(0, -0.42f, -0.1f);
            bar.transform.localScale = new Vector3(0.8f, 0.1f, 1f);
            Object.Destroy(bar.GetComponent<Collider>());

            bar.GetComponent<MeshRenderer>().material = new Material(Shader.Find("Sprites/Default"))
            {
                color = ally ? new Color(0.35f, 0.85f, 0.45f) : new Color(0.9f, 0.35f, 0.35f),
            };

            return bar.transform;
        }

        // ────────────────────────────── 배치 조정 (`08` §5-5, `D-53`, `D-75`)

        /// <summary>배치 화면에 세운 스프라이트 하나. 전투 규칙을 모르고 좌표·색만 갖는다.</summary>
        private sealed class PlacementUnit
        {
            public Transform Root;
            public SpriteRenderer Sprite;
        }

        private static readonly Color PlacementSelectedColor = new Color(1f, 0.92f, 0.35f);

        private Transform _placementRoot;
        private readonly Dictionary<string, PlacementUnit> _placementAllies = new Dictionary<string, PlacementUnit>();

        /// <summary>
        /// 배치 조정 화면을 세운다 — 아군은 <paramref name="allyPlacement"/> 좌표에, 적은 공개된 좌표에 선다.
        /// </summary>
        /// <remarks>
        /// 전투 재생용 유닛(<see cref="Setup"/>)과는 <b>별개 트리다.</b> 여기 세운 스프라이트는
        /// <see cref="PlacementController"/> 가 좌표만 옮기고, 실제 판정은 "전투 시작" 시점에 core 가 한다.
        /// </remarks>
        public void ShowPlacementPreview(IReadOnlyDictionary<string, Coord> allyPlacement,
                                         IReadOnlyList<EnemyPlacement> enemies)
        {
            ClearPlacementPreview();

            _placementRoot = new GameObject("PlacementPreview").transform;
            _placementRoot.SetParent(transform, false);

            foreach (var kv in allyPlacement)
                _placementAllies[kv.Key] = CreatePlacementUnit(kv.Key, kv.Value);

            foreach (var e in enemies)
                CreatePlacementUnit(e.Type, e.At);
        }

        public void ClearPlacementPreview()
        {
            if (_placementRoot != null) Object.Destroy(_placementRoot.gameObject);
            _placementRoot = null;
            _placementAllies.Clear();
        }

        private PlacementUnit CreatePlacementUnit(string typeId, Coord at)
        {
            var root = new GameObject(typeId);
            root.transform.SetParent(_placementRoot, false);
            root.transform.position = ToWorld(at);

            var renderer = root.AddComponent<SpriteRenderer>();
            renderer.sprite = _catalog != null ? _catalog.Find(ResolveSpritePath(typeId)) : null;
            renderer.sortingOrder = 10;

            if (renderer.sprite == null)
            {
                // ★ CreateUnit 과 같은 이유 — 안 보이게 두면 "스프라이트가 없는 것"과
                //   "유닛이 안 만들어진 것"이 구분되지 않는다.
                var placeholder = GameObject.CreatePrimitive(PrimitiveType.Quad);
                placeholder.transform.SetParent(root.transform, false);
                placeholder.transform.localScale = Vector3.one * 0.5f;
                Object.Destroy(placeholder.GetComponent<Collider>());
                placeholder.GetComponent<MeshRenderer>().material =
                    new Material(Shader.Find("Sprites/Default")) { color = Color.magenta };
            }
            else
            {
                var size = renderer.sprite.bounds.size;
                float scale = size.x > 0 ? CellSize * 0.8f / Mathf.Max(size.x, size.y) : 1f;
                root.transform.localScale = Vector3.one * scale;
            }

            return new PlacementUnit { Root = root.transform, Sprite = renderer };
        }

        /// <summary>격자 칸으로 스냅 이동. 드롭 확정 · 되돌림 둘 다 이걸 쓴다.</summary>
        public void MoveAllyPreview(string characterId, Coord to)
        {
            if (_placementAllies.TryGetValue(characterId, out var u)) u.Root.position = ToWorld(to);
        }

        /// <summary>드래그 중 커서를 자유롭게 따라간다 — 격자에 스냅하지 않는다.</summary>
        public void SetAllyPreviewFreePosition(string characterId, Vector3 worldPos)
        {
            if (_placementAllies.TryGetValue(characterId, out var u))
                u.Root.position = new Vector3(worldPos.x, worldPos.y, u.Root.position.z);
        }

        public void SetPlacementSelected(string characterId)
        {
            foreach (var kv in _placementAllies)
                if (kv.Value.Sprite != null)
                    kv.Value.Sprite.color = kv.Key == characterId ? PlacementSelectedColor : Color.white;
        }

        /// <summary>월드 좌표를 보드 칸으로 되돌린다. <see cref="ToWorld"/> 의 역함수다.</summary>
        public static Coord CoordAt(Vector3 world) => new Coord(
            Mathf.RoundToInt(world.x + (Coord.BoardWidth - 1) * 0.5f),
            Mathf.RoundToInt((Coord.BoardHeight - 1) * 0.5f - world.y));

        // ────────────────────────────── 이벤트 반영

        public void MoveTo(int unitId, int coordKey)
        {
            if (_units.TryGetValue(unitId, out var unit) && unit.Root != null)
                unit.Root.transform.position = ToWorld(FromKey(coordKey));
        }

        /// <summary><paramref name="hp"/> 는 <b>core 가 계산해 보낸 적용 후 값</b>이다. 여기서 빼지 않는다.</summary>
        public void SetHp(int unitId, int hp)
        {
            if (!_units.TryGetValue(unitId, out var unit) || unit.HpFill == null) return;

            float ratio = unit.MaxHp <= 0 ? 0f : Mathf.Clamp01((float)hp / unit.MaxHp);
            var scale = unit.HpFill.localScale;
            unit.HpFill.localScale = new Vector3(0.8f * ratio, scale.y, scale.z);
        }

        public void SetDead(int unitId)
        {
            if (!_units.TryGetValue(unitId, out var unit) || unit.Root == null) return;

            if (unit.Sprite != null) unit.Sprite.color = new Color(1f, 1f, 1f, 0.25f);
            if (unit.HpFill != null) unit.HpFill.localScale = new Vector3(0f, 0.1f, 1f);
        }

        /// <summary>공격 순간을 눈에 띄게. 연출이지 규칙이 아니다.</summary>
        public void FlashAttack(int actorId, int targetId)
        {
            if (_units.TryGetValue(targetId, out var target) && target.Sprite != null)
                target.Sprite.color = new Color(1f, 0.6f, 0.6f);
        }

        public void ClearFlash()
        {
            foreach (var unit in _units.Values)
            {
                if (unit.Sprite != null && unit.Sprite.color.a > 0.5f)
                    unit.Sprite.color = Color.white;
            }
        }

        // ────────────────────────────── 좌표

        /// <summary>보드 가운데가 원점이 되게 놓는다.</summary>
        public static Vector3 ToWorld(Coord c) => new Vector3(
            (c.X - (Coord.BoardWidth - 1) * 0.5f) * CellSize,
            ((Coord.BoardHeight - 1) * 0.5f - c.Y) * CellSize,
            0f);

        /// <summary>이벤트가 싣고 오는 좌표키를 되돌린다 (`23` §3).</summary>
        public static Coord FromKey(int orderKey) =>
            new Coord(orderKey % Coord.BoardWidth, orderKey / Coord.BoardWidth);

        /// <summary>
        /// 유닛 종류 → 스프라이트 경로. <b>데이터에 있으면 그걸 쓴다.</b>
        /// </summary>
        /// <remarks>
        /// ★ D+4 포맷 리뷰 결론 — <b>이벤트 로그를 늘리지 않고 View 가 <c>GameData</c> 를 받는 쪽</b>으로 정했다.
        /// 로그의 <c>UnitSpec</c> 에 스프라이트 경로를 실으면 <b>전투당 수만 건의 로그에 같은 문자열이 반복</b>되고,
        /// 동결한 포맷(`23`)도 건드려야 한다. 스프라이트는 <b>연출의 정보</b>지 전투의 정보가 아니다.
        /// </remarks>
        private string ResolveSpritePath(string typeId)
        {
            if (_spritePaths != null && _spritePaths.TryGetValue(typeId, out string path))
                return path;

            return SpritePathOf(typeId);
        }

        /// <summary>
        /// 데이터가 없을 때의 <b>대비책</b>. 이름으로 추측한다.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>이 규칙은 적 4종을 놓친다</b> — <c>bat</c>·<c>kappa</c>·<c>lantern</c>·<c>trex</c>.
        /// 그래서 <see cref="Initialize"/> 에 <c>spritePaths</c> 를 넘기는 게 정상 경로이고,
        /// 여기는 더미 로그 재생처럼 <c>GameData</c> 가 없는 경우만 쓴다.
        /// </remarks>
        public static string SpritePathOf(string typeId)
        {
            switch (typeId)
            {
                case "C1": return "Actor/Character/Samurai";
                case "C2": return "Actor/Character/Monk";
                case "C3": return "Actor/Character/NinjaRed";
                case "C4": return "Actor/Character/Hunter";
                case "C5": return "Actor/Character/NinjaMageBlack";
                case "C6": return "Actor/Character/Shaman";
                case "tenguRed": return "Actor/Boss/TenguRed";
                case "giantFrog": return "Actor/Boss/GiantFrog";
                case "totem": return "Actor/Character/Statue";
                default: return "Actor/Monster/" + char.ToUpperInvariant(typeId[0]) + typeId.Substring(1);
            }
        }
    }
}
