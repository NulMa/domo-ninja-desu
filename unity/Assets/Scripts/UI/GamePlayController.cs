using System.Collections;
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
        private bool _viewReady;
        private int _speedIndex;

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
            if (mgr == null || !mgr.IsRunActive) return;

            EnsureView();
            _startBattleButton.interactable = false;
            StartCoroutine(PlayRoundRoutine(mgr));
        }

        private IEnumerator PlayRoundRoutine(RunManager mgr)
        {
            var outcome = mgr.PlayRound(NullEventSink.Instance, collectLog: true);

            if (outcome?.Log != null)
            {
                _replayer.Play(outcome.Log);
                while (_replayer.IsPlaying) yield return null;
            }

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
