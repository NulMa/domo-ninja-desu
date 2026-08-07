using System.Collections.Generic;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Events;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

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

        /// <summary>타격 이펙트 프레임 캐시. 키는 카탈로그 베이스 키("FX/Hit/Samurai/SpriteSheet" 등) —
        /// 공격자 종류마다 다른 그림을 쓰므로(<see cref="HitFxSpecFor"/>) 하나가 아니라 사전으로 캐싱한다.</summary>
        private readonly Dictionary<string, Sprite[]> _hitFxFrameCache = new Dictionary<string, Sprite[]>();
        private readonly List<HitFx> _hitFxInstances = new List<HitFx>();

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

            /// <summary>현재 체력. <see cref="SetHp"/> 가 받은 값을 그대로 둔다 — 툴팁이 읽는다.</summary>
            public int Hp;

            public bool IsAlly;
            /// <summary>유닛 종류(`C1`.. · 몬스터/보스 typeId). 공격자별 타격 이펙트를 고를 때 쓴다.</summary>
            public string TypeId;

            // ── 도트 애니메이션(캐릭터·보스 한정, `D-77`). 없으면(몬스터 등) 전부 null 이고
            //    초상 스프라이트가 그대로 정지 화면으로 남는다 — TickAnimations 가 건드리지 않는다.
            public Sprite[] IdleFrames;
            public Sprite[] AttackFrames;
            public float IdleFrameSeconds;
            public float AttackFrameSeconds;
            public int FrameIndex;
            public float FrameTimer;
            public bool IsAttacking;

            /// <summary>true면 Idle 두 프레임을 순환 대신 <see cref="FacingAway"/> 로 골라 쓴다(`AnimSpec.HasDirectionalIdle`).</summary>
            public bool HasDirectionalIdle;
            /// <summary>위(뒤)를 보고 있으면 true — Idle의 1번(뒤통수) 프레임을 쓴다. 기본 false(정면).</summary>
            public bool FacingAway;

            // ── 절차적 공격 연출(몬스터 등, 분리 프레임이 없는 종류). `IdleFrames == null` 일 때만 쓴다 —
            //    프레임 애니메이션이 있으면 그쪽이 우선이고 이 값들은 항상 기본값(0)으로 남는다.
            public float BaseSpriteScale = 1f;
            public Vector3 PunchDirection;
            public float PunchLeft;

            // ── 칸 이동 슬라이드(`MoveTo`). 텔레포트 대신 짧게 미끄러지듯 보여준다.
            public Vector3 MoveFrom;
            public Vector3 MoveTarget;
            public float MoveLeft;

            /// <summary>승리 환호가 시작될 때 서 있던 높이. 뛰었다가 여기로 돌아온다.</summary>
            public float CheerBaseY;
        }

        /// <summary>화면에 재생 중인 타격 이펙트 1개. 유닛에 안 매여 있다 — 재생이 끝나면 스스로 사라진다.</summary>
        private sealed class HitFx
        {
            public Transform Transform;
            public SpriteRenderer Renderer;
            /// <summary>이 인스턴스가 재생 중인 프레임 배열. 공격자마다 달라 인스턴스별로 들고 있는다.</summary>
            public Sprite[] Frames;
            public int FrameIndex;
            public float FrameTimer;
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

        /// <summary>바닥 타일 한 장(16×16). 팩의 <c>TilesetFloor</c> 에서 흙 타일만 잘라 별도 에셋으로 뒀다.</summary>
        private const string GroundTileKey = "Backgrounds/Ground";

        /// <summary>
        /// 판 바깥으로 더 깔아야 하는 칸 수. <b>화면이 판보다 넓다.</b>
        /// </summary>
        /// <remarks>
        /// <para>
        /// ★ 전에는 <b>사방 한 줄</b>이었는데 근거 없는 값이었다. <see cref="BoardCamera"/> 는
        /// 판 8×6 이 어떤 창 비율에서도 들어오게 잡을 뿐, <b>남는 자리를 채우지는 않는다</b> —
        /// 그래서 넓은 창일수록 좌우에 배경색만 남았다.
        /// </para>
        /// <para>
        /// 실제 가시 범위(<c>orthographicSize = max(4.2, 4.2/aspect)</c>) 기준:
        /// </para>
        /// <list type="bullet">
        ///   <item>16:9 → x ±7.47 · y ±4.2</item>
        ///   <item>21:9 → x ±9.8 · y ±4.2</item>
        ///   <item>9:16(세로) → x ±4.2 · y ±7.47</item>
        /// </list>
        /// <para>
        /// 판 절반이 x 4.0 · y 3.0 이므로, 여유 <c>X=6 / Y=5</c> 면 x ±10 · y ±8 까지 덮어
        /// <b>21:9 가로와 9:16 세로를 모두 채운다.</b> 그 이상(32:9 같은)은 포기한다 —
        /// 칸 수가 제곱으로 늘어 손으로 칠할 양이 감당이 안 된다.
        /// </para>
        /// </remarks>
        public const int FieldMarginX = 6;

        /// <inheritdoc cref="FieldMarginX"/>
        public const int FieldMarginY = 5;

        /// <summary>
        /// 판 뒤에 <b>흙바닥</b>을 깐다. 칸마다 한 장씩 놓아 타일이 이어지게 한다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 지시(사용자): "배틀씬은 가지고 있는 에셋들에서 배경으로 배치 가능한 타일맵들을
        /// 장식으로 쓰면 좋을 것 같음."
        /// </para>
        /// <para>
        /// ★ <b>타일셋을 통째로 잘라 색인하지 않았다.</b> <c>TilesetFloor.png</c> 는 352×417 이라
        /// 세로가 16 의 배수가 아니고(26칸 + 1px), 그대로 슬라이스하면 <b>칸 번호가 한 줄씩 밀린다</b> —
        /// 그러면 "몇 번이 흙인가"가 임포트 설정에 매달린 값이 된다.
        /// 필요한 타일 한 장만 잘라 <c>Backgrounds/Ground.png</c> 로 뒀다.
        /// </para>
        /// <para>
        /// 격자 칸(<see cref="_gridCells"/>)은 <b>그대로 위에 남는다.</b> 진영 구분 색이 사라지면
        /// "왜 여기 못 놓지"가 다시 버그처럼 보인다 — 바닥은 그 아래에 깔릴 뿐이다.
        /// </para>
        /// </remarks>
        /// <summary>
        /// 사람이 <b>타일 팔레트로 그린</b> 전장. 있으면 절차적 바닥 대신 이걸 쓴다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>Resources</c> 에서 <b>이름으로</b> 찾는다. 스테이지·보스마다 다른 전장을 쓸 수 있게
        /// <b>구체적인 것부터</b> 훑고, 없으면 한 단계씩 일반적인 쪽으로 떨어진다 —
        /// 그려둔 게 없어도 판이 맨바닥으로 보이면 안 된다.
        /// </para>
        /// <list type="number">
        ///   <item><c>BattleField_{스테이지}_Boss</c> — 그 스테이지의 <b>보스 라운드</b> 전용</item>
        ///   <item><c>BattleField_{스테이지}</c> — 그 스테이지 공용</item>
        ///   <item><c>BattleField</c> — 전 스테이지 기본</item>
        ///   <item>없으면 <see cref="BuildGround"/> 의 절차적 흙바닥</item>
        /// </list>
        /// <para>
        /// ★ 무한 모드는 <b>여기에 아무것도 더 안 붙여도 된다.</b> 스테이지 id 를 순서대로
        /// 돌려주기만 하면 이 표가 그대로 돈다 — 전장 선택 규칙을 모드별로 또 만들 이유가 없다.
        /// </para>
        /// </remarks>
        private const string FieldPrefabPrefix = "BattleField";

        private Transform _fieldRoot;

        /// <summary>지금 깔린 전장이 어느 조합으로 뽑힌 것인지. 같은 값이면 다시 안 세운다.</summary>
        private string _fieldKey;

        /// <summary>
        /// 이 라운드에 맞는 전장을 깐다. 라운드가 바뀔 때마다 부르면 된다.
        /// </summary>
        /// <param name="stageId">스테이지 id(<c>S1</c>..). <c>null</c> 이면 기본 전장.</param>
        /// <param name="bossRound">보스가 나오는 라운드인가. 참이면 보스 전용 전장을 먼저 찾는다.</param>
        public void SetField(string stageId, bool bossRound)
        {
            string key = $"{stageId}/{bossRound}";
            if (_fieldKey == key && _fieldRoot != null) return;
            _fieldKey = key;

            if (_fieldRoot != null) Object.Destroy(_fieldRoot.gameObject);
            _fieldRoot = null;

            var prefab = FindFieldPrefab(stageId, bossRound);
            if (prefab != null)
            {
                var field = Object.Instantiate(prefab, transform);
                field.name = prefab.name;
                // 프리팹이 이미 보드 원점 기준으로 놓여 있다 — 부모에 붙일 때 그 좌표를 지킨다.
                field.transform.localPosition = prefab.transform.position;
                _fieldRoot = field.transform;
                return;
            }

            BuildGround();
        }

        /// <summary>
        /// 그 전장에 <b>한 칸이라도 칠해져 있는가.</b>
        /// </summary>
        /// <remarks>
        /// ★ 빈 전장을 그대로 쓰면 <b>판이 맨바닥으로 나온다.</b> 준비 메뉴가 스테이지·보스별
        /// 빈 프리팹을 미리 깔아두기 때문에(칠할 자리를 보여주려고), 이 검사가 없으면
        /// <b>아직 안 칠한 스테이지가 기본 전장보다 우선</b>해서 배경이 사라진다 —
        /// 파일을 만들어둔 것이 오히려 화면을 망가뜨리는 셈이다.
        /// 비어 있으면 없는 것으로 치고 다음 단계로 떨어진다.
        /// </remarks>
        private static bool HasAnyTile(GameObject fieldPrefab)
        {
            foreach (var map in fieldPrefab.GetComponentsInChildren<Tilemap>(true))
                if (map.GetUsedTilesCount() > 0) return true;

            return false;
        }

        /// <summary>
        /// 구체적인 것부터 훑어 <b>실제로 칠해진</b> 전장을 찾는다. 없으면 <c>null</c>.
        /// </summary>
        /// <remarks>
        /// ★ <b>"파일이 있는가"가 아니라 "타일이 있는가"로 고른다.</b>
        /// 준비 메뉴가 스테이지·보스별 <b>빈</b> 프리팹을 13개 미리 깔아두기 때문에,
        /// 파일 존재만 보면 <c>BattleField_S1</c> 이 비어 있어도 먼저 잡혀서
        /// <b>정작 칠해둔 기본 전장(<c>BattleField</c>)을 건너뛴다.</b>
        /// 실제로 그렇게 돼 있었다 — 기본 전장만 칠하면 게임에 안 나오는 상태였다.
        /// 빈 칸은 "아직 안 만든 것"이지 "비우기로 한 것"이 아니므로 다음 후보로 넘어간다.
        /// </remarks>
        private static GameObject FindFieldPrefab(string stageId, bool bossRound)
        {
            if (!string.IsNullOrEmpty(stageId))
            {
                if (bossRound)
                {
                    var boss = LoadIfPainted($"{FieldPrefabPrefix}_{stageId}_Boss");
                    if (boss != null) return boss;
                }

                var stage = LoadIfPainted($"{FieldPrefabPrefix}_{stageId}");
                if (stage != null) return stage;
            }

            return LoadIfPainted(FieldPrefabPrefix);
        }

        /// <summary>이름으로 찾되, <b>타일이 한 장도 없으면 없는 것으로 친다.</b></summary>
        private static GameObject LoadIfPainted(string resourceName)
        {
            var prefab = Resources.Load<GameObject>(resourceName);
            return prefab != null && HasAnyTile(prefab) ? prefab : null;
        }

        private void BuildGround()
        {
            var tile = _catalog != null ? _catalog.Find(GroundTileKey) : null;
            if (tile == null) return;

            var root = new GameObject("Ground").transform;
            root.SetParent(transform, false);

            // ★ 이것도 "지금 깔린 전장"이다. 안 맡겨두면 `SetField` 가 다음에 지울 대상을 못 찾아
            //   라운드마다 흙바닥이 80장씩 쌓인다 — 실제로 첫 실측에서 Ground 가 둘이었다.
            _fieldRoot = root;

            // 칸 하나를 꽉 채우도록 배율을 맞춘다. 16px 타일이라 원본 월드 크기는 1 이 아니다.
            var size = tile.bounds.size;
            float scale = size.x > 0f ? CellSize / size.x : 1f;

            for (int y = -FieldMarginY; y < Coord.BoardHeight + FieldMarginY; y++)
            {
                for (int x = -FieldMarginX; x < Coord.BoardWidth + FieldMarginX; x++)
                {
                    var go = new GameObject($"Ground_{x}_{y}");
                    go.transform.SetParent(root, false);

                    // ★ 판 밖 좌표를 <b>직접</b> 계산한다. 전에는 좌표를 판 안으로 clamp 한 뒤
                    //   차이만큼 밀었는데, **세로 부호를 빠뜨려 위아래가 접혔다** —
                    //   `ToWorld` 는 보드 Y 가 커질수록 월드 y 가 작아지는데 밀어주는 값은
                    //   그대로 더했기 때문이다. 여유가 한 줄일 땐 한 줄이 겹치는 정도라
                    //   눈에 안 띄었고, 5줄로 늘리자 세로가 ±3 에서 안 자라는 것으로 드러났다.
                    go.transform.position = new Vector3(
                        (x - (Coord.BoardWidth - 1) * 0.5f) * CellSize,
                        ((Coord.BoardHeight - 1) * 0.5f - y) * CellSize,
                        1.5f);
                    go.transform.localScale = Vector3.one * scale;

                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = tile;
                    sr.sortingOrder = -20;
                    // ★ 어둡게 칠하지 않는다(사용자 지시). 전에는 판 안 0.72 · 바깥 0.45 로 깔아
                    //   가장자리를 죽였는데, 그림을 그대로 보여주는 편이 낫다 —
                    //   어둡게 하는 건 그린 배경을 쓰기 시작하면 더더욱 방해가 된다.
                }
            }
        }

        /// <summary>격자. 아군 진영과 적 진영을 색으로 나눈다.</summary>
        private void BuildGrid()
        {
            // 기본 전장으로 시작한다. 스테이지가 정해지면 화면이 `SetField` 로 갈아끼운다.
            SetField(null, false);

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

                    // ★ 진영 구분은 남기되 <b>어둡게 덮지 않는다</b>(사용자 지시).
                    //   전에는 어두운 남색·자주를 alpha 0.55 로 덮어서, 배경을 아무리 잘 그려도
                    //   판 위만 그늘진 것처럼 보였다. 밝은 색을 옅게 얹어 **색조만** 남긴다 —
                    //   구분이 사라지면 "왜 여기 못 놓지"가 다시 버그처럼 보인다.
                    var color = ally
                        ? new Color(0.55f, 0.75f, 1.00f)
                        : new Color(1.00f, 0.62f, 0.58f);
                    if (dark) color *= 0.92f;
                    color.a = 0.18f;

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

            foreach (var fx in _hitFxInstances)
            {
                if (fx.Transform != null) Object.Destroy(fx.Transform.gameObject);
            }
            _hitFxInstances.Clear();
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

            // 진영 쪽을 보게 좌우 반전한다. 보드가 x축으로 진영이 갈려 있어(`Coord.AllyMaxX`) 아군은
            // 오른쪽(적 방향), 적은 왼쪽(아군 방향)을 보면 서로 마주보는 것으로 읽힌다. `flipX` 는
            // scale 과 별개 채널이라 절차적 펀치(확대+돌진)·타격 시 스케일 변화와 안 부딪힌다.
            renderer.flipX = spec.Team != 0;

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
                Hp = spec.MaxHp,
                IsAlly = spec.Team == 0,
                TypeId = spec.TypeId,
                IdleFrames = idleFrames,
                AttackFrames = attackFrames,
                IdleFrameSeconds = idleFrameSeconds,
                AttackFrameSeconds = attackFrameSeconds,
                HasDirectionalIdle = animSpec?.HasDirectionalIdle ?? false,
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

            /// <summary>
            /// true면 Idle의 두 프레임을 <b>애니메이션이 아니라 방향 포즈</b>로 쓴다 —
            /// 0번=정면(아래), 1번=뒤통수(위)를 <see cref="UnitView.FacingAway"/> 에 따라 골라 보여줄 뿐
            /// 시간에 따라 순환하지 않는다. 보스처럼 진짜 숨쉬기·깜빡임 루프가 있는 쪽은 false로 두고
            /// 기존 프레임 순환을 그대로 쓴다.
            /// </summary>
            public readonly bool HasDirectionalIdle;

            public AnimSpec(string idleKey, int idleFrames, float idleFrameSeconds,
                            string attackKey, int attackFrames, float attackFrameSeconds,
                            bool hasDirectionalIdle = false)
            {
                IdleKey = idleKey;
                IdleFrames = idleFrames;
                IdleFrameSeconds = idleFrameSeconds;
                AttackKey = attackKey;
                AttackFrames = attackFrames;
                AttackFrameSeconds = attackFrameSeconds;
                HasDirectionalIdle = hasDirectionalIdle;
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

        /// <remarks>
        /// Attack은 프레임 1장만 쓴다. 원본 4프레임은 <c>정면 → 뒤통수 → 옆모습 → 정면</c> 순으로
        /// 캐릭터가 한 바퀴 돌아보는 원화다(6종 전수 확인, `D-77` 후속) — 이 게임은 옆에서 본 전신이
        /// 아니라 고정 카메라의 흉상 초상이라 그 턴이 "동작"이 아니라 "제자리에서 도는 것"으로 읽힌다.
        /// <para>
        /// Idle은 프레임 2장(정면·뒤통수)을 <b>순환이 아니라 방향 포즈</b>로 재활용한다 — 이 팩엔
        /// 상하좌우 전용 그림이 없어서(`D-77` 후속 조사, `SpriteSheet.png` 통합 시트도 방향이 아니라
        /// 동작별 행이었다) 새로 그릴 게 아니면 이미 슬라이스된 두 장이 유일한 재료다. 뒤통수 프레임을
        /// "위(뒤)를 본다"로, 정면 프레임을 "아래(정면)"로 쓴다. 좌우는 이 두 프레임에 flipX 를 얹어 낸다.
        /// </para>
        /// </remarks>
        private static AnimSpec CharacterAnim(string folder) => new AnimSpec(
            $"Actor/Character/{folder}/SeparateAnim/Idle", 2, 0.18f,
            $"Actor/Character/{folder}/SeparateAnim/Attack", 1, 0.18f,
            hasDirectionalIdle: true);

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
            // 체력 막대를 발밑으로 내린 만큼(`BarBaseY`) 이쪽도 머리 위로 대칭 이동한다 — 0.44 에선
            // 막대(높이 0.165)의 아래끝이 +0.36 이라 그림 윗부분(+0.4까지)을 물고 있었다.
            root.transform.localPosition = new Vector3(0f, -BarBaseY, -0.1f);

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
            outline.flipX = source.flipX; // 본체와 반전이 어긋나면 뒤집힌 실루엣이 삐져나와 보인다.
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
        /// 체력·보호막 막대를 유닛의 <b>발밑 바깥</b>에 둘 높이.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 유닛 그림은 <c>CellSize * 0.8</c> 로 맞춰지므로 칸 중심 기준 <b>−0.4 ~ +0.4</b> 를 차지한다.
        /// 막대가 −0.38 에 있었는데 막대 높이가 0.22 라 <b>−0.49 ~ −0.27</b> 을 덮었다 —
        /// 그림의 아래쪽 몸통을 정확히 가린 것이다.
        /// </para>
        /// <para>
        /// ★ 그래서 <b>도트 애니메이션을 넣었는데 머리만 움직이는 것처럼 보였다.</b> 이 팩의 Idle 은
        /// 정면/뒤통수 두 포즈를 오가는 그림이라 <b>차이가 어깨선 아래에서 가장 크게 난다</b> —
        /// 하필 막대가 덮고 있던 자리다. 연출이 없는 게 아니라 <b>가려져 있었다.</b>
        /// </para>
        /// <para>
        /// 그림 아래끝(−0.4)에서 막대 절반(0.11)과 여백(0.04)만큼 더 내린다. 칸(±0.5)을 조금
        /// 넘지만 <see cref="ToWorld"/> 의 칸 간격이 1 이라 아랫줄 그림(−0.4 부터 시작)과는 안 겹친다.
        /// </para>
        /// </remarks>
        private const float BarBaseY = -0.55f;

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
            root.transform.localPosition = new Vector3(0f, BarBaseY, -0.1f);

            var trackSprite = _catalog != null ? _catalog.Find("UI/Bar/LifeBarMiniUnder") : null;

            // ★ 아군은 **초록으로 칠한 별도 스프라이트**를 쓴다. 틴트로는 못 만든다 —
            //   `LifeBarMiniProgress` 의 채움 픽셀이 빨강(224,57,76)이라 초록(0.45,0.95,0.5)을
            //   곱하면 (101,54,38) 즉 **탁한 갈색**이 된다. 실제로 그렇게 나와 있었다.
            //   `CreateShieldBar` 주석이 이미 같은 함정을 적어뒀는데(스프라이트 틴트는 원본보다
            //   밝아질 수 없다) 이쪽은 안 고쳐져 있었다 — 적군은 빨강×빨강이라 멀쩡해서
            //   **한쪽만 틀린 상태가 눈에 안 띄었다.**
            string fillKey = ally ? "UI/Bar/LifeBarMiniProgressAlly" : "UI/Bar/LifeBarMiniProgress";
            var fillSprite = _catalog != null ? _catalog.Find(fillKey) : null;
            if (trackSprite == null || fillSprite == null) return CreateFallbackHpBar(root.transform, ally);

            AddBarPart(root.transform, "Track", trackSprite, Color.white, 1, Vector3.zero,
                       ScaleFor(trackSprite, HpBarWidth, HpBarHeight));

            // 아군은 스프라이트가 이미 제 색이라 흰색(=원본 그대로), 적군만 붉은 쪽으로 살짝 민다.
            var fillScale = ScaleFor(fillSprite, HpBarWidth, HpBarHeight);
            var fill = AddBarPart(root.transform, "Fill", fillSprite,
                                  ally ? Color.white : new Color(1f, 0.45f, 0.42f),
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

        /// <summary>슬라이드 지속 시간(초). <see cref="TickMovement"/> 가 재생 배속과 무관하게 이 시간 동안 미끄러진다
        /// — <see cref="FlashSeconds"/>·<see cref="PunchSeconds"/> 와 같은 실시간 연출값이다.</summary>
        private const float MoveSeconds = 0.12f;

        /// <summary>
        /// 칸을 옮긴다. <b>텔레포트가 아니라 슬라이드다</b> — 순간이동이면 보드 위에서 "누가 움직였는지"를
        /// 놓치기 쉽다(`23` 재생 로그를 눈으로 따라가려면 이동이 눈에 보여야 한다).
        /// </summary>
        /// <remarks>
        /// 이전 슬라이드가 끝나기 전에 다음 <c>Move</c> 이벤트가 오면(배속을 올렸을 때 흔하다)
        /// <b>그 순간의 실제 화면 위치</b>에서 새로 출발한다 — 저장해둔 이전 목적지에서 다시 시작하면
        /// 그 사이 화면에 없던 구간을 순간이동으로 메우게 된다.
        /// </remarks>
        public void MoveTo(int unitId, int coordKey)
        {
            if (!_units.TryGetValue(unitId, out var unit) || unit.Root == null) return;

            unit.MoveFrom = unit.Root.transform.position;
            unit.MoveTarget = ToWorld(FromKey(coordKey));
            unit.MoveLeft = MoveSeconds;
            UpdateFacing(unit, unit.MoveTarget);
        }

        /// <summary>이동 슬라이드를 시간에 따라 진행한다. 재생기가 매 프레임 부른다.</summary>
        public void TickMovement(float deltaTime)
        {
            foreach (var unit in _units.Values)
            {
                if (unit.MoveLeft <= 0f || unit.Root == null) continue;

                unit.MoveLeft -= deltaTime;
                float t = 1f - Mathf.Clamp01(unit.MoveLeft / MoveSeconds);
                float eased = 1f - (1f - t) * (1f - t); // ease-out — 도착 직전에 멈칫하며 안착한다.
                unit.Root.transform.position = Vector3.Lerp(unit.MoveFrom, unit.MoveTarget, eased);

                if (unit.MoveLeft <= 0f) unit.Root.transform.position = unit.MoveTarget;
            }
        }

        /// <summary><paramref name="hp"/> 는 <b>core 가 계산해 보낸 적용 후 값</b>이다. 여기서 빼지 않는다.</summary>
        public void SetHp(int unitId, int hp)
        {
            if (!_units.TryGetValue(unitId, out var unit) || unit.HpFill == null) return;

            // 툴팁이 "체력 32 / 40" 을 적으려면 현재 값이 필요하다. 막대 배율에서 되돌리면
            // 반올림 때문에 원래 숫자가 안 나온다 — 받은 값을 그대로 들고 있는다.
            unit.Hp = hp;

            float ratio = unit.MaxHp <= 0 ? 0f : Mathf.Clamp01((float)hp / unit.MaxHp);
            SetFill(unit, ratio);
        }

        // ─────────────────────────────────────────────────────────────
        //  마우스를 올린 유닛의 스펙 (`UnitTooltip`)
        // ─────────────────────────────────────────────────────────────

        /// <summary>마우스 아래 있는 것. <see cref="TypeId"/> 가 <c>null</c> 이면 아무것도 없다.</summary>
        public readonly struct HoverTarget
        {
            /// <summary>아군이면 캐릭터 id(<c>C1</c>..), 적이면 적 타입 키. 없으면 <c>null</c>.</summary>
            public readonly string TypeId;
            public readonly bool IsAlly;

            /// <summary><see cref="HasLiveHp"/> 가 참일 때만 의미 있다. 배치 화면엔 아직 전투 체력이 없다.</summary>
            public readonly int Hp;
            public readonly int MaxHp;

            /// <summary>전투 재생 중이라 현재 체력을 아는가. 배치 미리보기면 거짓.</summary>
            public readonly bool HasLiveHp;

            public HoverTarget(string typeId, bool isAlly, int hp, int maxHp, bool hasLiveHp)
            {
                TypeId = typeId; IsAlly = isAlly; Hp = hp; MaxHp = maxHp; HasLiveHp = hasLiveHp;
            }
        }

        /// <summary>
        /// 지금 마우스가 무엇 위에 있는가. <b>배치 화면과 전투 화면 모두</b>에서 답한다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// ★ <b>여기서 툴팁을 띄우지 않는다.</b> 툴팁(<c>UnitTooltip</c>)과 그 내용에 필요한 런 상태는
        /// 둘 다 <c>DomoNinja.Unity.UI</c> 에 있고 그 어셈블리가 이미 <c>View</c> 를 참조한다 —
        /// 여기서 UI 를 부르면 <b>참조가 순환한다.</b> 이 뷰는 "무엇에 올려져 있는가"까지만 답한다.
        /// </para>
        /// <para>
        /// ★ 전투 유닛(<see cref="_units"/>)과 배치 미리보기(<see cref="_placementRoot"/>)는
        /// <b>별개 트리다.</b> 전투 것만 보면 <b>정작 배치를 고민하는 동안 스펙을 못 본다</b> —
        /// 적을 보고 자리를 정하는 화면이 바로 그 화면이다. 둘 다 훑는다.
        /// </para>
        /// </remarks>
        public HoverTarget Hovered { get; private set; }

        /// <summary>마우스가 유닛 중심에서 이 거리(월드) 안에 있으면 그 유닛으로 친다. 칸이 1 이라 절반이 경계다.</summary>
        private const float HoverRadius = 0.45f;

        private void Update()
        {
            Hovered = FindHovered();
            TickCheer();
        }

        private void OnDisable() => Hovered = default;

        // ─────────────────────────────────────────────────────────────
        //  승리 환호 — 전투가 "끝났다"는 신호
        // ─────────────────────────────────────────────────────────────

        /// <summary>환호가 남은 시간(초). 0 이면 아무것도 안 한다.</summary>
        private float _cheerLeft;

        /// <summary>환호 총 길이. <see cref="StartVictoryCheer"/> 를 부른 쪽이 이만큼은 기다려야 한다.</summary>
        public const float VictoryCheerSeconds = 0.9f;

        private const float CheerHopHeight = 0.18f;
        private const float CheerHops = 3f;

        /// <summary>
        /// 살아남은 아군을 <b>몇 번 뛰게</b> 한다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 지시(사용자): "전투 끝났을 때 간단한 전투 승리 연출. 현재는 이기자마자 팝업이
        /// 올라와서 전투 종료가 실감이 안 남."
        /// </para>
        /// <para>
        /// ★ 원인은 연출이 없어서가 아니라 <b>마지막 적이 죽은 프레임에 판이 바로 지워지기</b>
        /// 때문이다(<c>PlayRoundRoutine</c> 이 재생이 끝나자마자 <see cref="Clear"/> 를 부른다).
        /// 그래서 "이겼다"를 볼 시간 자체가 없었다 — 새 연출을 얹기 전에 <b>판을 잠깐 두는 것</b>이
        /// 먼저다. 환호는 그 시간에 무엇을 볼지를 채울 뿐이다.
        /// </para>
        /// <para>
        /// 스프라이트가 아니라 <see cref="UnitView.Root"/> 를 올린다 — 스프라이트 로컬 좌표는
        /// 절차적 공격 연출(돌진)이 쓰고 있어서, 같은 채널을 두 곳에서 만지면 섞인다.
        /// </para>
        /// </remarks>
        public void StartVictoryCheer()
        {
            // ★ 서 있는 자리를 지금 기억한다. `MoveTarget` 을 기준으로 삼으면 **한 번도 안 움직인
            //   유닛은 그 값이 (0,0,0)** 이라 환호가 시작되는 순간 판 원점으로 순간이동한다.
            foreach (var kv in _units)
            {
                var unit = kv.Value;
                if (unit.Root != null) unit.CheerBaseY = unit.Root.transform.position.y;
            }

            _cheerLeft = VictoryCheerSeconds;
        }

        private void TickCheer()
        {
            if (_cheerLeft <= 0f) return;

            _cheerLeft -= Time.deltaTime;
            float t = Mathf.Clamp01(1f - _cheerLeft / VictoryCheerSeconds);

            // 끝으로 갈수록 잦아든다 — 뚝 끊기면 마지막 프레임에 유닛이 순간이동한 것처럼 보인다.
            float damping = 1f - t;
            float hop = Mathf.Abs(Mathf.Sin(t * Mathf.PI * CheerHops)) * CheerHopHeight * damping;

            foreach (var kv in _units)
            {
                var unit = kv.Value;
                if (unit.Root == null || unit.IsDead || !unit.IsAlly) continue;

                var p = unit.Root.transform.position;
                unit.Root.transform.position = new Vector3(p.x, unit.CheerBaseY + hop, p.z);
            }
        }

        /// <remarks>
        /// 콜라이더를 붙여 <c>Physics2D</c> 로 쏘지 않는다 — 유닛에 물리를 달면
        /// <b>전투에 관여하지 않는 컴포넌트가 전투 오브젝트에 붙는다.</b> 판이 격자라
        /// 거리 비교로 충분하고, 그쪽이 죽은 유닛을 건너뛰기도 쉽다.
        /// </remarks>
        private HoverTarget FindHovered()
        {
            var cam = Camera.main;
            // ★ `UnityEngine.Input` 이 아니라 Input System 을 쓴다 — 이 프로젝트는 Player Settings 에서
            //   입력 처리를 Input System 패키지로 바꿔놨고, 옛 API 를 부르면 예외가 난다.
            //   `PlacementController` 가 이미 같은 방식으로 마우스를 읽는다.
            if (cam == null || Mouse.current == null) return default;

            var world = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            float bestSq = HoverRadius * HoverRadius;
            var best = default(HoverTarget);

            foreach (var kv in _units)
            {
                var unit = kv.Value;
                if (unit.Root == null || unit.IsDead) continue;

                float sq = SqDistance(unit.Root.transform.position, world);
                if (sq >= bestSq) continue;

                bestSq = sq;
                best = new HoverTarget(unit.TypeId, unit.IsAlly, unit.Hp, unit.MaxHp, true);
            }

            if (best.TypeId != null || _placementRoot == null) return best;

            // 배치 미리보기 — 오브젝트 이름이 곧 typeId 다(`CreatePlacementUnit`).
            foreach (Transform child in _placementRoot)
            {
                float sq = SqDistance(child.position, world);
                if (sq >= bestSq) continue;

                bestSq = sq;
                best = new HoverTarget(child.name, _placementAllies.ContainsKey(child.name), 0, 0, false);
            }

            return best;
        }

        private static float SqDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dy = a.y - b.y;
            return dx * dx + dy * dy;
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

            _units.TryGetValue(targetId, out var target);
            if (target?.Root != null) UpdateFacing(unit, target.Root.transform.position);

            if (unit.AttackFrames != null)
            {
                unit.IsAttacking = true;
                unit.FrameIndex = 0;
                unit.FrameTimer = 0f;
                return;
            }

            var direction = unit.IsAlly ? Vector3.right : Vector3.left;
            if (target?.Root != null)
            {
                var delta = target.Root.transform.position - unit.Root.transform.position;
                if (delta.sqrMagnitude > 0.0001f) direction = delta.normalized;
            }

            unit.PunchDirection = direction;
            unit.PunchLeft = PunchSeconds;
        }

        /// <summary>
        /// 좌우만 뒤집는다(<see cref="SpriteRenderer.flipX"/>) — 이 팩엔 위아래를 볼 수 있는 그림이 없어
        /// 상하 성분은 무시한다. 대상이 정확히 같은 열(<c>dx≈0</c>)이면 방향을 정할 근거가 없으니
        /// <b>이전 방향을 그대로 둔다</b> — 매번 억지로 정하면 세로로 마주 선 상대를 칠 때마다
        /// 좌우가 튀어서 더 어색해진다.
        /// </summary>
        private static void UpdateFacing(UnitView unit, Vector3 towardWorldPos)
        {
            if (unit.Sprite == null || unit.Root == null) return;

            var delta = towardWorldPos - unit.Root.transform.position;
            if (delta.sqrMagnitude < 0.0001f) return; // 방향을 정할 근거가 없으면 이전 방향을 유지한다.

            if (Mathf.Abs(delta.y) > Mathf.Abs(delta.x))
            {
                // 세로 성분이 더 크면 위(뒤통수)·아래(정면) 포즈를 고른다. 좌우 반전은 손대지 않는다 —
                // 뒤통수는 좌우가 거의 대칭이라 이전 값을 그대로 둬도 티가 안 난다.
                unit.FacingAway = delta.y > 0f;
                return;
            }

            // 가로 성분이 더 크거나 같으면 정면(아래) 포즈로 되돌리고 좌우만 반전한다 — 이 팩엔 옆모습
            // 전용 그림이 없어서, 정면 포즈를 뒤집는 것 자체가 "옆"의 유일한 표현이다.
            unit.FacingAway = false;
            bool flipX = delta.x < 0f;
            unit.Sprite.flipX = flipX;
            if (unit.Outline != null) unit.Outline.flipX = flipX;
        }

        /// <summary>절차적 펀치(확대+돌진)의 지속 시간(초).</summary>
        private const float PunchSeconds = 0.22f;
        private const float PunchScaleAmount = 0.22f;
        /// <summary>돌진 거리(월드 단위). 칸 크기(<see cref="CellSize"/>=1)에 비해 작게 잡는다 —
        /// 옆 칸까지 넘어가면 "이동"으로 읽혀서 <see cref="MoveTo"/> 이벤트와 헷갈린다.</summary>
        private const float PunchLungeDistance = 0.14f;

        /// <summary>피격.</summary>
        /// <param name="actorId">때린 쪽 유닛. 종류별 타격 이펙트를 고르는 데만 쓴다(<see cref="HitFxSpecFor"/>) —
        /// 못 찾거나 -1(자해 등 공격자가 없는 피해)이면 범용 이펙트로 떨어진다.</param>
        public void FlashDamage(int unitId, int actorId = -1)
        {
            Flash(unitId, DamageTint);
            SpawnHitFx(unitId, actorId);
        }

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

        /// <summary>이펙트 한 프레임이 화면에 머무는 시간(초). 4프레임 × 0.05초 = 0.2초.</summary>
        private const float HitFxFrameSeconds = 0.05f;

        /// <summary>이펙트 한 변의 목표 크기(월드 단위). 칸(<see cref="CellSize"/>)을 거의 채운다 — 무기별 배지를
        /// 없앤 대신, 어떤 무기인지보다 "맞았다"는 순간 자체가 눈에 띄어야 한다.</summary>
        private const float HitFxSize = 0.9f;

        /// <summary>
        /// 캐릭터 6종 → 테마에 맞는 타격 이펙트 카탈로그 키 + 프레임 수. 무기 배지 때 쓰던 것과
        /// 같은 매핑을 재활용한다(사무라이→카타나 slash, 수도승→봉 회전, 적영→사이 발톱,
        /// 사냥꾼→활 관통, 주술사·무녀→마법진). 몬스터·보스·매핑 밖 typeId는 <c>null</c> —
        /// 호출부가 범용 이펙트(<see cref="DefaultHitFxKey"/>)로 떨어진다.
        /// </summary>
        private static (string Key, int Frames)? HitFxSpecFor(string typeId)
        {
            switch (typeId)
            {
                case "C1": return ("FX/Hit/Samurai/SpriteSheet", 4);
                case "C2": return ("FX/Hit/Monk/SpriteSheet", 4);
                case "C3": return ("FX/Hit/NinjaRed/SpriteSheet", 4);
                case "C4": return ("FX/Hit/Hunter/SpriteSheet", 4);
                case "C5": return ("FX/Hit/NinjaMageBlack/SpriteSheet", 6);
                case "C6": return ("FX/Hit/Shaman/SpriteSheet", 4);
                default: return null;
            }
        }

        /// <summary>범용 타격 이펙트(몬스터·보스, 그리고 캐릭터 전용 그림을 못 찾았을 때 쓰는 대체).</summary>
        private const string DefaultHitFxKey = "FX/Hit/SpriteSheet";
        private const int DefaultHitFxFrames = 4;

        /// <summary>
        /// 대상이 맞는 순간 공격자 테마에 맞는 타격 이펙트를 잠깐 띄운다. 캐릭터 모서리에 무기
        /// 아이콘을 상시로 붙였던 이전 방식은 자리를 너무 차지해 뺐고(D+6), 그 자리에 있던 무기별
        /// 구분을 여기로 옮겼다 — "무엇으로 때렸나"가 상시 배지 대신 타격 순간에만 드러난다.
        /// </summary>
        /// <remarks>
        /// 유닛에 안 매인 독립 오브젝트다. 대상이 죽어 사라져도 이펙트는 재생을 끝까지 마친다 —
        /// 죽인 마지막 타격이 화면에서 잘려 보이면 "왜 안 맞았지"로 읽힌다.
        /// </remarks>
        private void SpawnHitFx(int unitId, int actorId)
        {
            if (!_units.TryGetValue(unitId, out var unit) || unit.Root == null) return;
            if (_catalog == null) return;

            string key = DefaultHitFxKey;
            int frameCount = DefaultHitFxFrames;
            if (_units.TryGetValue(actorId, out var attacker) && attacker.TypeId != null)
            {
                var spec = HitFxSpecFor(attacker.TypeId);
                if (spec.HasValue) (key, frameCount) = spec.Value;
            }

            if (!_hitFxFrameCache.TryGetValue(key, out var frames))
            {
                frames = LoadFrames(_catalog, key, frameCount);
                _hitFxFrameCache[key] = frames; // 못 찾아도 null 로 캐싱 — 매번 다시 찾지 않는다.
            }
            if (frames == null) return;

            var go = new GameObject("HitFx");
            go.transform.SetParent(_unitRoot, false);
            go.transform.position = unit.Root.transform.position;

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = frames[0];
            renderer.sortingOrder = 5;

            var size = renderer.sprite.bounds.size;
            float scale = size.x > 0 ? HitFxSize / Mathf.Max(size.x, size.y) : 1f;
            go.transform.localScale = Vector3.one * scale;

            _hitFxInstances.Add(new HitFx { Transform = go.transform, Renderer = renderer, Frames = frames });
        }

        /// <summary>타격 이펙트를 시간에 따라 진행하고, 다 돌면 스스로 없앤다. 재생기가 매 프레임 부른다.</summary>
        public void TickHitFx(float deltaTime)
        {
            for (int i = _hitFxInstances.Count - 1; i >= 0; i--)
            {
                var fx = _hitFxInstances[i];
                fx.FrameTimer += deltaTime;
                if (fx.FrameTimer < HitFxFrameSeconds) continue;

                fx.FrameTimer -= HitFxFrameSeconds;
                fx.FrameIndex++;

                if (fx.FrameIndex >= fx.Frames.Length)
                {
                    Object.Destroy(fx.Transform.gameObject);
                    _hitFxInstances.RemoveAt(i);
                    continue;
                }

                fx.Renderer.sprite = fx.Frames[fx.FrameIndex];
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

                    // 방향 포즈 유닛(캐릭터)은 시간으로 순환하지 않는다 — UpdateFacing이 정한 방향을
                    // 그대로 고정해서 보여준다. 순환하면 정면↔뒤통수를 계속 오가는 "회전"으로 보인다
                    // (`D-77` 후속) — 이 분기가 그 재발을 막는다. Idle뿐 아니라 Attack도 마찬가지다 —
                    // 위(뒤)를 보던 중에 공격이라고 정면 공격 프레임으로 튀면 "공격할 때만 갑자기
                    // 정면을 본다"로 보인다. 뒤를 본 채 때리는 전용 그림은 없으니, 그럴 땐 윈드업
                    // 프레임 대신 뒤통수 Idle 포즈를 그대로 들고 있는다 — 번쩍임·타격 이펙트가
                    // "공격했다"는 신호를 이미 맡고 있어 포즈 자체는 방향 일관성을 더 우선한다.
                    if (unit.HasDirectionalIdle)
                    {
                        bool facingAwayNow = unit.FacingAway && unit.IdleFrames.Length > 1;

                        if (attacking)
                        {
                            unit.FrameTimer += deltaTime;
                            if (unit.FrameTimer >= unit.AttackFrameSeconds)
                            {
                                unit.FrameTimer -= unit.AttackFrameSeconds;
                                unit.IsAttacking = false;
                            }
                        }

                        // 방금 위에서 되돌렸을 수 있으니(같은 틱 안에서) attacking을 다시 읽는다 —
                        // 아니면 되돌린 그 틱에 공격 프레임이 한 틱 더 남아 보인다.
                        bool stillAttacking = unit.IsAttacking && unit.AttackFrames != null;
                        unit.Sprite.sprite = (stillAttacking && !facingAwayNow)
                            ? unit.AttackFrames[0]
                            : unit.IdleFrames[facingAwayNow ? 1 : 0];
                        continue;
                    }

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
