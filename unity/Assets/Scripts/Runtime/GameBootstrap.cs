using System.Linq;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Economy;
using DomoNinja.Core.Events;
using DomoNinja.Core.Rng;
using UnityEngine;

namespace DomoNinja.Unity
{
    /// <summary>
    /// 씬 진입점. P0 시점에는 <b>경계가 실제로 성립하는지 확인하는 역할만</b> 한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 이 스크립트가 컴파일된다는 것 자체가 게이트 3 의 통과 조건이다 —
    /// Unity 가 core 를 로컬 UPM 패키지로 참조해 컴파일했다는 뜻이기 때문이다.
    /// 빈 asmdef 만으로는 확인되지 않는다. 스크립트가 하나도 없으면 어셈블리가
    /// 아예 만들어지지 않아서, 참조가 되는지 안 되는지 알 수 없는 상태로 지나간다.
    /// </para>
    /// <para>
    /// ★ 여기서 <see cref="DeterministicRandom"/> 과 <see cref="ListEventSink"/> 를
    /// 둘 다 건드리는 것도 의도된 것이다. 이 둘이 core 와 unity 의 경계 그 자체다 —
    /// 시드는 Unity 가 주입하고, 이벤트 로그는 core 가 뱉고 Unity 가 재생한다.
    /// 런 루프는 P3, 재생은 P5 에서 이 자리에 붙는다. (19 §2.5, §3)
    /// </para>
    /// </remarks>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Tooltip("0 이면 실행할 때마다 시각으로 시드를 만든다. 재현이 필요하면 고정값을 넣는다.")]
        [SerializeField] private long _seed;

        [Tooltip("켜면 시작 시 결정론 경계를 점검하고 콘솔에 남긴다.")]
        [SerializeField] private bool _verifyOnStart = true;

        private DeterministicRandom _rng = null!;
        private ListEventSink _events = null!;

        private void Awake()
        {
            ulong seed = _seed != 0
                ? (ulong)_seed
                : (ulong)System.DateTime.UtcNow.Ticks;

            _rng = new DeterministicRandom(seed);
            _events = new ListEventSink();

            if (_verifyOnStart)
            {
                VerifyBoundary(seed);
            }

            StartCoroutine(StreamingGameData.LoadAsync(OnDataLoaded, OnDataError));
        }

        /// <summary>
        /// 데이터가 들어오면 <b>전투를 한 판 돌려본다.</b>
        /// </summary>
        /// <remarks>
        /// ★ 화면이 없는 동안 이게 유일한 확인이다.
        /// 게이트 3 은 "core 가 컴파일된다"까지만 증명했고,
        /// <b>브라우저에서 실제로 도는지는 아직 아무도 확인하지 않았다.</b>
        /// 로드·검증·전투가 여기서 한 번 돌면 그 구간이 닫힌다.
        /// </remarks>
        private void OnDataLoaded(GameData data)
        {
            var config = CombatConfig.From(data.Economy, 20);
            var meta = new MetaProgress(data.Meta);
            var engine = new RunEngine(data, config);

            var build = BuildSpace.Enumerate(data).First();
            var run = engine.StartRun("S1", build.CharacterIds, meta);
            var summary = engine.PlayRun(run, meta, _rng, NullEventSink.Instance, false, build);

            Debug.Log(
                $"[Bootstrap] 데이터 로드 OK — 캐릭터 {data.Characters.Count} · 스킬 {data.Skills.Count} · " +
                $"보조 {data.SupportSkills.Count} · 스테이지2 {(data.HasEncounterSetFor("S2") ? "있음" : "없음")}");

            Debug.Log(
                $"[Bootstrap] 시험 전투 — 빌드 {build.Id} · " +
                $"{(summary.Cleared ? "클리어" : "실패")} {summary.RoundsWon}/{summary.RoundsReached}승 · " +
                $"{summary.TotalTicks / 20.0:F1}초");
        }

        private void OnDataError(string message)
        {
            // 조용히 넘어가면 데이터 없이 도는 빌드가 배포되고, 실행하고 나서야 안다.
            Debug.LogError($"[Bootstrap] 데이터 로드 실패 — {message}");
        }

        /// <summary>
        /// core 가 Unity 안에서도 같은 수열을 주는지 확인한다.
        /// </summary>
        /// <remarks>
        /// dotnet 쪽 테스트가 이미 같은 검사를 한다. 그래도 여기서 한 번 더 하는 이유는,
        /// 확인해야 하는 대상이 "코드"가 아니라 <b>"런타임"</b>이기 때문이다.
        /// Unity 는 Mono / IL2CPP 로 돌고 WebGL 은 또 다르다.
        /// sim(CoreCLR) 결과와 게임(IL2CPP) 결과가 갈라지면 밸런스 수치가 전부 무의미해지는데,
        /// 그 사실을 마감 직전에 알게 되는 게 최악이다.
        /// </remarks>
        private void VerifyBoundary(ulong seed)
        {
            var a = new DeterministicRandom(seed);
            var b = new DeterministicRandom(seed);
            for (int i = 0; i < 1000; i++) { a.NextUInt64(); b.NextUInt64(); }

            bool rngOk = a.StateHash() == b.StateHash();
            bool coordOk = new Coord(0, 0).SqrDistanceTo(new Coord(3, 4)) == 25;

            _events.Emit(new GameEvent(EventKind.RoundEnd, 0, 0, 0, 0));
            bool sinkOk = _events.Events.Count == 1;
            _events.Clear();

            if (rngOk && coordOk && sinkOk)
            {
                Debug.Log($"[Bootstrap] core 경계 확인 — seed={seed}, 결정론 OK");
            }
            else
            {
                Debug.LogError(
                    $"[Bootstrap] core 경계 확인 실패 — rng={rngOk} coord={coordOk} sink={sinkOk}. " +
                    "시뮬 결과와 게임 결과가 갈라질 수 있다. 밸런스 작업을 진행하기 전에 원인을 찾을 것.");
            }
        }
    }
}
