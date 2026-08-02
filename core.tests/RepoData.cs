using System;
using System.IO;
using DomoNinja.Core.Data;
using Newtonsoft.Json.Linq;

namespace DomoNinja.Core.Tests
{
    /// <summary>
    /// 저장소의 실제 `/data` JSON 을 테스트에 공급한다.
    /// </summary>
    /// <remarks>
    /// ★ 테스트가 <b>가짜 데이터가 아니라 실제 파일</b>을 읽는 게 요점이다.
    /// 최적화기(P4)가 값을 바꿔 커밋하므로, 검증 규칙이 도는 대상은 늘 저장소의 현재 데이터여야 한다.
    /// 고정된 샘플을 쓰면 데이터가 계약을 어겨도 테스트는 계속 초록으로 남는다.
    ///
    /// 경로를 상수로 박지 않고 거슬러 올라가며 찾는 이유 — 출력 디렉토리가
    /// `artifacts/bin/...` 로 재배치돼 있어(`Directory.Build.props`) 상대 깊이가 고정이 아니다.
    /// </remarks>
    public static class RepoData
    {
        private static readonly Lazy<string> DataDir = new Lazy<string>(FindDataDir);

        public static string Characters => Read("characters.json");
        public static string Skills => Read("skills.json");
        public static string Encounters => Read("encounters.json");
        public static string Economy => Read("economy.json");
        public static string Meta => Read("meta.json");

        public static string Read(string fileName) =>
            File.ReadAllText(Path.Combine(DataDir.Value, fileName));

        /// <summary>
        /// <see cref="GameDataFiles.Load"/> 에 넘길 읽기 함수. <b>없는 파일은 <c>null</c> 이다.</b>
        /// </summary>
        /// <remarks>
        /// 스테이지 파일 이름을 여기 박지 않는다 — 그 매핑은 `economy.stages.list[].encounterFile` 에 있고,
        /// 테스트가 따로 들고 있으면 데이터와 어긋나도 초록으로 남는다.
        /// </remarks>
        public static string? TryRead(string fileName)
        {
            string path = Path.Combine(DataDir.Value, fileName);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        /// <summary>저장소의 `/data` 전체를 데이터가 정한 대로 읽어 로드한다.</summary>
        public static GameData LoadAll() => GameDataFiles.Load(TryRead);

        public static JObject Json(string fileName) => JObject.Parse(Read(fileName));

        private static string FindDataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "data");
                if (File.Exists(Path.Combine(candidate, "_schema", "README.md")))
                    return candidate;
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException(
                $"`data/_schema/README.md` 를 찾지 못했다. 시작 지점: {AppContext.BaseDirectory}");
        }
    }
}
