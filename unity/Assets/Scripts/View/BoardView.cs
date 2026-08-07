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

        /// <summary>
        /// 보스인 유닛 종류. <b>데이터의 <c>isBoss</c> 를 그대로 받는다</b> — 이름으로 추측하지 않는다.
        /// </summary>
        /// <remarks>
        /// 경로에 <c>Actor/Boss/</c> 가 들어가는지로 갈라도 지금은 맞지만, 그러면 <b>폴더 이름이
        /// 기획 정보가 된다</b> — 아트가 폴더를 정리하는 순간 보스가 조용히 작아진다.
        /// <c>_spritePaths</c> 를 데이터에서 받기로 한 것과 같은 이유다.
        /// </remarks>
        private ICollection<string> _bossTypeIds;

        /// <summary>
        /// 보스 스프라이트 확대 배율.
        /// </summary>
        /// <remarks>
        /// 지시(사용자): *"보스 스프라이트 너무 작게 나오던데 지금 크기의 1.5배만 해서 넣자."*
        /// <para>
        /// 원본 그림이 82×82(텐구)·40×40(개구리)로 잡몹(16×16)보다 훨씬 큰데,
        /// <b>전원을 칸의 0.8 로 맞추는 규칙이 그 차이를 통째로 지웠다.</b>
        /// 칸에 맞추는 것 자체는 필요하다(초상 크기가 제각각이다) — 보스만 예외를 준다.
        /// </para>
        /// </remarks>
        private const float BossSpriteScale = 1.5f;

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

            /// <summary>
            /// <b>맞은</b> 순간의 튐이 남은 시간(초). <see cref="PunchLeft"/>(때리는 쪽)와 다른 사건이다.
            /// </summary>
            /// <remarks>
            /// 프레임 애니메이션이 있는 종류(캐릭터·보스)도 이건 탄다 —
            /// 때리는 연출은 그림이 대신해주지만 <b>맞는 그림은 아무도 안 그려줬다.</b>
            /// </remarks>
            public float HitPunchLeft;

            /// <summary>죽은 뒤 흩어지는 데 남은 시간(초). 0 이면 이미 다 사라졌거나 살아 있다.</summary>
            public float DeathFadeLeft;
        }

        /// <summary>화면에 재생 중인 타격 이펙트 1개. 유닛에 안 매여 있다 — 재생이 끝나면 스스로 사라진다.</summary>
        /// <summary>떠오르며 사라지는 피해 숫자 하나. <see cref="HitFx"/> 와 같이 유닛에 안 매인다.</summary>
        private sealed class DamagePopup
        {
            public Transform Transform;
            public TMPro.TextMeshPro Text;
            public float Left;
            public float Total;
            public Vector3 From;

            /// <summary>뜨기 전 기다리는 시간(초). 스킬 이름이 겹치지 않게 미룰 때 쓴다.</summary>
            public float Delay;

            /// <summary>다 올라갔을 때의 높이(월드).</summary>
            public float Rise = DamagePopupRise;
        }

        /// <summary>스킬 발동 때 유닛을 감싸는 빛. 유닛에 붙어 다니다 스스로 사라진다.</summary>
        private sealed class CastGlow
        {
            public Transform Transform;
            public SpriteRenderer Renderer;
            /// <summary>따라다닐 유닛의 그림. 애니메이션 프레임이 바뀌면 빛도 같이 바뀐다.</summary>
            public SpriteRenderer Source;
            public float Left;
            public float Total;
        }

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
                               IReadOnlyDictionary<string, string> spritePaths = null,
                               ICollection<string> bossTypeIds = null)
        {
            _catalog = catalog;
            _spritePaths = spritePaths;
            _bossTypeIds = bossTypeIds;
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

        /// <summary>보스인 적 종류를 데이터에서 그대로 뽑는다. <see cref="SpritePathsFrom"/> 와 같은 이유다.</summary>
        public static HashSet<string> BossTypeIdsFrom(GameData data)
        {
            var set = new HashSet<string>();
            if (data == null) return set;

            foreach (var kv in data.EnemyTypes)
                if (kv.Value.IsBoss) set.Add(kv.Key);

            return set;
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

            // 무기·피해 숫자도 같이 치운다 — 유닛 트리 밖에 있어서 안 지우면 다음 라운드까지 남는다.
            foreach (var fx in _weaponFx)
            {
                if (fx.Transform != null) Object.Destroy(fx.Transform.gameObject);
            }
            _weaponFx.Clear();

            foreach (var p in _damagePopups)
            {
                if (p.Transform != null) Object.Destroy(p.Transform.gameObject);
            }
            _damagePopups.Clear();

            // 빛은 유닛의 자식이라 위에서 이미 사라졌다. 목록만 비우면 된다.
            _castGlows.Clear();
            _castSlotFreeAt = 0f;
            _volleyActor = -1;

            foreach (var o in _overlays)
            {
                if (o.Renderer != null) Object.Destroy(o.Renderer.gameObject);
            }
            _overlays.Clear();
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
                // 초상 크기가 제각각이라 칸에 맞춘다. 보스만 그 위에 배율을 더 준다 —
                // 안 그러면 82×82 로 그려진 보스가 16×16 슬라임과 같은 크기로 선다.
                var size = renderer.sprite.bounds.size;
                baseSpriteScale = size.x > 0 ? CellSize * 0.8f / Mathf.Max(size.x, size.y) : 1f;
                if (_bossTypeIds != null && _bossTypeIds.Contains(spec.TypeId))
                    baseSpriteScale *= BossSpriteScale;

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

            // ★ 무기·피해 숫자는 <b>스스로</b> 돈다. 재생기에 맡겼더니 재생이 끝나는 순간
            //   Tick 이 멈춰서 **마지막 연출이 그 자리에 얼어붙은 채 안 사라졌다** —
            //   실측에서 전투 뒤에도 Katana·Bow·EnergyBall 이 (0,0,0) 에 남아 있었다.
            //   승리 연출 0.9초 동안에도 재생기는 멈춰 있으므로 여기서 도는 편이 맞다.
            TickWeaponFx(Time.deltaTime);
            TickDamagePopups(Time.deltaTime);
            TickCastGlows(Time.deltaTime);
            TickOverlays(Time.deltaTime);

            // ★ 죽는 연출도 **스스로** 돌아야 한다. <see cref="TickAnimations"/> 에 얹었더니
            //   재생기가 마지막 이벤트를 소비하고 멈추는 순간 Tick 이 끊겨,
            //   **라운드를 이긴 순간의 마지막 적만 반쯤 사라진 채 얼어붙었다**(사용자 지적).
            //   위의 무기·피해 숫자가 같은 이유로 이미 여기 와 있다.
            TickHitPunch(Time.deltaTime);
            TickDeathFade(Time.deltaTime);
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
            unit.HitPunchLeft = 0f;
            unit.DeathFadeLeft = DeathFadeSeconds;
            if (unit.Sprite != null)
            {
                unit.Sprite.color = Color.white;   // TickDeathFade 가 여기서부터 흐리게 한다
                unit.Sprite.transform.localScale = Vector3.one * unit.BaseSpriteScale;
            }
            // 흩어지는 0.45초 동안만 빈 트랙이 보인다 — 그 뒤엔 <see cref="TickDeathFade"/> 가
            // 유닛을 통째로 끈다. 트랙만 남겨두면 시체 없이 막대가 혼자 떠 있게 된다.
            if (unit.HpFill != null) SetFill(unit, 0f);
            if (unit.ShieldFill != null) unit.ShieldFill.parent.gameObject.SetActive(false);
            if (unit.Outline != null) unit.Outline.gameObject.SetActive(false);

            // ★ 아군 쪽을 더 크게 흔든다. 같은 세기로 두면 <b>이겼는지 졌는지가 화면에서 안 갈린다</b> —
            //   적이 훨씬 많이 죽으므로 잦은 쪽이 약해야 드문 쪽(아군 사망)이 사건으로 읽힌다.
            if (unit.IsAlly)
            {
                BoardCamera.Shake(AllyDeathShake, AllyDeathShakeSeconds);

                // ★ 흔들림만으로는 <b>누가</b> 죽었는지가 안 갈린다 — 적이 죽어도 흔들리기 때문이다.
                //   가장자리만 붉게 물들이면 판 가운데(읽어야 하는 곳)를 안 가리고 진영이 읽힌다.
                ShowOverlay(OverlayKind.Vignette, new Color(0.85f, 0.1f, 0.12f), 0.5f, 0.55f);
            }
            else
            {
                BoardCamera.Shake(EnemyDeathShake, EnemyDeathShakeSeconds);
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  화면 덮개 — 비네트 · 번쩍임
        // ─────────────────────────────────────────────────────────────

        private enum OverlayKind { Vignette, Flash }

        private sealed class ScreenOverlay
        {
            public SpriteRenderer Renderer;
            public Color Color;
            public float PeakAlpha;
            public float Left;
            public float Total;
        }

        private readonly List<ScreenOverlay> _overlays = new List<ScreenOverlay>();
        private static Sprite _vignetteSprite;
        private static Sprite _flatSprite;

        /// <summary>덮개가 판보다 확실히 커야 하는 크기(월드). 세로 반높이 4.2 보다 넉넉하다.</summary>
        private const float OverlaySize = 40f;

        /// <summary>
        /// 화면 전체를 짧게 덮는다. <b>비네트는 가장자리만, 번쩍임은 고르게.</b>
        /// </summary>
        /// <remarks>
        /// ★ <b>후처리를 안 쓴다</b>(테두리 빛과 같은 이유) — WebGL 에서 전체 화면 후처리는
        /// 빌드 크기와 픽셀 비용을 같이 올린다. 스프라이트 한 장이면 충분하다.
        /// 비네트 텍스처는 64×64 로 <b>한 번만</b> 만들어 두고 계속 쓴다.
        /// </remarks>
        private void ShowOverlay(OverlayKind kind, Color color, float peakAlpha, float seconds)
        {
            var go = new GameObject("ScreenOverlay");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0f, 0f, -4f);   // 유닛·팝업보다 앞

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = kind == OverlayKind.Vignette ? VignetteSprite() : FlatSprite();
            sr.sortingOrder = 200;

            var size = sr.sprite.bounds.size;
            go.transform.localScale = Vector3.one * (OverlaySize / Mathf.Max(size.x, size.y));

            var c = color; c.a = 0f;
            sr.color = c;

            _overlays.Add(new ScreenOverlay
            {
                Renderer = sr, Color = color, PeakAlpha = peakAlpha,
                Left = seconds, Total = seconds,
            });
        }

        /// <summary>덮개를 띄웠다 지운다. 빠르게 차고 천천히 빠진다 — 사건은 순간이고 여운은 길다.</summary>
        public void TickOverlays(float deltaTime)
        {
            for (int i = _overlays.Count - 1; i >= 0; i--)
            {
                var o = _overlays[i];
                o.Left -= deltaTime;

                if (o.Left <= 0f || o.Renderer == null)
                {
                    if (o.Renderer != null) Object.Destroy(o.Renderer.gameObject);
                    _overlays.RemoveAt(i);
                    continue;
                }

                float t = 1f - o.Left / o.Total;
                // 앞 15% 에 차오르고 나머지 85% 동안 빠진다.
                float a = t < 0.15f ? t / 0.15f : 1f - (t - 0.15f) / 0.85f;

                var c = o.Color;
                c.a = o.PeakAlpha * a;
                o.Renderer.color = c;
            }
        }

        /// <summary>가운데는 비고 가장자리로 갈수록 짙어지는 64×64. 한 번 만들어 계속 쓴다.</summary>
        private static Sprite VignetteSprite()
        {
            if (_vignetteSprite != null) return _vignetteSprite;

            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var pixels = new Color32[n * n];

            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    // 중심에서의 거리 0~1. 0.45 안쪽은 완전히 투명하게 둔다 — 판을 안 가리는 게 목적이다.
                    float dx = (x + 0.5f) / n * 2f - 1f;
                    float dy = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, d));
                    pixels[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();

            _vignetteSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            return _vignetteSprite;
        }

        /// <summary>고르게 덮는 1×1. 번쩍임용.</summary>
        private static Sprite FlatSprite()
        {
            if (_flatSprite != null) return _flatSprite;

            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();

            _flatSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
            return _flatSprite;
        }

        // ─────────────────────────────────────────────────────────────
        //  화면 흔들림 세기 — 사건의 무게 순서가 곧 숫자의 순서다
        // ─────────────────────────────────────────────────────────────
        //
        // 지시(사용자): "큰 스킬을 쓰거나, 적 처치, 혹은 아군 사망시에 화면 흔들림."
        //
        // 세로 반높이가 4.2 이므로 0.16 은 화면 높이의 약 2% 다.
        // 위로 더 올리면 픽셀 아트가 흐려 보이기 시작한다 — 판이 8×6 이라 흔들 여백이 적다.

        private const float AllyDeathShake = 0.16f;
        private const float AllyDeathShakeSeconds = 0.30f;

        private const float EnemyDeathShake = 0.07f;
        private const float EnemyDeathShakeSeconds = 0.18f;

        private const float SkillCastShake = 0.05f;
        private const float SkillCastShakeSeconds = 0.16f;

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

            PlayWeaponFx(actorId, targetId);

            // 이 공격이 여럿을 맞히면 뒤따르는 피해마다 한 발씩 더 나간다 — <see cref="NotifyVolleyHit"/>.
            _volleyActor = IsRangedActor(actorId) ? actorId : -1;
            _volleyMainTarget = targetId;
        }

        /// <summary>지금 다중 사격 중인 원거리 공격자. -1 이면 없다.</summary>
        private int _volleyActor = -1;

        /// <summary>그 공격의 주 표적. <b>이쪽으로는 이미 한 발 나갔다.</b></summary>
        private int _volleyMainTarget = -1;

        /// <summary>
        /// 방금 공격이 <b>주 표적이 아닌 누군가</b>도 맞혔다. 그쪽으로 한 발 더 날린다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 지시(사용자): *"사냥꾼의 난사처럼 다중공격을 하는 원거리 공격은 투사체도
        /// 그만큼 늘어나야한다고 생각함."*
        /// </para>
        /// <para>
        /// ★ <b>표적을 추측하는 게 아니다.</b> `C4-B` 난사는 <c>Attack(사냥꾼, 주표적)</c> 한 건 뒤에
        /// <c>Damage(사냥꾼, 표적)</c> 를 <b>맞은 수만큼</b> 잇달아 낸다
        /// (`BattleSimulator.PerformAttack` — 표적별 <c>Attack</c> 은 안 낸다).
        /// <b>누구를 맞혔는지는 이미 적혀 있는 사실</b>이고, `23` 이
        /// *"같은 틱의 이벤트가 여러 건이면 리스트 순서가 곧 인과 순서"* 라고 보장한다 —
        /// 화면은 그 연속을 그대로 읽는다. (`24` §2 의 `C` 안 — *"틱 간격으로 짐작"* — 과는
        /// 다른 일이다. 짐작할 것이 없다.)
        /// </para>
        /// <para>
        /// ⚠️ 그래서 <b>연속이 끊기면 반드시 닫아야 한다</b>(<see cref="EndVolley"/>).
        /// 같은 틱에 다른 유닛의 도트가 돌면 <c>Damage(같은 사냥꾼, 다른 적)</c> 가 또 나오는데
        /// (도트는 <b>건 사람</b>이 가해자로 적힌다), 그건 화살이 아니다.
        /// </para>
        /// <para>
        /// 근접 광역(<c>C1-B</c> 연격 · <c>C2-B</c> 파동)은 <b>안 늘린다.</b> 한 번 휘둘러
        /// 여럿이 맞는 것이라 휘두르기가 여러 번 나오면 오히려 틀린 그림이 된다.
        /// </para>
        /// </remarks>
        public void NotifyVolleyHit(int actorId, int targetId)
        {
            if (_volleyActor < 0 || actorId != _volleyActor) return;
            if (targetId < 0 || targetId == _volleyMainTarget) return;   // 주 표적엔 이미 나갔다
            if (targetId == actorId) return;                             // 자해는 공격이 아니다

            PlayWeaponFx(actorId, targetId);
        }

        /// <summary>공격에 딸린 연속이 끝났다. <see cref="NotifyVolleyHit"/> 의 경고 참조.</summary>
        public void EndVolley()
        {
            _volleyActor = -1;
            _volleyMainTarget = -1;
        }

        private bool IsRangedActor(int actorId)
        {
            if (RangedTypeIds == null) return false;
            return _units.TryGetValue(actorId, out var actor) && RangedTypeIds.Contains(actor.TypeId);
        }

        // ─────────────────────────────────────────────────────────────
        //  무기 연출 — 가까우면 휘두르고, 멀면 날린다
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 캐릭터별 무기와 투사체. 없는 종류(몬스터)는 <c>null</c> 이라 연출을 건너뛴다.
        /// </summary>
        /// <remarks>
        /// 팀원이 <c>5e77e32</c> 에서 정한 테마를 그대로 쓴다 —
        /// 사무라이→카타나 · 수도승→봉 · 적영→사이 · 사냥꾼→활 · 주술사→마법봉 · 무녀→책.
        /// 한 번 정한 짝을 다시 정하면 <b>화면마다 무기가 달라진다.</b>
        /// </remarks>
        private static (string Weapon, string Projectile, string Sound)? WeaponOf(string typeId)
        {
            switch (typeId)
            {
                case "C1": return ("Weapon/Katana", "FX/Projectile/Shuriken", AudioKeys.AttackBlade);
                case "C2": return ("Weapon/Stick", "FX/Projectile/EnergyBall", AudioKeys.AttackBlunt);
                case "C3": return ("Weapon/Sai", "FX/Projectile/Shuriken", AudioKeys.AttackBlade);
                case "C4": return ("Weapon/Bow", "FX/Projectile/Arrow", AudioKeys.AttackBow);
                case "C5": return ("Weapon/MagicWand", "FX/Projectile/EnergyBall", AudioKeys.AttackMagic);
                case "C6": return ("Weapon/Book", "FX/Projectile/EnergyBall", AudioKeys.AttackMagic);
                default: return null;
            }
        }

        /// <summary>
        /// 이 공격자가 낼 소리. 무기가 없는 종류(몬스터)면 <c>null</c> — 부른 쪽이 기본음을 쓴다.
        /// </summary>
        /// <remarks>
        /// 소리를 <see cref="BoardView"/> 가 직접 재생하지 않고 이름만 돌려주는 이유 —
        /// 전투 소리는 <c>BattleReplayer</c> 가 한 곳에서 낸다. 두 곳에서 내면
        /// <b>배속을 바꿨을 때 한쪽만 반영되는</b> 종류의 어긋남이 생긴다.
        /// </remarks>
        public string AttackSoundKey(int actorId)
        {
            if (!_units.TryGetValue(actorId, out var actor)) return null;
            return WeaponOf(actor.TypeId)?.Sound;
        }

        /// <summary>
        /// 투사체를 쓰는 유닛 종류. <b>화면 쪽이 런 상태를 보고 채운다.</b>
        /// </summary>
        /// <remarks>
        /// <para>
        /// ★ 처음엔 <b>공격 순간의 거리</b>로 갈랐다. 그러면 적영이 `표창`(사거리 1→4)을 산 뒤에도
        /// <b>적이 붙으면 사이를 휘두른다.</b> 사용자 지적 — *"적영 같은 경우엔 근거리는 검,
        /// 표창 찍으면 표창만"*. 무기를 정하는 건 지금 거리가 아니라 <b>고른 빌드</b>다.
        /// </para>
        /// <para>
        /// 기본 사거리(<c>characters.json</c>)와 액티브 스킬의 <c>setRange</c> 를 같이 봐야 알 수 있는데,
        /// 둘 다 <b>런 상태</b>라 뷰가 알 수 없다 — 그래서 화면이 채워준다.
        /// 비어 있으면(관전 뷰) 전부 근접으로 친다.
        /// </para>
        /// </remarks>
        public HashSet<string> RangedTypeIds { get; set; }

        private void PlayWeaponFx(int actorId, int targetId)
        {
            if (!_units.TryGetValue(actorId, out var actor) || actor.Root == null) return;

            var spec = WeaponOf(actor.TypeId);
            if (spec == null || _catalog == null) return;

            var from = actor.Root.transform.position;
            bool hasTarget = _units.TryGetValue(targetId, out var target) && target.Root != null;

            // 표적이 없는 광역 공격은 진영 방향으로 낸다.
            var to = hasTarget
                ? target.Root.transform.position
                : from + new Vector3(actor.IsAlly ? 1f : -1f, 0f, 0f);

            bool ranged = RangedTypeIds != null && RangedTypeIds.Contains(actor.TypeId);
            if (ranged) SpawnProjectile(spec.Value.Projectile, from, to);
            else SpawnSwing(spec.Value.Weapon, from, to, actor);
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

            // 맞은 쪽이 순간 부푼다. 색만 바뀌면 <b>연타가 한 번으로 보인다</b> —
            // 번쩍임은 0.18초 동안 이어지므로 그 안에 들어온 두 번째 타격이 화면에서 사라진다.
            if (_units.TryGetValue(unitId, out var hit) && !hit.IsDead)
                hit.HitPunchLeft = HitPunchSeconds;
        }

        /// <summary>맞은 쪽이 부푸는 시간(초). 짧아야 한다 — 길면 유닛이 물렁해 보인다.</summary>
        private const float HitPunchSeconds = 0.12f;

        /// <summary>부푸는 정도. 0.15 면 15% 다.</summary>
        private const float HitPunchAmount = 0.18f;

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

        // ─────────────────────────────────────────────────────────────
        //  피해 숫자
        // ─────────────────────────────────────────────────────────────

        private readonly List<DamagePopup> _damagePopups = new List<DamagePopup>();

        /// <summary>숫자가 떠 있는 시간(초). 길면 연타 때 숫자가 쌓여 판을 가린다.</summary>
        private const float DamagePopupSeconds = 0.7f;

        /// <summary>떠오르는 높이(월드). 칸이 1 이라 반 칸이면 이웃 칸까지 안 넘어간다.</summary>
        private const float DamagePopupRise = 0.5f;

        /// <summary>
        /// 맞은 자리에 <b>피해량</b>을 띄운다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 지시(사용자): "타격마다 데미지 이펙트 띄워줘."
        /// </para>
        /// <para>
        /// 값은 <c>EventKind.Damage</c> 의 <c>Value</c>(피해량)를 그대로 쓴다 —
        /// 화면에서 다시 계산하지 않는다. 체력 막대만으로는 <b>얼마나 아팠는지</b>가 안 읽히고,
        /// 특히 스킬이 평타보다 센 것이 숫자로 안 보이면 스킬을 산 보람이 없다.
        /// </para>
        /// <para>
        /// ★ 유닛에 붙이지 않고 <b>맞은 자리에 떨어뜨린다.</b> 붙이면 유닛이 움직이거나 죽을 때
        /// 숫자가 같이 끌려가거나 사라진다 — 죽는 순간의 마지막 타격이 제일 보고 싶은 숫자다.
        /// </para>
        /// </remarks>
        public void ShowDamage(int unitId, int amount)
        {
            if (amount <= 0) return;
            if (!_units.TryGetValue(unitId, out var unit) || unit.Root == null) return;

            var go = new GameObject("DamagePopup");
            go.transform.SetParent(transform, false);

            var from = unit.Root.transform.position + new Vector3(0f, 0.25f, -2f);
            go.transform.position = from;

            var text = go.AddComponent<TMPro.TextMeshPro>();
            text.text = amount.ToString();
            text.fontSize = 3.2f;
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.color = unit.IsAlly ? new Color(1f, 0.55f, 0.5f) : new Color(1f, 0.93f, 0.6f);
            text.sortingOrder = 60;

            // 글자가 칸보다 커지지 않게. RectTransform 이 기본 200x50 이라 그대로 두면 엄청 넓다.
            var rt = text.rectTransform;
            rt.sizeDelta = new Vector2(2f, 1f);

            _damagePopups.Add(new DamagePopup
            {
                Transform = go.transform,
                Text = text,
                Left = DamagePopupSeconds,
                Total = DamagePopupSeconds,
                From = from,
            });
        }

        /// <summary>피해 숫자를 띄우고 흐리게 하며, 다 되면 없앤다. 재생기가 매 프레임 부른다.</summary>
        public void TickDamagePopups(float deltaTime)
        {
            for (int i = _damagePopups.Count - 1; i >= 0; i--)
            {
                var p = _damagePopups[i];
                p.Left -= deltaTime;

                if (p.Left <= 0f || p.Transform == null)
                {
                    if (p.Transform != null) Object.Destroy(p.Transform.gameObject);
                    _damagePopups.RemoveAt(i);
                    continue;
                }

                // 아직 차례가 안 왔으면 숨겨둔다 — 이름이 여럿 겹치는 것을 막는 유일한 수단이다.
                if (p.Delay > 0f)
                {
                    p.Delay -= deltaTime;
                    p.Left = p.Total;   // 기다린 시간은 수명에서 빼지 않는다
                    if (p.Text.enabled) p.Text.enabled = false;
                    continue;
                }
                if (!p.Text.enabled) p.Text.enabled = true;

                float t = 1f - p.Left / p.Total;
                p.Transform.position = p.From + new Vector3(0f, p.Rise * t, 0f);

                // 마지막 30% 에서만 사라진다 — 처음부터 흐려지면 읽기 전에 안 보인다.
                var c = p.Text.color;
                c.a = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
                p.Text.color = c;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  액티브 스킬 — 이름과 테두리 빛
        // ─────────────────────────────────────────────────────────────

        private readonly List<CastGlow> _castGlows = new List<CastGlow>();

        /// <summary>다음 스킬 이름을 띄울 수 있는 시각(<see cref="Time.time"/>). 겹침을 막는다.</summary>
        private float _castSlotFreeAt;

        private const float CastPopupSeconds = 1.1f;
        private const float CastGlowSeconds = 0.55f;

        /// <summary>이름 하나가 차지하는 시간(초). 전투 시작에 셋이 한꺼번에 올 때 이 간격으로 흩어진다.</summary>
        private const float CastSlotSeconds = 0.4f;

        private static readonly Color CastNameColor = new Color(1f, 0.85f, 0.35f);
        private static readonly Color CastGlowColor = new Color(1f, 0.92f, 0.55f);

        /// <summary>
        /// 캐릭터 종류 → 그 캐릭터가 고른 <b>액티브 스킬 이름</b>. <b>화면 쪽이 로스터를 보고 채운다.</b>
        /// </summary>
        /// <remarks>
        /// 이벤트에 이름을 싣지 않는 이유는 <see cref="EventKind.SkillCast"/> 주석에 있다 —
        /// 한 유닛의 액티브는 전투 내내 하나라 <b>"누가"만 알면 이름은 여기서 나온다.</b>
        /// <see cref="RangedTypeIds"/> 와 같은 자리에서 같은 로스터를 읽어 채운다.
        /// </remarks>
        public Dictionary<string, string> ActiveSkillNames { get; set; }

        /// <summary>
        /// 액티브 스킬을 알린다. <paramref name="fired"/> 가 참이면 <b>실제로 터진 것</b>이다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 지시(사용자): "스킬발동시 시전 캐릭터에 … <b>스킬이름!</b> 같은느낌으로 띄워주고,
        /// 캐릭터 주변에 반짝이는 효과" → 이어서 *"테두리 글레이징이나 하이라이팅 같은 연출"*.
        /// </para>
        /// <para>
        /// ★ <b>후처리(블룸)를 쓰지 않았다.</b> "빛난다"의 정석은 블룸이지만 WebGL 에서
        /// 전체 화면 후처리는 빌드 크기와 픽셀 비용을 같이 올린다. 대신 <b>유닛 그림을 한 장 더 깔고
        /// 키워서 뒤에 둔다</b> — 테두리만 번지는 것처럼 보이고, 비용은 스프라이트 1장이다.
        /// 애니메이션 프레임을 매 프레임 따라 복사하므로 움직이는 중에도 윤곽이 어긋나지 않는다.
        /// </para>
        /// </remarks>
        public void ShowSkillCast(int unitId, bool fired)
        {
            if (!_units.TryGetValue(unitId, out var unit) || unit.Root == null || unit.IsDead) return;

            string name = null;
            if (ActiveSkillNames != null) ActiveSkillNames.TryGetValue(unit.TypeId, out name);

            if (fired)
            {
                SpawnCastGlow(unit);
                BoardCamera.Shake(SkillCastShake, SkillCastShakeSeconds);
            }

            // 이름을 모르면 빛만 낸다 — 관전 뷰처럼 로스터가 없는 곳에서도 연출은 남는다.
            if (string.IsNullOrEmpty(name)) return;

            // 전투 시작 알림은 같은 틱에 인원수만큼 몰려온다. 슬롯을 잡아 순서대로 흩는다.
            float now = Time.time;
            if (_castSlotFreeAt < now) _castSlotFreeAt = now;
            float delay = _castSlotFreeAt - now;
            _castSlotFreeAt += CastSlotSeconds;

            var go = new GameObject("SkillCastPopup");
            go.transform.SetParent(transform, false);

            var from = unit.Root.transform.position + new Vector3(0f, 0.45f, -2f);
            go.transform.position = from;

            var text = go.AddComponent<TMPro.TextMeshPro>();
            text.text = name + "!";
            text.fontSize = fired ? 3.6f : 2.8f;
            text.fontStyle = TMPro.FontStyles.Bold;
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.color = CastNameColor;
            text.sortingOrder = 61;   // 피해 숫자(60)보다 위 — 겹치면 이름이 읽혀야 한다
            text.rectTransform.sizeDelta = new Vector2(4f, 1f);
            text.enabled = false;     // 차례가 오면 Tick 이 켠다

            _damagePopups.Add(new DamagePopup
            {
                Transform = go.transform,
                Text = text,
                Left = CastPopupSeconds,
                Total = CastPopupSeconds,
                From = from,
                Delay = delay,
                Rise = 0.75f,
            });
        }

        private void SpawnCastGlow(UnitView unit)
        {
            if (unit.Sprite == null || unit.Sprite.sprite == null) return;

            var go = new GameObject("CastGlow");
            go.transform.SetParent(unit.Root.transform, false);
            go.transform.localPosition = Vector3.zero;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = unit.Sprite.sprite;
            sr.color = CastGlowColor;
            sr.sortingOrder = unit.Sprite.sortingOrder - 1;   // 유닛 뒤. 앞에 두면 얼굴을 덮는다

            _castGlows.Add(new CastGlow
            {
                Transform = go.transform,
                Renderer = sr,
                Source = unit.Sprite,
                Left = CastGlowSeconds,
                Total = CastGlowSeconds,
            });
        }

        /// <summary>테두리 빛을 키우며 흐리게 한다. 다 되면 없앤다.</summary>
        public void TickCastGlows(float deltaTime)
        {
            for (int i = _castGlows.Count - 1; i >= 0; i--)
            {
                var g = _castGlows[i];
                g.Left -= deltaTime;

                // 유닛이 죽어 사라지면 빛도 같이 사라진다 — 부모가 없어지므로 Transform 이 null 이 된다.
                if (g.Left <= 0f || g.Transform == null || g.Source == null)
                {
                    if (g.Transform != null) Object.Destroy(g.Transform.gameObject);
                    _castGlows.RemoveAt(i);
                    continue;
                }

                float t = 1f - g.Left / g.Total;

                // 애니메이션을 따라간다. 안 하면 공격 모션 중에 윤곽이 어긋나 두 겹으로 보인다.
                g.Renderer.sprite = g.Source.sprite;
                g.Renderer.flipX = g.Source.flipX;

                g.Transform.localScale = Vector3.one * Mathf.Lerp(1.06f, 1.38f, t);

                var c = CastGlowColor;
                c.a = 0.85f * (1f - t);
                g.Renderer.color = c;
            }
        }

        /// <summary>휘두르거나 날아가는 것 하나. 유닛에 안 매인다 — 끝나면 스스로 사라진다.</summary>
        private sealed class WeaponFx
        {
            public Transform Transform;
            public float Left;
            public float Total;
            public Vector3 From;
            public Vector3 To;
            /// <summary>참이면 호를 그리며 휘두르고, 거짓이면 직선으로 날아간다.</summary>
            public bool IsSwing;
            public float BaseAngle;

            /// <summary>
            /// 휘두르는 주인. <b>휘두르기는 손에 붙어 있어야 한다.</b>
            /// </summary>
            /// <remarks>
            /// 지시(사용자): *"휘두르는 무기는 용병 움직여도 따라가야 한다고 생각,
            /// 지금은 캐릭터 이동하면 무기만 제자리에서 휘둘러지더라."*
            /// <para>
            /// 자식으로 붙이지 않고 <b>매 프레임 위치만 따라간다</b> — 붙이면 유닛의 배율
            /// (<c>BaseSpriteScale</c> · 펀치 확대)까지 상속돼서 무기가 같이 커졌다 작아진다.
            /// 투사체는 안 따라간다. 그건 손을 떠난 것이라 제자리에서 날아가는 게 맞다.
            /// </para>
            /// </remarks>
            public UnitView Owner;

            public SpriteRenderer Renderer;
            /// <summary>여러 장이면 날아가는 동안 넘긴다. 한 장이면 길이 1.</summary>
            public Sprite[] Frames;
            /// <summary>0 이 아니면 방향을 보는 대신 이 속도(도/초)로 돈다.</summary>
            public float SpinPerSecond;
            /// <summary>도는 것의 지금 각도(도).</summary>
            public float Angle;
        }

        /// <summary>
        /// 투사체 <b>그림 자체</b>가 어떻게 그려져 있는지. 그림에만 있는 정보라 코드가 갖고 있어야 한다.
        /// </summary>
        /// <remarks>
        /// ★ <b>전에는 모든 투사체가 오른쪽을 보고 그려졌다고 가정했다.</b>
        /// 실제로는 화살도 쿠나이도 <b>↗ 45° 대각선</b>으로 그려져 있어서, 오른쪽으로 쏜 화살이
        /// 45° 들려 날아갔다. 사용자 지적 — *"표창은 상관 없지만 화살같은 경우엔 많이 신경쓰임"*.
        /// <para>
        /// 표창은 <b>각도를 안 맞추고 돌린다.</b> 던진 표창이 한 방향을 보고 가는 게 오히려 어색하다 —
        /// "상관 없는 것"을 억지로 맞추는 대신 그 그림에 맞는 움직임을 준다.
        /// </para>
        /// <para>
        /// 쓰이지 않는 <c>Kunai</c> 도 적어둔다. 표에 없으면 기본값(0°)으로 조용히 틀어지는데,
        /// 그게 방금 화살에서 일어난 일이다.
        /// </para>
        /// </remarks>
        private readonly struct ProjectileStyle
        {
            /// <summary>그림이 향하고 있는 각도(도). 0=오른쪽, 90=위.</summary>
            public readonly float NativeAngle;

            /// <summary>0 이 아니면 방향 대신 회전. 도/초.</summary>
            public readonly float SpinPerSecond;

            public ProjectileStyle(float nativeAngle, float spinPerSecond)
            {
                NativeAngle = nativeAngle;
                SpinPerSecond = spinPerSecond;
            }
        }

        private static ProjectileStyle StyleOf(string projectileKey)
        {
            switch (projectileKey)
            {
                case "FX/Projectile/Arrow": return new ProjectileStyle(45f, 0f);
                case "FX/Projectile/Kunai": return new ProjectileStyle(45f, 0f);
                case "FX/Projectile/Shuriken": return new ProjectileStyle(0f, 900f);
                case "FX/Projectile/EnergyBall": return new ProjectileStyle(90f, 0f);
                default: return new ProjectileStyle(0f, 0f);
            }
        }

        /// <summary>
        /// 투사체 그림을 가져온다. 이어 붙은 시트면 <c>_0</c> 부터 있는 만큼.
        /// </summary>
        /// <remarks>
        /// ★ <b>장수를 여기 적지 않는다.</b> 몇 장으로 자를지는 <c>SpriteCatalogBuilder</c> 가
        /// 시트 폭에서 역산하므로, 같은 숫자를 뷰에도 적으면 아트가 프레임을 늘렸을 때
        /// <b>둘이 어긋나고 마지막 프레임이 조용히 빠진다.</b> 없어질 때까지 찾는 쪽이 맞다.
        /// </remarks>
        private Sprite[] LoadProjectileFrames(string projectileKey)
        {
            // 한 장짜리는 자르지 않아 키 그대로 색인돼 있다.
            var single = _catalog.Find(projectileKey);
            if (single != null) return new[] { single };

            var frames = new List<Sprite>();
            for (int i = 0; ; i++)
            {
                var frame = _catalog.Find($"{projectileKey}_{i}");
                if (frame == null) break;
                frames.Add(frame);
            }

            return frames.Count > 0 ? frames.ToArray() : null;
        }

        private readonly List<WeaponFx> _weaponFx = new List<WeaponFx>();

        private const float SwingSeconds = 0.22f;
        private const float ProjectileSeconds = 0.16f;

        /// <summary>휘두르는 호의 각도(도). 너무 크면 팔이 한 바퀴 도는 것처럼 보인다.</summary>
        private const float SwingArcDegrees = 110f;

        /// <summary>무기가 유닛 중심에서 떨어져 도는 반지름.</summary>
        private const float SwingRadius = 0.34f;

        /// <summary>
        /// 무기를 <b>캐릭터 뒤에서</b> 휘두른다.
        /// </summary>
        /// <remarks>
        /// 지시(사용자): "근접 캐릭터들은 주먹이나 어울리는 무기를 휘두르게, 플레이어보다 레이어는 아래로."
        /// <para>
        /// ★ <c>sortingOrder</c> 를 유닛(0)보다 <b>낮게</b> 둔다. 앞에 그리면 무기가 얼굴을 가려
        /// <b>누가 때리는지</b>가 안 보인다 — 이 게임에서 읽어야 하는 건 무기가 아니라 유닛이다.
        /// </para>
        /// </remarks>
        private void SpawnSwing(string weaponKey, Vector3 from, Vector3 to, UnitView owner)
        {
            var sprite = _catalog.Find(weaponKey);
            if (sprite == null) return;

            var go = new GameObject("SwingFx");
            go.transform.SetParent(transform, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = -2;   // 유닛(0)보다 뒤

            var size = sprite.bounds.size;
            float scale = size.y > 0f ? 0.55f / size.y : 1f;
            go.transform.localScale = Vector3.one * scale;

            var dir = to - from;
            float baseAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            // ★ 첫 프레임 자리를 여기서 잡는다. Tick 에만 맡기면 생성된 프레임 동안
            //   판 원점(0,0)에 한 번 찍혔다가 제자리로 튄다.
            float startRad = (baseAngle + SwingArcDegrees * 0.5f) * Mathf.Deg2Rad;
            go.transform.position = from
                + new Vector3(Mathf.Cos(startRad), Mathf.Sin(startRad), 0f) * SwingRadius
                + new Vector3(0f, 0f, 0.05f);

            _weaponFx.Add(new WeaponFx
            {
                Transform = go.transform,
                Left = SwingSeconds,
                Total = SwingSeconds,
                From = from,
                To = to,
                IsSwing = true,
                BaseAngle = baseAngle,
                Owner = owner,
            });
        }

        /// <summary>
        /// 투사체를 표적 쪽으로 날린다.
        /// </summary>
        /// <remarks>
        /// ★ <b>도착을 기다렸다가 피해를 적용하지 않는다.</b> 피해는 이미 로그에 적혀 있고
        /// 재생이 전투 진행을 늦추면 <c>sim</c> 결과와 게임 결과가 갈라진다(`08` §5.5).
        /// 비행 시간을 0.16초로 짧게 잡아 "맞았는데 화살이 늦게 온다" 가 눈에 안 띄게 한다.
        /// </remarks>
        private void SpawnProjectile(string projectileKey, Vector3 from, Vector3 to)
        {
            var frames = LoadProjectileFrames(projectileKey);
            if (frames == null) return;

            var style = StyleOf(projectileKey);

            var go = new GameObject("ProjectileFx");
            go.transform.SetParent(transform, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = frames[0];
            sr.sortingOrder = -2;   // 유닛보다 뒤 — 휘두르기와 같은 층

            var size = frames[0].bounds.size;
            float scale = size.x > 0f ? 0.42f / Mathf.Max(size.x, size.y) : 1f;
            go.transform.localScale = Vector3.one * scale;

            // ★ 겨눈 각도에서 <b>그림이 원래 향하는 각도를 뺀다.</b> 그냥 겨눈 각도를 주면
            //   45° 로 그려진 화살이 45° 더 들린다 — 전에 그렇게 날아갔다.
            var dir = to - from;
            float aim = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            float angle = style.SpinPerSecond != 0f ? 0f : aim - style.NativeAngle;
            go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            go.transform.position = from + new Vector3(0f, 0f, 0.05f);   // 휘두르기와 같은 이유

            _weaponFx.Add(new WeaponFx
            {
                Transform = go.transform,
                Left = ProjectileSeconds,
                Total = ProjectileSeconds,
                From = from,
                To = to,
                IsSwing = false,
                Renderer = sr,
                Frames = frames,
                SpinPerSecond = style.SpinPerSecond,
                Angle = angle,
            });
        }

        /// <summary>휘두르기·투사체를 진행하고 끝난 것을 없앤다. 재생기가 매 프레임 부른다.</summary>
        public void TickWeaponFx(float deltaTime)
        {
            for (int i = _weaponFx.Count - 1; i >= 0; i--)
            {
                var fx = _weaponFx[i];
                fx.Left -= deltaTime;

                if (fx.Left <= 0f || fx.Transform == null)
                {
                    if (fx.Transform != null) Object.Destroy(fx.Transform.gameObject);
                    _weaponFx.RemoveAt(i);
                    continue;
                }

                float t = 1f - fx.Left / fx.Total;

                if (fx.IsSwing)
                {
                    // ★ 주인이 살아 움직이면 <b>손을 따라간다.</b> 시작 위치만 붙들면
                    //   유닛이 다음 칸으로 미끄러지는 동안 무기만 원래 칸에서 휘둘러진다.
                    if (fx.Owner != null && fx.Owner.Root != null)
                        fx.From = fx.Owner.Root.transform.position;

                    // 표적 쪽을 향해 호를 그린다 — 뒤에서 앞으로 베어 나가는 느낌.
                    float angle = fx.BaseAngle + Mathf.Lerp(SwingArcDegrees * 0.5f, -SwingArcDegrees * 0.5f, t);
                    float rad = angle * Mathf.Deg2Rad;
                    fx.Transform.position = fx.From
                        + new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * SwingRadius
                        + new Vector3(0f, 0f, 0.05f);
                    fx.Transform.rotation = Quaternion.Euler(0f, 0f, angle - 90f);
                }
                else
                {
                    fx.Transform.position = Vector3.Lerp(fx.From, fx.To, t) + new Vector3(0f, 0f, 0.05f);

                    // 표창처럼 방향을 안 맞추는 것은 대신 돈다.
                    if (fx.SpinPerSecond != 0f)
                    {
                        fx.Angle += fx.SpinPerSecond * deltaTime;
                        fx.Transform.rotation = Quaternion.Euler(0f, 0f, fx.Angle);
                    }

                    // 이어 붙은 시트면 날아가는 동안 프레임을 넘긴다.
                    if (fx.Frames != null && fx.Frames.Length > 1 && fx.Renderer != null)
                    {
                        int frame = Mathf.Clamp((int)(t * fx.Frames.Length), 0, fx.Frames.Length - 1);
                        fx.Renderer.sprite = fx.Frames[frame];
                    }
                }
            }
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
                if (unit.HitPunchLeft > 0f) continue;   // 맞은 튐이 이긴다 — 아래 패스가 배율을 쓴다

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

        /// <summary>
        /// 맞은 유닛이 순간 부푼다. <b>종류를 안 가린다</b> — 위 반복문은 애니메이션이 있는 유닛을
        /// <c>continue</c> 로 건너뛰지만, "맞는 그림"은 캐릭터에도 없다.
        /// </summary>
        private void TickHitPunch(float deltaTime)
        {
            foreach (var unit in _units.Values)
            {
                if (unit.HitPunchLeft <= 0f || unit.Sprite == null) continue;

                unit.HitPunchLeft -= deltaTime;

                var t = unit.Sprite.transform;
                if (unit.HitPunchLeft <= 0f)
                {
                    t.localScale = Vector3.one * unit.BaseSpriteScale;
                    continue;
                }

                // 맞은 순간이 가장 크고 곧바로 돌아온다 — 종 모양이면 <b>늦게 커져서</b>
                // 타격과 어긋난 자리에서 부푼 것처럼 보인다.
                float k = unit.HitPunchLeft / HitPunchSeconds;
                t.localScale = Vector3.one * unit.BaseSpriteScale * (1f + HitPunchAmount * k);
            }
        }

        /// <summary>죽은 유닛이 위로 튀며 돌아 <b>완전히</b> 사라진다.</summary>
        /// <remarks>
        /// <para>
        /// 지시(사용자): *"몹 잡았을 때 시체 안사라지고 반투명처리했던데, 남겨둘거면
        /// 용병보다 아래 레이어로 내리고, 아니면 없에는게 맞다고 생각함."*
        /// </para>
        /// <para>
        /// ★ <b>내가 시체를 남긴 근거가 화면의 근거가 아니었다.</b>
        /// *"화면이 유닛을 지우면 「죽었다」와 「화면에서 사라졌다」가 같아져 로그와 대조할 수 없다"* 고
        /// 적었는데, 그건 <b>내가 디버깅할 때의 편의</b>지 플레이어가 볼 이유가 아니다.
        /// 대조할 것이 필요하면 <b>로그를 보면 된다</b> — 그러라고 로그가 있다.
        /// </para>
        /// <para>
        /// 판이 8×6 이고 한 전투에 적이 여럿 죽는다. 반투명 시체는 <c>sortingOrder</c> 가
        /// 산 유닛과 같아서 <b>같은 칸에 두 겹으로 겹친다</b> — core 는 시체를 칸에서 빼므로
        /// 그 자리에 산 유닛이 바로 들어온다. 층을 내려도 겹침은 남고 잡음만 는다.
        /// </para>
        /// <para>
        /// <see cref="UnitView.Root"/> 를 통째로 끈다 — 스프라이트만 끄면 <b>빈 체력바 트랙이
        /// 혼자 떠 있는다.</b> (<see cref="SetDead"/> 가 트랙을 남기는 이유는 시체가 남는다는
        /// 전제 위에 있었고, 통째로 사라지면 그 모호함 자체가 없어진다.)
        /// </para>
        /// </remarks>
        private void TickDeathFade(float deltaTime)
        {
            foreach (var unit in _units.Values)
            {
                if (unit.DeathFadeLeft <= 0f || unit.Sprite == null) continue;

                unit.DeathFadeLeft -= deltaTime;
                float t = 1f - Mathf.Clamp01(unit.DeathFadeLeft / DeathFadeSeconds);

                var st = unit.Sprite.transform;
                st.localPosition = new Vector3(0f, Mathf.Sin(t * Mathf.PI) * DeathHopHeight, 0f);
                st.localRotation = Quaternion.Euler(0f, 0f, (unit.IsAlly ? 1f : -1f) * DeathSpinDegrees * t);

                var c = unit.Sprite.color;
                c.a = 1f - t;
                unit.Sprite.color = c;

                if (unit.DeathFadeLeft <= 0f && unit.Root != null) unit.Root.SetActive(false);
            }
        }

        private const float DeathFadeSeconds = 0.45f;
        private const float DeathHopHeight = 0.22f;
        private const float DeathSpinDegrees = 80f;

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

            // ★ 격자 색은 <b>바뀐 뒤에만</b> 보인다 — 보고 있지 않으면 언제 바뀌었는지 모른다.
            //   진입 순간에 한 번 번쩍여야 "지금부터 다르다"가 사건으로 읽힌다.
            if (on)
            {
                ShowOverlay(OverlayKind.Flash, new Color(0.9f, 0.15f, 0.1f), 0.32f, 0.5f);
                BoardCamera.Shake(SuddenDeathShake, SuddenDeathShakeSeconds);
            }
        }

        private const float SuddenDeathShake = 0.13f;
        private const float SuddenDeathShakeSeconds = 0.35f;

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
