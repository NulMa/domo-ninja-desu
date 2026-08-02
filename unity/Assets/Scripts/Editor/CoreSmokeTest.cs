using System.IO;
using System.Linq;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using DomoNinja.Core.Economy;
using DomoNinja.Core.Events;
using DomoNinja.Core.Rng;
using UnityEditor;
using UnityEngine;

namespace DomoNinja.Unity.Editor
{
    /// <summary>
    /// 에디터에서 <b>core 가 Unity 런타임 안에서 실제로 도는지</b> 확인한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 게이트 3 은 <b>"core 가 컴파일된다"</b> 까지만 증명했다.
    /// 컴파일과 실행은 다르다 — 이 프로젝트가 D+2 에 세 번 겪은 *"했다 ≠ 된다"* 와 같은 자리다.
    /// (Optuna: 설치 ≠ 동작 / 게이트 3: asmdef 생성 ≠ 컴파일 / nullable: 설정 ≠ 적용)
    /// </para>
    /// <para>
    /// <c>sim</c> 은 CoreCLR 에서 돌고 Unity 는 Mono·IL2CPP 에서 돈다.
    /// <b>둘이 같은 시드로 다른 결과를 내면 밸런스 수치가 전부 무의미해진다.</b>
    /// 그래서 여기 출력은 <c>sim --replay</c> 와 <b>대조하라고</b> 찍는다 —
    /// 눈으로 같은지 보는 게 지금 할 수 있는 유일한 확인이다.
    /// </para>
    /// <para>
    /// 플레이 모드가 필요 없다. 데이터를 <see cref="File"/> 로 읽기 때문인데,
    /// ⚠️ <b>그래서 이 검사는 WebGL 경로를 확인하지 않는다.</b>
    /// 브라우저는 <see cref="StreamingGameData"/> 의 <c>UnityWebRequest</c> 경로를 타고,
    /// 그건 실제 WebGL 빌드로만 확인된다.
    /// </para>
    /// </remarks>
    public static class CoreSmokeTest
    {
        [MenuItem("DomoNinja/시험 전투 실행 (core 동작 확인)")]
        public static void Run()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "data");
            if (!File.Exists(Path.Combine(dir, "economy.json")))
            {
                Debug.LogWarning("[SmokeTest] StreamingAssets 에 데이터가 없다. 먼저 '데이터 동기화' 를 실행한다.");
                DataSync.Sync();
            }

            GameData data;
            try
            {
                data = GameDataFiles.Load(name =>
                {
                    string path = Path.Combine(dir, name);
                    return File.Exists(path) ? File.ReadAllText(path) : null;
                });
            }
            catch (DataValidationException ex)
            {
                Debug.LogError($"[SmokeTest] 데이터 검증 실패\n{ex.Message}");
                return;
            }

            Debug.Log($"[SmokeTest] 로드 OK — 캐릭터 {data.Characters.Count} · 액티브 {data.Skills.Count} · " +
                      $"보조 {data.SupportSkills.Count} · 적 {data.EnemyTypes.Count} · " +
                      $"스테이지2 {(data.HasEncounterSetFor("S2") ? "있음" : "없음")}");

            var config = CombatConfig.From(data.Economy, 20);
            var engine = new RunEngine(data, config);
            var build = BuildSpace.Enumerate(data).First();
            var meta = new MetaProgress(data.Meta);

            var run = engine.StartRun("S1", build.CharacterIds, meta);
            var summary = engine.PlayRun(run, meta, new DeterministicRandom(1UL),
                                         NullEventSink.Instance, false, build);

            // sim 과 대조할 수 있게 한 줄로 찍는다. 형식이 다르면 눈으로 비교가 안 된다.
            Debug.Log($"[SmokeTest] 빌드 {build.Id} · 시드 1\n" +
                      $"  {(summary.Cleared ? "클리어" : "실패")} " +
                      $"{summary.RoundsWon}/{summary.RoundsReached}승 · 생명 {summary.LivesLeft} · " +
                      $"{summary.TotalTicks}틱 · {summary.TotalUnitTicks}유닛틱");
        }
    }
}
