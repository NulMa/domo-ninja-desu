using System.Collections;
using System.Collections.Generic;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Events;
using DomoNinja.Unity.View;
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
        private Text _speedLabel;
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
            _startBattleButton = EnsureButton(_board.Find("StartBattleButton").gameObject);
            _speedButton = EnsureButton(_board.Find("SpeedButton").gameObject);
            _speedLabel = _board.Find("SpeedButton/Label").GetComponent<Text>();

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

            var aliveIds = new List<string>();
            foreach (var entry in mgr.CurrentRun.Deployed)
                if (entry.IsAlive) aliveIds.Add(entry.CharacterId);

            if (_placementController == null)
                _placementController = _boardView.gameObject.AddComponent<PlacementController>();

            _placementController.Setup(_boardView, aliveIds, _lastPlacement, _pendingVariant.Units);
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
                                   BoardView.SpritePathsFrom(mgr.Data));

            _viewReady = true;
        }

        /// <summary>보드 8×6 이 한 화면에 들어오게. `BattleViewBootstrap` 과 같은 값이다.</summary>
        private static void SetupCamera()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                camera = new GameObject("Main Camera").AddComponent<Camera>();
                camera.tag = "MainCamera";
            }

            camera.orthographic = true;
            camera.orthographicSize = 4.2f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            camera.clearFlags = CameraClearFlags.SolidColor;
        }

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

            // 재생이 끝나면 다음 화면(상점/결과)으로 넘어가기 전에 보드를 비운다 —
            // 안 비우면 world-space 유닛 스프라이트가 그 화면 위에 그대로 남아 겹쳐 보인다.
            _boardView.Clear();

            var run = mgr.CurrentRun;
            bool runOver = run == null || run.IsOver || run.Round > mgr.Data.Economy.TotalRounds;

            if (runOver)
            {
                mgr.EndRun();
                UIScreenManager.ShowPopup("Result");
                yield break;
            }

            mgr.EnsureShopRestocked();
            UIScreenManager.ShowScreen("Shop");
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
