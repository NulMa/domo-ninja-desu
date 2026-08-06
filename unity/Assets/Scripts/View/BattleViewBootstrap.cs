using System.Collections;
using System.Linq;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using DomoNinja.Core.Economy;
using DomoNinja.Core.Events;
using DomoNinja.Core.Rng;
using UnityEngine;

namespace DomoNinja.Unity.View
{
    /// <summary>
    /// 씬 진입점. <b>데이터를 읽고 전투를 돌려 화면에 재생한다.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 이 컴포넌트 하나만 씬에 있으면 된다. 나머지는 전부 코드가 만든다 (`19` §5.1).
    /// 씬 파일에 오브젝트를 늘어놓으면 병합할 수 없는 바이너리가 커지고,
    /// 2인이 같은 씬을 건드리는 순간 손으로 풀 수 없는 충돌이 난다.
    /// </para>
    /// <para>
    /// ⚠️ <b>지금은 관전 전용이다.</b> 로스터 선택·상점·배치는 `UI`(P5) 몫이고,
    /// 여기서는 봇이 고른 빌드로 런을 끝까지 돌린 뒤 라운드를 차례로 재생한다.
    /// 그래도 이게 필요한 이유 — <b>화면이 없으면 "돌아간다"와 "돌아간다고 믿는다"가 구분되지 않는다.</b>
    /// </para>
    /// </remarks>
    public sealed class BattleViewBootstrap : MonoBehaviour
    {
        [Tooltip("0 이면 1 을 쓴다. 같은 값이면 언제나 같은 전투가 재생된다.")]
        [SerializeField] private long _seed = 1;

        [Tooltip("빌드 공간(4,320) 에서 몇 번째 빌드로 돌릴지.")]
        [SerializeField] private int _buildIndex;

        [SerializeField] private string _stage = "S1";

        [Tooltip("라운드 사이에 쉬는 시간(초).")]
        [SerializeField] private float _roundGap = 1.2f;

        private BoardView _board;
        private BattleReplayer _replayer;

        private IEnumerator Start()
        {
            SetupCamera();

            _board = new GameObject("Board").AddComponent<BoardView>();
            _board.transform.SetParent(transform, false);

            _replayer = gameObject.AddComponent<BattleReplayer>();
            _replayer.Bind(_board);

            GameData data = null;
            yield return StreamingGameData.LoadAsync(
                loaded => data = loaded,
                error => Debug.LogError($"[View] 데이터 로드 실패 — {error}"));

            // ★ 보드 초기화를 데이터 로드 **뒤로** 옮겼다. 스프라이트 경로를 데이터에서 받아야 하는데,
            //   앞에서 만들면 넘길 것이 없어 View 가 이름으로 추측하게 된다 — 적 4종이 그렇게 사라졌다.
            //   데이터가 없어도 격자는 그린다: "데이터를 못 읽었다" 와 "화면이 안 떴다" 는 다른 사고다.
            _board.Initialize(Resources.Load<SpriteCatalog>(SpriteCatalog.ResourceName),
                              BoardView.SpritePathsFrom(data));

            if (data == null) yield break;

            yield return PlayRun(data);
        }

        private IEnumerator PlayRun(GameData data)
        {
            var config = CombatConfig.From(data.Economy, 20);
            var engine = new RunEngine(data, config);
            var meta = new MetaProgress(data.Meta);

            var build = BuildSpace.Enumerate(data).Skip(Mathf.Max(0, _buildIndex)).FirstOrDefault();
            if (build == null)
            {
                Debug.LogError("[View] 빌드를 찾지 못했다.");
                yield break;
            }

            ulong seed = _seed != 0 ? (ulong)_seed : 1UL;
            var run = engine.StartRun(_stage, build.CharacterIds, meta);

            // 로그를 켜고 돌린다. sim 은 끄지만(처리량) 여기서는 그게 재생 대상이다.
            var summary = engine.PlayRun(run, meta, new DeterministicRandom(seed),
                                         NullEventSink.Instance, collectLogs: true, build: build);

            Debug.Log($"[View] {build.Id} · 시드 {seed} · " +
                      $"{(summary.Cleared ? "클리어" : "실패")} {summary.RoundsWon}/{summary.RoundsReached}승");

            foreach (var outcome in summary.Rounds)
            {
                if (outcome.Log == null) continue;

                Debug.Log($"[View] 라운드 {outcome.Round} ({outcome.VariantId}) 재생 — " +
                          $"{outcome.Ticks / 20f:F1}초 · {(outcome.Won ? "승" : "패")}");

                _replayer.Play(outcome.Log);
                while (_replayer.IsPlaying) yield return null;

                yield return new WaitForSeconds(_roundGap);
            }

            Debug.Log("[View] 재생 완료");
        }

        /// <summary>보드 8×6 이 한 화면에 들어오게. 세로 대치가 아니라 좌우 대치라 가로가 길다.</summary>
        /// <remarks>
        /// ★ <c>public</c> 인 이유는 <b>에디터가 같은 값을 쓰기 위해서</b>다.
        /// 이 값들은 실행할 때 카메라에 덮어씌워지므로, 씬에 저장된 카메라가 무엇이든 게임은 이렇게 돈다.
        /// 그 말은 <b>편집 모드에서 보는 화면이 실제 화면과 다를 수 있다</b>는 뜻이다 —
        /// 실제로 씬 카메라는 원근(perspective)에 스카이박스였고 게임은 직교(orthographic)에 어두운 단색이다.
        /// UI 를 그 위에 얹는 사람에게는 배경색도 프레이밍도 판단 재료라서, 에디터 메뉴가 이 함수를 호출해
        /// 씬 카메라를 같은 값으로 맞춘다. 값을 에디터 쪽에 복사해두면 언젠가 한쪽만 바뀐다.
        /// </remarks>
        public static void SetupCamera()
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
    }
}
