using System.Collections.Generic;
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

        /// <summary>화면에 올라와 있는 유닛 1체.</summary>
        private sealed class UnitView
        {
            public GameObject Root;
            public SpriteRenderer Sprite;
            public Transform HpFill;
            public int MaxHp;
            public bool IsAlly;
        }

        public void Initialize(SpriteCatalog catalog)
        {
            _catalog = catalog;
            BuildGrid();

            _unitRoot = new GameObject("Units").transform;
            _unitRoot.SetParent(transform, false);
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
            renderer.sprite = _catalog != null ? _catalog.Find(SpritePathOf(spec.TypeId)) : null;
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
        /// 유닛 종류 → 스프라이트 폴더.
        /// </summary>
        /// <remarks>
        /// ⚠️ 임시 구현이다. 데이터의 <c>sprite</c> 필드를 이벤트 로그가 싣고 오지 않아서
        /// 지금은 이름으로 추측한다. 로그 포맷 v2 에서 <c>UnitSpec</c> 에 스프라이트 경로를
        /// 넣거나, View 가 <c>GameData</c> 를 함께 받는 쪽으로 정리해야 한다 —
        /// D+4 포맷 리뷰 안건이다.
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
