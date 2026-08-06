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
        private readonly List<MeshRenderer> _gridCells = new List<MeshRenderer>();
        private bool _suddenDeath;
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
            /// <summary>체력이 가득 찼을 때의 가로 배율. 스프라이트마다 원본 크기가 달라 미리 재둔다.</summary>
            public float FillFullScaleX;
            public Transform ShieldFill;
            public float ShieldFullScaleX;
            /// <summary>도발 대상 표시용. 유닛 스프라이트를 한 장 더 깔아 만든다.</summary>
            public SpriteRenderer Outline;
            /// <summary>번쩍임이 남은 시간(초).</summary>
            public float FlashLeft;
            public bool IsDead;
            public int MaxHp;
            public bool IsAlly;

            // ── 도트 애니메이션(캐릭터·보스 한정, `D-77`). 없으면(몬스터 등) 전부 null 이고
            //    초상 스프라이트가 그대로 정지 화면으로 남는다 — TickAnimations 가 건드리지 않는다.
            public Sprite[] IdleFrames;
            public Sprite[] AttackFrames;
            public float IdleFrameSeconds;
            public float AttackFrameSeconds;
            public int FrameIndex;
            public float FrameTimer;
            public bool IsAttacking;

            // ── 절차적 공격 연출(몬스터 등, 분리 프레임이 없는 종류). `IdleFrames == null` 일 때만 쓴다 —
            //    프레임 애니메이션이 있으면 그쪽이 우선이고 이 값들은 항상 기본값(0)으로 남는다.
            public float BaseSpriteScale = 1f;
            public Vector3 PunchDirection;
            public float PunchLeft;
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

                    // 에디터에서도 격자를 그릴 수 있어야 해서 Destroy 를 갈랐다.
                    // 편집 모드의 Object.Destroy 는 그 자리에서 실패하므로, 이 한 줄 때문에
                    // 보드 미리보기(BoardPreview) 가 통째로 못 돈다. 규칙은 아무것도 안 바뀐다.
                    var collider = cell.GetComponent<Collider>();
                    if (Application.isPlaying) Object.Destroy(collider);
                    else Object.DestroyImmediate(collider);

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
                    _gridCells.Add(renderer);
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
            renderer.sortingOrder = 0;

            // 애니메이션이 있는 종류(캐릭터·보스)는 Idle 첫 프레임으로 시작한다 — 없으면(몬스터 등)
            // 기존처럼 초상(Faceset) 정지 화면이다. 전투 재생(Setup)만 이 분기를 타고,
            // 배치 화면(CreatePlacementUnit)은 계속 초상만 쓴다 — 아직 "전투 중"이 아니라서다.
            Sprite[] idleFrames = null, attackFrames = null;
            float idleFrameSeconds = 0f, attackFrameSeconds = 0f;
            var animSpec = _catalog != null ? AnimSpecOf(spec.TypeId) : null;
            if (animSpec.HasValue)
            {
                idleFrames = LoadFrames(_catalog, animSpec.Value.IdleKey, animSpec.Value.IdleFrames);
                attackFrames = LoadFrames(_catalog, animSpec.Value.AttackKey, animSpec.Value.AttackFrames);
                idleFrameSeconds = animSpec.Value.IdleFrameSeconds;
                attackFrameSeconds = animSpec.Value.AttackFrameSeconds;
            }

            // 프레임 애니메이션이 없는 종류(몬스터 등)는 초상(Faceset) 대신 "몸통" 그림을 우선 쓴다 —
            // 절차적 공격 연출(확대+돌진)이 이 그림 위에서 돈다. 없으면(Body 색인이 안 된 종류) 기존 초상으로.
            Sprite bodySprite = idleFrames == null && _catalog != null
                ? _catalog.Find(ResolveSpritePath(spec.TypeId) + "/Body")
                : null;

            renderer.sprite = idleFrames != null
                ? idleFrames[0]
                : (bodySprite != null ? bodySprite : (_catalog != null ? _catalog.Find(ResolveSpritePath(spec.TypeId)) : null));

            float baseSpriteScale = 1f;
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
                baseSpriteScale = size.x > 0 ? CellSize * 0.8f / Mathf.Max(size.x, size.y) : 1f;
                spriteObject.transform.localScale = Vector3.one * baseSpriteScale;
            }

            var fill = CreateHpBar(root.transform, spec.Team == 0);
            var shield = CreateShieldBar(root.transform);
            var outline = CreateOutline(spriteObject.transform, renderer);

            return new UnitView
            {
                Root = root,
                Sprite = renderer,
                HpFill = fill,
                FillFullScaleX = fill != null ? fill.localScale.x : 1f,
                ShieldFill = shield,
                ShieldFullScaleX = shield != null ? shield.localScale.x : 1f,
                Outline = outline,
                MaxHp = spec.MaxHp,
                IsAlly = spec.Team == 0,
                IdleFrames = idleFrames,
                AttackFrames = attackFrames,
                IdleFrameSeconds = idleFrameSeconds,
                AttackFrameSeconds = attackFrameSeconds,
                BaseSpriteScale = baseSpriteScale,
            };
        }

        /// <summary>
        /// 유닛 종류 → 애니메이션 시트 명세. 캐릭터 6 + 보스 2 만 있다 — 몬스터는 방향별 분리 프레임이
        /// 없어 정지 초상으로 남는다(`D-77` 스코프 결정, "캐릭터 + 보스까지").
        /// </summary>
        /// <remarks>
        /// 키는 <see cref="SpriteCatalogBuilder"/> 가 슬라이싱해 색인한 <c>{경로}_{프레임번호}</c> 의 베이스다.
        /// 프레임 수를 여기 다시 적는 이유 — 카탈로그는 실제로 잘린 프레임 수만 알고, 런타임에서
        /// 몇 장을 이어붙일지는 View 의 연출 결정이라 카탈로그에 물어볼 수 없다.
        /// </remarks>
        private readonly struct AnimSpec
        {
            public readonly string IdleKey;
            public readonly int IdleFrames;
            public readonly float IdleFrameSeconds;
            public readonly string AttackKey;
            public readonly int AttackFrames;
            public readonly float AttackFrameSeconds;

            public AnimSpec(string idleKey, int idleFrames, float idleFrameSeconds,
                            string attackKey, int attackFrames, float attackFrameSeconds)
            {
                IdleKey = idleKey;
                IdleFrames = idleFrames;
                IdleFrameSeconds = idleFrameSeconds;
                AttackKey = attackKey;
                AttackFrames = attackFrames;
                AttackFrameSeconds = attackFrameSeconds;
            }
        }

        private static AnimSpec? AnimSpecOf(string typeId)
        {
            switch (typeId)
            {
                case "C1": return CharacterAnim("Samurai");
                case "C2": return CharacterAnim("Monk");
                case "C3": return CharacterAnim("NinjaRed");
                case "C4": return CharacterAnim("Hunter");
                case "C5": return CharacterAnim("NinjaMageBlack");
                case "C6": return CharacterAnim("Shaman");
                case "tenguRed":
                    return new AnimSpec("Actor/Boss/TenguRed/Idle", 6, 0.15f,
                                        "Actor/Boss/TenguRed/Attack", 15, 0.045f);
                case "giantFrog":
                    return new AnimSpec("Actor/Boss/GiantFrog/Idle40x40", 5, 0.15f,
                                        "Actor/Boss/GiantFrog/Attack", 3, 0.1f);
                default: return null;
            }
        }

        private static AnimSpec CharacterAnim(string folder) => new AnimSpec(
            $"Actor/Character/{folder}/SeparateAnim/Idle", 4, 0.15f,
            $"Actor/Character/{folder}/SeparateAnim/Attack", 4, 0.08f);

        /// <summary>프레임을 하나라도 못 찾으면 <c>null</c> — 애니메이션 전체를 포기하고 정지 초상으로 돌아간다.</summary>
        private static Sprite[] LoadFrames(SpriteCatalog catalog, string baseKey, int count)
        {
            var frames = new Sprite[count];
            for (int i = 0; i < count; i++)
            {
                frames[i] = catalog.Find($"{baseKey}_{i}");
                if (frames[i] == null) return null;
            }
            return frames;
        }

        /// <summary>
        /// 보호막 막대. 유닛 <b>위쪽</b>에 따로 얹는다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 체력 막대 옆이 아니라 반대편에 두는 이유 — 붙여 놓으면 두 막대가 한 막대로 읽혀서
        /// <b>"체력이 많은 유닛"과 "보호막을 두른 유닛"이 구분되지 않는다.</b>
        /// </para>
        /// <para>
        /// ★ 팩 스프라이트를 안 쓰고 사각형을 쓴다. <c>LifeBarMiniProgress</c> 는 <b>빨강</b>이라
        /// 청록으로 칠하면 곱해져서 탁한 자주가 된다 — 실제로 처음에 그렇게 나왔다.
        /// 스프라이트 틴트는 원본보다 밝아질 수 없다.
        /// </para>
        /// </remarks>
        private Transform CreateShieldBar(Transform parent)
        {
            var root = new GameObject("ShieldBar");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, 0.44f, -0.1f);

            float height = HpBarHeight * 0.75f;
            AddQuad(root.transform, "ShieldTrack", new Color(0.12f, 0.14f, 0.17f),
                    new Vector3(HpBarWidth + 0.04f, height + 0.04f, 1f), Vector3.zero);

            var fill = AddQuad(root.transform, "ShieldFill", new Color(0.44f, 0.85f, 1f),
                               new Vector3(HpBarWidth, height, 1f), new Vector3(0f, 0f, -0.01f));
            root.SetActive(false);
            return fill;
        }

        private static Transform AddQuad(Transform parent, string name, Color color, Vector3 scale, Vector3 localPos)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = localPos;
            quad.transform.localScale = scale;

            var collider = quad.GetComponent<Collider>();
            if (Application.isPlaying) Object.Destroy(collider); else Object.DestroyImmediate(collider);

            quad.GetComponent<MeshRenderer>().material =
                new Material(Shader.Find("Sprites/Default")) { color = color };
            return quad.transform;
        }

        /// <summary>
        /// 도발 표시용 외곽선. 유닛 스프라이트를 <b>조금 크게, 단색으로</b> 뒤에 한 장 더 깐다.
        /// </summary>
        private static SpriteRenderer CreateOutline(Transform spriteParent, SpriteRenderer source)
        {
            if (source == null || source.sprite == null) return null;

            var go = new GameObject("Outline");
            go.transform.SetParent(spriteParent, false);
            go.transform.localScale = Vector3.one * 1.3f;
            go.transform.localPosition = new Vector3(0f, 0f, 0.05f);

            var outline = go.AddComponent<SpriteRenderer>();
            outline.sprite = source.sprite;
            outline.color = new Color(1f, 0.75f, 0.2f);
            outline.sortingOrder = source.sortingOrder - 1;
            go.SetActive(false);
            return outline;
        }

        /// <summary>체력 막대의 월드 크기. 칸(1) 안에 들어가야 한다.</summary>
        /// <remarks>
        /// 높이를 0.14 로 잡았다가 화면에서 안 보여 0.22 로 올렸다.
        /// 원본이 18×4 인데 <b>위아래 1px 씩이 테두리</b>라 실제 색이 차는 건 절반뿐이다 —
        /// 원본 픽셀 수로 어림하면 두 배 얇게 나온다.
        /// </remarks>
        private const float HpBarWidth = 0.8f;
        private const float HpBarHeight = 0.22f;

        /// <summary>
        /// 체력 막대. <b>빈 칸(트랙) 위에 채움을 얹는다.</b>
        /// </summary>
        /// <remarks>
        /// <para>
        /// ★ 전에는 <b>단색 사각형 하나</b>였다. 문제가 둘이었다 —
        /// (1) 빈 칸이 없어서 <b>"체력이 절반"과 "최대 체력이 작은 유닛"이 같아 보였고</b>,
        /// (2) 사각형의 원점이 가운데라 체력이 줄면 <b>양쪽에서 동시에 줄어들었다.</b>
        /// 체력은 한쪽에서 닳아야 남은 양이 읽힌다.
        /// </para>
        /// <para>
        /// 팩의 <c>LifeBarMiniUnder</c>/<c>LifeBarMiniProgress</c>(18×4)가 정확히 이 용도로 들어 있다.
        /// 표에 없으면 <b>예전 사각형으로 떨어진다</b> — 막대가 사라지면 체력이 0 인 것과 구분되지 않는다.
        /// </para>
        /// </remarks>
        private Transform CreateHpBar(Transform parent, bool ally)
        {
            var root = new GameObject("HpBar");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, -0.38f, -0.1f);

            var trackSprite = _catalog != null ? _catalog.Find("UI/Bar/LifeBarMiniUnder") : null;
            var fillSprite = _catalog != null ? _catalog.Find("UI/Bar/LifeBarMiniProgress") : null;
            if (trackSprite == null || fillSprite == null) return CreateFallbackHpBar(root.transform, ally);

            AddBarPart(root.transform, "Track", trackSprite, Color.white, 1, Vector3.zero,
                       ScaleFor(trackSprite, HpBarWidth, HpBarHeight));

            var fillScale = ScaleFor(fillSprite, HpBarWidth, HpBarHeight);
            var fill = AddBarPart(root.transform, "Fill", fillSprite,
                                  ally ? new Color(0.45f, 0.95f, 0.5f) : new Color(1f, 0.45f, 0.42f),
                                  2, Vector3.zero, fillScale);
            return fill;
        }

        /// <summary>스프라이트 원본 크기를 목표 월드 크기로 맞추는 배율.</summary>
        private static Vector3 ScaleFor(Sprite sprite, float width, float height)
        {
            var size = sprite.bounds.size;
            return new Vector3(size.x > 0f ? width / size.x : 1f,
                               size.y > 0f ? height / size.y : 1f, 1f);
        }

        private static Transform AddBarPart(Transform parent, string name, Sprite sprite,
                                            Color color, int order, Vector3 localPos, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            return go.transform;
        }

        private static Transform CreateFallbackHpBar(Transform parent, bool ally)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bar.name = "Fill";
            bar.transform.SetParent(parent, false);
            bar.transform.localScale = new Vector3(HpBarWidth, HpBarHeight, 1f);

            var collider = bar.GetComponent<Collider>();
            if (Application.isPlaying) Object.Destroy(collider); else Object.DestroyImmediate(collider);

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
            renderer.sortingOrder = 0;

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
            SetFill(unit, ratio);
        }

        /// <summary>
        /// 채움을 <b>왼쪽 끝에 붙인 채로</b> 줄인다.
        /// </summary>
        /// <remarks>
        /// 스프라이트 원점이 가운데라 배율만 줄이면 양쪽에서 동시에 줄어든다.
        /// 줄어든 만큼 왼쪽으로 밀어 왼쪽 끝을 고정한다 — 스프라이트 피벗을 임포트 설정으로 바꾸는 방법도
        /// 있지만, 그러면 <b>이 코드가 왜 도는지가 인스펙터 값에 숨는다.</b>
        /// </remarks>
        private static void SetFill(UnitView unit, float ratio)
        {
            var scale = unit.HpFill.localScale;
            unit.HpFill.localScale = new Vector3(unit.FillFullScaleX * ratio, scale.y, scale.z);

            var pos = unit.HpFill.localPosition;
            unit.HpFill.localPosition = new Vector3(-HpBarWidth * 0.5f * (1f - ratio), pos.y, pos.z);
        }

        public void SetDead(int unitId)
        {
            if (!_units.TryGetValue(unitId, out var unit) || unit.Root == null) return;

            unit.IsDead = true;
            unit.FlashLeft = 0f;
            if (unit.Sprite != null) unit.Sprite.color = new Color(1f, 1f, 1f, 0.25f);
            // 빈 트랙은 남긴다 — 막대가 통째로 사라지면 "죽었다"와 "막대를 못 그렸다"가 같아 보인다.
            if (unit.HpFill != null) SetFill(unit, 0f);
            if (unit.ShieldFill != null) unit.ShieldFill.parent.gameObject.SetActive(false);
            if (unit.Outline != null) unit.Outline.gameObject.SetActive(false);
        }

        /// <summary>번쩍임이 눈에 남아 있는 시간(초).</summary>
        /// <remarks>
        /// ★ <b>전에는 한 프레임이었다.</b> 재생기가 매 <c>Update</c> 마다 색을 지우고 그 프레임의
        /// 이벤트만 다시 칠했기 때문에, 60fps 에서 번쩍임이 <b>16ms 만 보였다.</b>
        /// 사람 눈에는 안 보이는 시간이고, 그래서 "연출이 없다"가 아니라 <b>"연출이 있는데 안 보인다"</b> 였다.
        /// </remarks>
        private const float FlashSeconds = 0.18f;

        private static readonly Color AttackTint = new Color(1f, 0.95f, 0.55f);
        private static readonly Color DamageTint = new Color(1f, 0.42f, 0.38f);
        private static readonly Color HealTint = new Color(0.45f, 1f, 0.55f);

        /// <summary>공격하는 쪽을 번쩍인다. <b>맞는 쪽이 아니다</b> — 피격은 <see cref="FlashDamage"/> 가 맡는다.</summary>
        /// <remarks>
        /// 전에는 <c>Attack</c> 도 대상을 붉게 칠했다. 그러면 <c>Damage</c> 와 구분이 없어져
        /// <b>때린 것과 맞은 것이 같은 그림</b>이 된다. 휘두르는 쪽은 노란빛, 맞는 쪽은 붉은빛으로 나눈다.
        /// </remarks>
        public void FlashAttack(int actorId, int targetId)
        {
            Flash(actorId, AttackTint);
            PlayAttackAnimation(actorId, targetId);
        }

        /// <summary>
        /// 공격을 짧게 연출한다. Idle 로는 <see cref="TickAnimations"/> 가 알아서 되돌린다.
        /// </summary>
        /// <remarks>
        /// 분리 프레임이 있는 종류(캐릭터·보스)는 Attack 프레임으로 전환하고,
        /// 없는 종류(몬스터 등)는 대상 방향으로 확대+돌진하는 절차적 펀치로 대신한다 —
        /// 이 메서드가 <see cref="FlashAttack"/> 과 짝이라 항상 같이 불리므로, 여기서 갈라야
        /// <c>BattleReplayer</c> 가 유닛 종류를 알 필요가 없다.
        /// </remarks>
        private void PlayAttackAnimation(int actorId, int targetId)
        {
            if (!_units.TryGetValue(actorId, out var unit) || unit.IsDead) return;

            if (unit.AttackFrames != null)
            {
                unit.IsAttacking = true;
                unit.FrameIndex = 0;
                unit.FrameTimer = 0f;
                return;
            }

            var direction = unit.IsAlly ? Vector3.right : Vector3.left;
            if (_units.TryGetValue(targetId, out var target) && target.Root != null)
            {
                var delta = target.Root.transform.position - unit.Root.transform.position;
                if (delta.sqrMagnitude > 0.0001f) direction = delta.normalized;
            }

            unit.PunchDirection = direction;
            unit.PunchLeft = PunchSeconds;
        }

        /// <summary>절차적 펀치(확대+돌진)의 지속 시간(초).</summary>
        private const float PunchSeconds = 0.22f;
        private const float PunchScaleAmount = 0.22f;
        /// <summary>돌진 거리(월드 단위). 칸 크기(<see cref="CellSize"/>=1)에 비해 작게 잡는다 —
        /// 옆 칸까지 넘어가면 "이동"으로 읽혀서 <see cref="MoveTo"/> 이벤트와 헷갈린다.</summary>
        private const float PunchLungeDistance = 0.14f;

        /// <summary>피격.</summary>
        public void FlashDamage(int unitId) => Flash(unitId, DamageTint);

        /// <summary>회복. <b>피격과 반드시 달라야 한다</b> — 둘 다 체력 숫자만 바꾸면 화면에서 같은 사건이 된다.</summary>
        public void FlashHeal(int unitId) => Flash(unitId, HealTint);

        private void Flash(int unitId, Color tint)
        {
            if (!_units.TryGetValue(unitId, out var unit) || unit.Sprite == null) return;
            if (unit.IsDead) return;

            unit.Sprite.color = tint;
            unit.FlashLeft = FlashSeconds;
        }

        /// <summary>번쩍임을 시간에 따라 되돌린다. 재생기가 매 프레임 부른다.</summary>
        public void TickFlashes(float deltaTime)
        {
            foreach (var unit in _units.Values)
            {
                if (unit.Sprite == null || unit.FlashLeft <= 0f) continue;

                unit.FlashLeft -= deltaTime;
                float t = Mathf.Clamp01(unit.FlashLeft / FlashSeconds);
                unit.Sprite.color = Color.Lerp(Color.white, unit.Sprite.color, t);

                if (unit.FlashLeft <= 0f) unit.Sprite.color = Color.white;
            }
        }

        /// <summary>
        /// Idle 루프 · Attack 전환을 시간에 따라 진행한다. 재생기가 매 프레임 부른다(`TickFlashes` 와 짝).
        /// </summary>
        /// <remarks>
        /// 색(<c>Sprite.color</c>, 번쩍임)과 스프라이트(<c>Sprite.sprite</c>, 프레임)는 서로 다른 채널이라
        /// <see cref="TickFlashes"/> 와 간섭하지 않는다 — 공격 프레임이 도는 동안에도 피격 번쩍임이 같이 보인다.
        /// </remarks>
        public void TickAnimations(float deltaTime)
        {
            foreach (var unit in _units.Values)
            {
                if (unit.IsDead || unit.Sprite == null) continue;

                if (unit.IdleFrames != null)
                {
                    bool attacking = unit.IsAttacking && unit.AttackFrames != null;
                    var frames = attacking ? unit.AttackFrames : unit.IdleFrames;
                    float frameSeconds = attacking ? unit.AttackFrameSeconds : unit.IdleFrameSeconds;

                    unit.FrameTimer += deltaTime;
                    if (unit.FrameTimer >= frameSeconds)
                    {
                        unit.FrameTimer -= frameSeconds;
                        unit.FrameIndex++;

                        if (unit.FrameIndex >= frames.Length)
                        {
                            unit.FrameIndex = 0;
                            // 공격 재생이 한 바퀴 끝나면 Idle 로 돌아간다 — 계속 반복하면 "공격 중"이
                            // 실제 공격 이벤트보다 오래 보여서 재생 로그와 화면이 어긋난다.
                            if (attacking)
                            {
                                unit.IsAttacking = false;
                                frames = unit.IdleFrames;
                            }
                        }
                    }

                    unit.Sprite.sprite = frames[unit.FrameIndex];
                    continue;
                }

                if (unit.PunchLeft <= 0f) continue;

                unit.PunchLeft -= deltaTime;
                float t = Mathf.Clamp01(unit.PunchLeft / PunchSeconds);
                // 0 → 1 → 0 종 모양 곡선. 확대와 돌진이 같은 박자로 커졌다 줄어든다.
                float envelope = Mathf.Sin((1f - t) * Mathf.PI);

                var spriteTransform = unit.Sprite.transform;
                spriteTransform.localScale = Vector3.one * unit.BaseSpriteScale * (1f + PunchScaleAmount * envelope);
                spriteTransform.localPosition = unit.PunchDirection * (PunchLungeDistance * envelope);

                if (unit.PunchLeft <= 0f)
                {
                    spriteTransform.localScale = Vector3.one * unit.BaseSpriteScale;
                    spriteTransform.localPosition = Vector3.zero;
                }
            }
        }

        /// <summary>즉시 되돌린다. 되감기·정지처럼 시간이 이어지지 않는 경우에만 쓴다.</summary>
        public void ClearFlash()
        {
            foreach (var unit in _units.Values)
            {
                unit.FlashLeft = 0f;
                if (unit.Sprite != null && !unit.IsDead) unit.Sprite.color = Color.white;
            }
        }

        // ────────────────────────────── 보호막 · 상태

        /// <summary>
        /// 보호막을 체력 막대 위에 <b>따로</b> 그린다.
        /// </summary>
        /// <remarks>
        /// 체력에 더해 그리면 "체력이 많은 유닛"과 "보호막을 두른 유닛"이 같아 보인다.
        /// 폭은 최대 체력을 분모로 삼되 1.0 에서 자른다 — 보호막이 체력보다 클 수 있는데,
        /// 그때 막대가 칸 밖으로 나가면 보드가 읽히지 않는다.
        /// </remarks>
        public void SetShield(int unitId, int shield)
        {
            if (!_units.TryGetValue(unitId, out var unit) || unit.ShieldFill == null) return;

            float ratio = unit.MaxHp <= 0 ? 0f : Mathf.Clamp01((float)shield / unit.MaxHp);
            var scale = unit.ShieldFill.localScale;
            unit.ShieldFill.localScale = new Vector3(unit.ShieldFullScaleX * ratio, scale.y, scale.z);

            var pos = unit.ShieldFill.localPosition;
            unit.ShieldFill.localPosition = new Vector3(-HpBarWidth * 0.5f * (1f - ratio), pos.y, pos.z);

            // 채움만이 아니라 빈 칸까지 통째로 감춘다. 보호막이 없는 유닛 위에 빈 막대가 늘 떠 있으면
            // 그건 정보가 아니라 잡음이다 — 보드에 유닛이 12기면 잡음도 12개다.
            unit.ShieldFill.parent.gameObject.SetActive(shield > 0);
        }

        /// <summary>
        /// 도발(어그로) 대상을 <b>외곽선</b>으로 표시한다.
        /// </summary>
        /// <remarks>
        /// 아이콘을 얹지 않은 이유 — 어떤 아이콘을 쓸지는 연출 결정이고 팀원 몫이다.
        /// 외곽선은 <b>유닛 스프라이트를 그대로 한 장 더 깔아</b> 만들기 때문에 새 그림이 필요 없고,
        /// 나중에 아이콘이 정해져도 버릴 것이 없다.
        /// </remarks>
        public void SetTaunt(int unitId, bool on)
        {
            if (_units.TryGetValue(unitId, out var unit) && unit.Outline != null)
                unit.Outline.gameObject.SetActive(on);
        }

        /// <summary>
        /// 서든데스 진입. 판 전체를 붉게 물들여 <b>규칙이 바뀌었음</b>을 알린다.
        /// </summary>
        /// <remarks>
        /// 한 번만 적용한다. 같은 값이 여러 번 들어와도 색을 거듭 곱하면 판이 새빨개진다 —
        /// 이벤트는 한 번뿐이지만 되감기·재생이 들어오면 여러 번 불릴 수 있다.
        /// </remarks>
        public void SetSuddenDeath(bool on)
        {
            if (_suddenDeath == on) return;
            _suddenDeath = on;

            foreach (var cell in _gridCells)
            {
                if (cell == null) continue;
                cell.material.color = on
                    ? cell.material.color * SuddenDeathTint
                    : new Color(cell.material.color.r / SuddenDeathTint.r,
                                cell.material.color.g / SuddenDeathTint.g,
                                cell.material.color.b / SuddenDeathTint.b);
            }
        }

        private static readonly Color SuddenDeathTint = new Color(1.35f, 0.72f, 0.72f);

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
