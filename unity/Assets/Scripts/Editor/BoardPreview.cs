using System.Collections.Generic;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Events;
using DomoNinja.Unity.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DomoNinja.Unity.Editor
{
    /// <summary>
    /// 편집 모드에서 8×6 보드를 임시로 그린다. <b>씬 파일에는 저장되지 않는다.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 보드는 코드가 만든다 — 씬에 늘어놓으면 병합 불가능한 씬 파일이 커지기 때문이다(`19` §5.1).
    /// 그 결정은 그대로 두되, <b>UI 를 보드 위에 올려야 하는 사람에게는 보드가 안 보인다</b>는 문제가 남는다.
    /// 전투 화면의 패널·버튼이 보드를 가리는지는 플레이 모드에 들어가야만 알 수 있었고,
    /// 플레이 중에 옮긴 위치는 저장되지 않는다.
    /// </para>
    /// <para>
    /// ★ <b><see cref="HideFlags.DontSave"/> 로 만든다.</b> 씬을 저장해도 이 오브젝트는 파일에 안 들어간다 —
    /// "미리보기를 지우는 걸 깜빡하고 커밋" 이 구조적으로 불가능해야 한다.
    /// 같은 이유로 플레이 모드에 들어갈 때 자동으로 지운다. 남아 있으면 진짜 보드와 두 겹으로 겹친다.
    /// </para>
    /// <para>
    /// 격자는 <see cref="BoardView"/> 가 그린다. 여기서 좌표·칸 크기를 다시 계산하면 그게 두 번째 사본이 되고,
    /// 언젠가 한쪽만 바뀐다 — <b>미리보기가 실제와 다른 자리를 보여주는 것이 미리보기가 없는 것보다 나쁘다.</b>
    /// </para>
    /// </remarks>
    public static class BoardPreview
    {
        private const string RootName = "~보드 미리보기 (저장 안 됨)";

        [InitializeOnLoadMethod]
        private static void Hook() => EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) Clear();
        };

        /// <summary>
        /// 씬 카메라를 <b>게임이 실행 중에 쓰는 값</b>으로 맞춘다.
        /// </summary>
        /// <remarks>
        /// <see cref="BattleViewBootstrap.SetupCamera"/> 가 시작할 때 카메라를 덮어쓰므로,
        /// 씬에 저장된 카메라 설정은 게임 동작에 영향을 주지 않는다. 대신 <b>편집 모드에서만</b> 보인다 —
        /// 즉 맞춰두지 않으면 <b>에디터에서 UI 를 얹어보는 배경이 실제와 다른 화면</b>이 된다.
        /// 실제로 그랬다: 씬은 원근 + 스카이박스, 게임은 직교 + 어두운 단색이었다.
        /// </remarks>
        [MenuItem("DomoNinja/카메라를 게임과 같게 맞추기", false, 22)]
        public static void MatchCameraToRuntime()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                Debug.LogWarning("[BoardPreview] Main Camera 가 없다 — 맞출 대상이 없다.");
                return;
            }

            Undo.RecordObject(camera, "카메라를 게임과 같게");
            Undo.RecordObject(camera.transform, "카메라를 게임과 같게");
            BattleViewBootstrap.SetupCamera();
            EditorUtility.SetDirty(camera);
        }

        [MenuItem("DomoNinja/보드 미리보기 켜기", false, 20)]
        public static void Create()
        {
            Clear();

            // 원근 카메라 아래에서 그린 격자는 실제와 다른 자리에 보인다.
            // 미리보기가 거짓말을 하느니 카메라를 먼저 맞춘다.
            MatchCameraToRuntime();

            var root = new GameObject(RootName) { hideFlags = HideFlags.DontSave };
            var board = root.AddComponent<BoardView>();
            board.Initialize(Resources.Load<SpriteCatalog>(SpriteCatalog.ResourceName));

            AddSampleUnits(board);

            MarkThrowaway(root.transform);
            Selection.activeGameObject = root;
            SceneView.FrameLastActiveSceneView();
        }

        /// <summary>
        /// 체력이 <b>서로 다른</b> 표본 유닛 몇을 세운다.
        /// </summary>
        /// <remarks>
        /// ★ 격자만 그리면 <b>체력바를 편집 모드에서 볼 방법이 없다.</b> 유닛은 전투가 돌아야 생기고,
        /// 전투는 런을 시작해야 돌기 때문이다. 그래서 체력바를 고칠 때마다 플레이 → 런 시작 →
        /// 라운드 진입까지 가야 했고, 그렇게 확인한 것은 <b>저장되지 않는다.</b>
        /// <para>
        /// 체력을 100/60/25/0 으로 벌려 세우는 이유 — 한 값만 보면 <b>"막대가 한쪽에서 닳는가"</b>를
        /// 확인할 수 없다. 실제로 예전 막대는 가운데에서 양쪽으로 줄고 있었고 그게 이 방식으로 드러났다.
        /// </para>
        /// </remarks>
        private static void AddSampleUnits(BoardView board)
        {
            var units = new List<UnitSpec>
            {
                new UnitSpec(1, 0, "C1", 100, new Coord(1, 1).OrderKey),
                new UnitSpec(2, 0, "C2", 100, new Coord(1, 3).OrderKey),
                new UnitSpec(3, 1, "slime", 100, new Coord(6, 1).OrderKey),
                new UnitSpec(4, 1, "slime", 100, new Coord(6, 3).OrderKey),
            };

            board.Setup(new BattleLog(1, 1, 1UL, units,
                new List<GameEvent> { new GameEvent(EventKind.RoundEnd, 0, 0, 0, 0) }));

            board.SetHp(1, 100);
            board.SetHp(2, 60);
            board.SetHp(3, 25);
            board.SetDead(4);

            // 상태 표시도 같이 세운다. 이것들은 **전투 중 특정 순간에만** 나오는데,
            // 그 순간을 만들려면 런을 시작해 해당 스킬이 터질 때까지 기다려야 한다.
            board.SetShield(2, 40);
            board.SetTaunt(1, true);
        }

        /// <summary>
        /// 미리보기를 지운다. 없으면 아무 일도 안 한다.
        /// </summary>
        /// <remarks>
        /// ★ <b><see cref="Object.FindObjectsByType{T}(FindObjectsInactive)"/> 로는 못 찾는다.</b>
        /// 그 API 는 <see cref="HideFlags.DontSave"/> 가 붙은 오브젝트를 <b>결과에서 빼기 때문</b>이고,
        /// 이 미리보기는 정확히 그 플래그로 만들어진다. 처음에 그렇게 짰다가 실제로 안 지워지는 걸 확인했다 —
        /// 에러도 경고도 없이 "지웠다"고 로그만 남는 종류의 실패다.
        /// <para>
        /// 그래서 씬의 루트 오브젝트를 직접 훑는다. 정적 필드에 참조를 들고 있는 방법은
        /// <b>도메인 리로드에서 날아가므로</b> 안 된다 — 스크립트를 고칠 때마다 미리보기가 미아가 된다.
        /// </para>
        /// </remarks>
        [MenuItem("DomoNinja/보드 미리보기 끄기", false, 21)]
        public static void Clear()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                    if (root.name == RootName)
                        Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 자식과 머티리얼까지 전부 "저장 안 함"으로 표시한다.
        /// </summary>
        /// <remarks>
        /// 루트에만 걸면 <see cref="BoardView"/> 가 코드로 만든 칸 48개와 그 머티리얼이 남아,
        /// 도메인 리로드 때 "누수된 오브젝트" 경고가 콘솔을 채운다. 경고가 흔해지면 진짜 경고를 못 본다.
        /// </remarks>
        private static void MarkThrowaway(Transform node)
        {
            node.gameObject.hideFlags = HideFlags.DontSave;

            var renderer = node.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.sharedMaterial != null)
                renderer.sharedMaterial.hideFlags = HideFlags.DontSave;

            for (int i = 0; i < node.childCount; i++)
                MarkThrowaway(node.GetChild(i));
        }
    }
}
