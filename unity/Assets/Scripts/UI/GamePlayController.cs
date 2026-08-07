using System.Collections;
using System.Collections.Generic;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Events;
using DomoNinja.Unity.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UImage = UnityEngine.UI.Image;

namespace DomoNinja.Unity
{
    /// <summary>전투 화면. "전투 시작" 클릭 → 한 라운드를 실제로 플레이하고 보드에 재생한다.</summary>
    /// <remarks>
    /// 런이 끝나면 <see cref="RunManager.EndRun"/> 을 호출하고 결과 화면으로, 아니면 상점으로 넘어간다.
    /// 보드/재생기는 씬에 미리 두지 않고 여기서 코드로 만든다(`View` 쪽 관례와 동일 — `19` §5.1).
    /// </remarks>
    public sealed class GamePlayController : MonoBehaviour
    {
        private static readonly float[] SpeedSteps = { 1f, 2f, 4f };
        private const string CharacterSlotPrefix = "CharacterSlot";

        private Button _startBattleButton;
        private Button _speedButton;
        private TMP_Text _speedLabel;
        private Transform _board;

        private BoardView _boardView;
        private BattleReplayer _replayer;
        private PlacementController _placementController;
        private VariantDef _pendingVariant;
        private bool _viewReady;
        private int _speedIndex;

        /// <summary>라운드 넘어 기억하는 마지막 배치. 다음 라운드 기본값으로 쓴다(`08` §5-5의 편의 규칙).</summary>
        private readonly Dictionary<string, Coord> _lastPlacement = new Dictionary<string, Coord>();

        private void Awake()
        {
            _board = transform.Find("Board");
            _startBattleButton = UITheme.EnsureButton(_board.Find("StartBattleButton").gameObject);
            _speedButton = UITheme.EnsureButton(_board.Find("SpeedButton").gameObject);
            _speedLabel = _board.Find("SpeedButton/Label").GetComponent<TMP_Text>();

            _startBattleButton.onClick.AddListener(OnStartBattle);
            _speedButton.onClick.AddListener(OnCycleSpeed);
        }

        private void OnEnable()
        {
            EnsureView();
            _startBattleButton.interactable = true;
            RefreshCharacterSlots();
            SetupPlacement();
        }

        /// <summary>
        /// 화면이 꺼지는 모든 경로(퇴각 포함)를 여기 하나로 받는다.
        /// </summary>
        /// <remarks>
        /// ★ 배치 조정 중 퇴각하면 <see cref="OnStartBattle"/> 이 안 불려 프리뷰가 안 지워진다 —
        /// <see cref="BoardView"/>·<see cref="PlacementController"/> 는 이 화면과 별개 트리라 씬 루트에
        /// 계속 살아있고, 배치 좌표 사전을 쥔 채 클릭을 계속 받는다. "게임 화면이 꺼졌다"와
        /// "배치 입력이 멈췄다"가 여기서 갈리면 안 되므로 화면 생명주기에 그대로 묶는다.
        /// </remarks>
        private void OnDisable()
        {
            if (_boardView != null) _boardView.ClearPlacementPreview();
            _pendingVariant = null;
        }

        /// <summary>
        /// 적 배치를 공개하고 아군을 배치 조정 가능한 상태로 세운다 (`D-53` "적 배치 공개 → 배치 조정").
        /// </summary>
        /// <remarks>
        /// <see cref="RunManager.PeekVariant"/> 는 라운드당 한 번만 RNG 를 소비하고 결과를 캐시한다 —
        /// 여기서 부른 걸 <see cref="OnStartBattle"/> 이 그대로 이어받아 같은 변형으로 전투를 돌린다.
        /// </remarks>
        private void SetupPlacement()
        {
            var mgr = RunManager.Instance;
            if (mgr == null || !mgr.IsRunActive || mgr.Data == null) return;

            _pendingVariant = mgr.PeekVariant();
            if (_pendingVariant == null) return;

            // 지난 라운드 전투 잔해(사망 이펙트 등)가 배치 화면까지 남아있지 않게.
            _boardView.Clear();

            // ★ 전장은 **이번 라운드 편성이 정해진 뒤에** 고른다 — 보스가 나오는 라운드인지를
            //   알아야 보스 전용 전장을 쓸 수 있고, 그건 변형을 뽑아봐야 안다.
            _boardView.SetField(mgr.CurrentRun.StageId, HasBoss(mgr.Data, _pendingVariant));

            // 무기 연출은 **고른 빌드**가 정한다 — 라운드마다 다시 계산한다(상점에서 스킬을 사면 바뀐다).
            _boardView.RangedTypeIds = BuildRangedTypeIds(mgr);
            _boardView.ActiveSkillNames = BuildActiveSkillNames(mgr);

            var aliveIds = new List<string>();
            foreach (var entry in mgr.CurrentRun.Deployed)
                if (entry.IsAlive) aliveIds.Add(entry.CharacterId);

            if (_placementController == null)
                _placementController = _boardView.gameObject.AddComponent<PlacementController>();

            _placementController.Setup(_boardView, aliveIds, _lastPlacement, _pendingVariant.Units);
        }

        /// <summary>
        /// 투사체를 쓰는 유닛 종류를 모은다 — <b>기본 사거리 + 산 액티브 스킬</b>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 지시(사용자): *"적영 같은 경우엔 근거리는 검, 표창 찍으면 표창만."*
        /// 무기를 정하는 건 공격 순간의 거리가 아니라 <b>고른 빌드</b>다 —
        /// 표창을 산 적영은 적이 붙어도 표창을 던져야 한다.
        /// </para>
        /// <para>
        /// 판정 근거는 둘 다 데이터에 있다:
        /// <c>characters.json</c> 의 기본 <c>range</c>(C1~C3 은 1, C4~C6 은 3~5)와
        /// 액티브 스킬 효과의 <c>setRange</c>(`C3-B 표창` → 4, `C4-A 저격` → 7).
        /// <b>숫자를 코드에 박지 않는다</b> — 밸런스가 사거리를 바꾸면 연출도 따라온다.
        /// </para>
        /// </remarks>
        private static HashSet<string> BuildRangedTypeIds(RunManager mgr)
        {
            var set = new HashSet<string>();
            var run = mgr.CurrentRun;
            if (run == null || mgr.Data == null) return set;

            foreach (var entry in run.Deployed)
            {
                var def = mgr.Data.FindCharacter(entry.CharacterId);
                if (def == null) continue;

                int range = def.Range;

                var active = entry.ActiveSkillId != null ? mgr.Data.FindSkill(entry.ActiveSkillId) : null;
                if (active != null) range = Mathf.Max(range, SetRangeOf(active));

                if (range > 1) set.Add(entry.CharacterId);
            }

            return set;
        }

        /// <summary>
        /// 캐릭터 종류 → 그 캐릭터가 고른 <b>액티브 스킬 이름</b>. 화면이 <c>SkillCast</c> 때 띄운다.
        /// </summary>
        /// <remarks>
        /// ★ <b>이름을 이벤트에 싣지 않은 이유가 여기 있다.</b> 한 유닛의 액티브는 전투 내내 하나라
        /// core 는 <b>"누가"</b>만 알리면 되고, 이름은 이미 로스터를 들고 있는 이쪽이 안다.
        /// 이벤트에 문자열을 실으면 틱마다 할당이 생겨 <c>GameEvent</c> 를 struct 로 둔 이유가 사라진다.
        /// <para>
        /// <see cref="BuildRangedTypeIds"/> 와 같은 자리에서 같은 로스터를 읽는다 — 둘을 떼어 놓으면
        /// 한쪽만 갱신되는 라운드가 생기고, 그러면 <b>표창을 던지는데 이름은 그림자</b>가 뜬다.
        /// </para>
        /// </remarks>
        private static Dictionary<string, string> BuildActiveSkillNames(RunManager mgr)
        {
            var map = new Dictionary<string, string>();
            var run = mgr.CurrentRun;
            if (run == null || mgr.Data == null) return map;

            foreach (var entry in run.Deployed)
            {
                if (entry.ActiveSkillId == null) continue;

                var skill = mgr.Data.FindSkill(entry.ActiveSkillId);
                if (skill?.Name == null) continue;

                map[entry.CharacterId] = skill.Name;
            }

            return map;
        }

        /// <summary>스킬 효과에서 <c>setRange</c> 를 찾는다. 없으면 0 — 기본 사거리가 그대로 쓰인다.</summary>
        private static int SetRangeOf(SkillDef skill)
        {
            int best = 0;
            foreach (var token in skill.Effects)
            {
                var value = token?["setRange"];
                if (value != null) best = Mathf.Max(best, (int)value);
            }
            return best;
        }

        /// <summary>이번에 뽑힌 편성에 보스가 있는가. <b>저작된 변형 전체가 아니라 뽑힌 것</b>만 본다.</summary>
        /// <remarks>
        /// 라운드 단위로 "보스 라운드인가"를 물으면(<c>RoundDef.Variants</c> 전수 검사) 보스가 안 뽑힌
        /// 변형에서도 보스 전장이 깔린다 — 판은 보스인데 나오는 건 슬라임이 된다.
        /// </remarks>
        private static bool HasBoss(GameData data, VariantDef variant)
        {
            if (variant == null) return false;

            foreach (var unit in variant.Units)
                if (data.EnemyTypes.TryGetValue(unit.Type, out var def) && def.IsBoss)
                    return true;

            return false;
        }

        private void EnsureView()
        {
            if (_viewReady) return;

            var mgr = RunManager.Instance;
            if (mgr == null || !mgr.IsDataLoaded) return;

            SetupCamera();

            var boardGo = new GameObject("BattleBoard");
            _boardView = boardGo.AddComponent<BoardView>();
            _replayer = boardGo.AddComponent<BattleReplayer>();
            _replayer.Bind(_boardView);
            _boardView.Initialize(Resources.Load<SpriteCatalog>(SpriteCatalog.ResourceName),
                                   BoardView.SpritePathsFrom(mgr.Data),
                                   BoardView.BossTypeIdsFrom(mgr.Data));

            _viewReady = true;
        }

        /// <summary>
        /// 보드가 짚어준 유닛(<see cref="BoardView.HoveredUnitId"/>)을 툴팁으로 옮긴다.
        /// </summary>
        /// <remarks>
        /// 뷰는 "무엇에 올려져 있는가"까지만 안다 — 무엇을 보여줄지는 런 상태를 아는 이쪽이 정한다.
        /// 같은 유닛에 계속 올려둔 동안에도 매 프레임 다시 만든다: <b>전투 중 체력이 변한다.</b>
        /// </remarks>
        /// <remarks>
        /// ★ <b>전투 화면이 실제로 앞에 있을 때만 짚는다.</b> <c>StageIntro</c>·<c>Shop</c> 은
        /// <c>GamePlay</c> <b>위에</b> 뜨는데, 그동안에도 <c>BoardView</c> 는 뒤에서 배치 유닛을
        /// 계속 짚고 있었다. 그 결과 <b>스테이지 인트로 화면에서 있지도 않은 용병 스펙이 떴고</b>,
        /// 적 초상화에 올려 뜬 툴팁도 여기서 매 프레임 지워졌다.
        /// <c>PlacementController</c> 가 입력을 막을 때 쓰는 것과 같은 조건이다.
        /// </remarks>
        private void Update()
        {
            if (_boardView == null) return;

            if (!UIScreenManager.IsActive("GamePlay") || UIScreenManager.IsActive("StageIntro"))
            {
                UnitTooltip.Hide(this);
                return;
            }

            string text = DescribeHovered(_boardView.Hovered);
            if (string.IsNullOrEmpty(text)) UnitTooltip.Hide(this);
            else UnitTooltip.Show(this, text);
        }

        /// <summary>
        /// 마우스를 올린 유닛의 스펙. 아군이면 <b>산 것까지</b> 붙는다.
        /// </summary>
        /// <remarks>
        /// <c>TypeId</c> 는 아군이면 캐릭터 id(<c>C1</c>..), 적이면 적 타입 키다 —
        /// <c>BattleSetup</c> 과 배치 미리보기가 둘 다 그렇게 넣는다. 값 하나로 양쪽을 가른다.
        /// <para>
        /// 배치 화면에는 아직 전투 체력이 없다(<c>HasLiveHp == false</c>). 그때는 로스터의
        /// 누적 체력(<see cref="RosterEntry.Hp"/>)이 맞는 값이다 — 체력은 라운드를 넘어 누적된다(`A-6`).
        /// </para>
        /// </remarks>
        private string DescribeHovered(BoardView.HoverTarget target)
        {
            if (target.TypeId == null) return null;

            var mgr = RunManager.Instance;
            if (mgr == null || mgr.Data == null) return null;

            if (!target.IsAlly)
            {
                return mgr.Data.EnemyTypes.TryGetValue(target.TypeId, out var enemy)
                    ? UnitStatText.ForEnemy(enemy, target.HasLiveHp ? target.Hp : (int?)null)
                    : null;
            }

            var def = mgr.Data.FindCharacter(target.TypeId);
            if (def == null) return null;

            // 런이 없으면(관전 뷰) 기본 스펙까지만 — 산 것을 물어볼 곳이 없다.
            var run = mgr.CurrentRun;
            if (run == null) return UnitStatText.ForCharacter(def, target.HasLiveHp ? target.Hp : (int?)null);

            RosterEntry entry = null;
            foreach (var e in run.Deployed)
                if (e.CharacterId == target.TypeId) { entry = e; break; }

            if (entry == null)
                return UnitStatText.ForCharacter(def, target.HasLiveHp ? target.Hp : (int?)null);

            return UnitStatText.ForDeployedAlly(def, entry, mgr.Data,
                                                target.HasLiveHp ? target.Hp : (int?)null);
        }

        /// <summary>
        /// 보드 8×6 이 한 화면에 들어오게.
        /// </summary>
        /// <remarks>
        /// 전에는 <c>BattleViewBootstrap</c> 과 <b>같은 값을 여기에도 적어뒀다.</b>
        /// 한쪽만 고치면 <b>어느 화면에서 들어왔느냐에 따라 프레이밍이 달라지고</b>, 그건 눈으로 못 찾는다.
        /// 이제 <see cref="BoardCamera"/> 한 벌만 본다.
        /// </remarks>
        private static void SetupCamera() => BoardCamera.Frame(Camera.main);

        private void OnStartBattle()
        {
            var mgr = RunManager.Instance;
            if (mgr == null || !mgr.IsRunActive || _placementController == null || _pendingVariant == null) return;

            EnsureView();
            _startBattleButton.interactable = false;

            // 이번 배치를 기억해둔다 — 다음 라운드 기본값이 여기서 나온다.
            var placement = new Dictionary<string, Coord>();
            foreach (var kv in _placementController.Placement)
            {
                placement[kv.Key] = kv.Value;
                _lastPlacement[kv.Key] = kv.Value;
            }

            _boardView.ClearPlacementPreview();

            StartCoroutine(PlayRoundRoutine(mgr, placement));
        }

        private IEnumerator PlayRoundRoutine(RunManager mgr, IReadOnlyDictionary<string, Coord> placement)
        {
            var outcome = mgr.PlayRoundWithPlacement(placement, NullEventSink.Instance, collectLog: true);
            _pendingVariant = null;

            if (outcome?.Log != null)
            {
                _replayer.Play(outcome.Log);
                while (_replayer.IsPlaying) yield return null;
            }

            // ★ 전에는 여기서 곧장 Clear() 했다. 그래서 **마지막 적이 죽은 그 프레임에 판이
            //   사라지고** 승리 팝업이 올라왔다 — "이겼다"를 볼 시간이 아예 없어서
            //   전투가 끝난 실감이 안 났다(사용자 지적).
            //   판을 잠깐 두고, 그 시간에 살아남은 아군이 뛴다.
            if (outcome != null && outcome.Won)
            {
                // 환호와 같은 프레임에 낸다 — 소리가 늦으면 이미 뛰고 있는 것을 뒤늦게 설명하는 꼴이 된다.
                AudioManager.Instance?.PlaySfx(AudioKeys.Victory);
                _boardView.StartVictoryCheer();
                yield return new WaitForSeconds(BoardView.VictoryCheerSeconds);
            }

            // 다음 화면(상점/결과)으로 넘어가기 전에 보드를 비운다 —
            // 안 비우면 world-space 유닛 스프라이트가 그 화면 위에 그대로 남아 겹쳐 보인다.
            _boardView.Clear();

            var run = mgr.CurrentRun;
            bool runOver = run == null || run.IsOver || run.Round > mgr.Data.Economy.TotalRounds;

            if (runOver)
            {
                mgr.EndRun();

                // ★ 라운드 승리음과 **다른 소리**를 낸다. 라운드는 8번 이기고 스테이지는 한 번 깬다 —
                //   같은 소리면 마지막 한 번이 그냥 아홉 번째로 들린다.
                //   진 채로 끝났을 때 소리가 없으면 <b>화면만 바뀌고 아무 일도 안 일어난 것처럼</b> 보인다.
                AudioManager.Instance?.PlaySfx(
                    outcome != null && outcome.Won ? AudioKeys.StageClear : AudioKeys.Defeat);

                UIScreenManager.ShowPopup("Result");
                yield break;
            }

            mgr.EnsureShopRestocked();
            BattleVictoryPopup.Show(mgr.LastRoundCurrencyGained, () => UIScreenManager.ShowScreen("Shop"));
        }

        private void OnCycleSpeed()
        {
            _speedIndex = (_speedIndex + 1) % SpeedSteps.Length;
            float speed = SpeedSteps[_speedIndex];
            if (_replayer != null) _replayer.Speed = speed;
            _speedLabel.text = $"x{speed:0.#}";
        }

        private void RefreshCharacterSlots()
        {
            var mgr = RunManager.Instance;
            var deployed = mgr != null ? mgr.CurrentRun?.Deployed : null;

            for (int i = 0; i < 3; i++)
            {
                var slot = _board.Find(CharacterSlotPrefix + (i + 1));
                if (slot == null) continue;

                string characterId = deployed != null && i < deployed.Count ? deployed[i].CharacterId : null;
                RefreshSlotFace(slot, characterId);
            }
        }

        private void RefreshSlotFace(Transform slot, string characterId)
        {
            var face = slot.Find("Face");

            if (characterId == null)
            {
                if (face != null) face.gameObject.SetActive(false);
                return;
            }

            if (face == null)
            {
                var faceGo = new GameObject("Face", typeof(RectTransform));
                faceGo.transform.SetParent(slot, false);
                var rt = faceGo.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(4, 4);
                rt.offsetMax = new Vector2(-4, -4);
                faceGo.AddComponent<UImage>().preserveAspect = true;
                face = faceGo.transform;
            }

            face.gameObject.SetActive(true);

            var mgr = RunManager.Instance;
            var catalog = Resources.Load<SpriteCatalog>(SpriteCatalog.ResourceName);
            var def = mgr != null && mgr.Data != null ? mgr.Data.FindCharacter(characterId) : null;
            face.GetComponent<UImage>().sprite = def != null && catalog != null ? catalog.Find(def.Sprite) : null;
        }    }
}
