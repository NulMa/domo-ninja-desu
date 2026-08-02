// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DomoNinja.Core.Data
{
    /// <summary>
    /// `/data` 전체를 읽어 <see cref="GameData"/> 를 만든다. <b>어느 파일이 어느 스테이지인지는 데이터가 안다.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>파일 이름을 코드에 두지 않는 게 이 타입의 존재 이유다.</b>
    /// 스테이지가 늘 때마다 sim·Unity·테스트 세 곳이 같은 매핑을 각자 들고 있으면,
    /// 한 곳만 빠뜨려도 <b>그쪽에서만 스테이지가 없는 채로 조용히 돌아간다.</b>
    /// 매핑은 `economy.stages.list[].encounterFile` 에 있고, 여기는 그걸 따라 읽기만 한다.
    /// </para>
    /// <para>
    /// ★ <b>파일을 직접 열지 않고 읽기 함수를 받는다.</b>
    /// WebGL 에는 파일 시스템이 없다 — Unity 는 <c>TextAsset</c> 으로, <c>sim</c> 은
    /// <c>File.ReadAllText</c> 로 같은 계약을 만족시킨다.
    /// core 가 <c>File</c> 을 부르면 dotnet 에서만 되고 브라우저에서 깨지는데,
    /// 그 차이는 WebGL 빌드를 실제로 돌리기 전까지 드러나지 않는다.
    /// </para>
    /// </remarks>
    public static class GameDataFiles
    {
        public const string Characters = "characters.json";
        public const string Skills = "skills.json";
        public const string Encounters = "encounters.json";
        public const string Economy = "economy.json";
        public const string Meta = "meta.json";

        /// <summary>
        /// 전부 읽어 로드한다.
        /// </summary>
        /// <param name="read">
        /// 파일 이름(<c>"characters.json"</c>)을 받아 내용을 돌려준다.
        /// <b>없는 파일이면 <c>null</c> 을 돌려주면 된다</b> — 저작이 아직 안 들어온 스테이지를 건너뛴다.
        /// </param>
        /// <exception cref="DataValidationException">계약 위반이 하나라도 있으면.</exception>
        public static GameData Load(Func<string, string?> read)
        {
            if (read == null) throw new ArgumentNullException(nameof(read));

            string characters = Require(read, Characters);
            string skills = Require(read, Skills);
            string encounters = Require(read, Encounters);
            string economy = Require(read, Economy);
            string meta = Require(read, Meta);

            var extra = CollectEncounterSets(economy, read);

            return GameDataLoader.Load(characters, skills, encounters, economy, meta, extra);
        }

        /// <summary>
        /// `economy.stages.list` 를 훑어 추가 관문 세트를 모은다.
        /// </summary>
        /// <remarks>
        /// 기본 세트(`encounters.json`)는 <see cref="GameDataLoader"/> 가 이미 받으므로 건너뛴다.
        /// <b>파일이 없으면 조용히 건너뛴다</b> — 저작이 아직 안 들어온 스테이지 때문에
        /// 시뮬 전체가 멈추면 저작 진행이 밸런스 루프를 막는다.
        /// 대신 <see cref="GameData.HasEncounterSetFor"/> 로 들어왔는지 물어볼 수 있다.
        /// </remarks>
        private static Dictionary<string, string> CollectEncounterSets(string economyJson, Func<string, string?> read)
        {
            var sets = new Dictionary<string, string>();

            JObject economy;
            try
            {
                economy = JObject.Parse(economyJson);
            }
            catch (Exception)
            {
                // 파싱 실패는 GameDataLoader 가 PARSE 오류로 보고한다. 여기서 던지면
                // "economy.json 이 깨졌다"가 아니라 스택 트레이스가 먼저 나온다.
                return sets;
            }

            if (!((economy["stages"] as JObject)?["list"] is JArray list)) return sets;

            foreach (var entry in list)
            {
                string? setName = (string?)entry["encounterSet"];
                string? fileName = (string?)entry["encounterFile"];

                if (setName == null || fileName == null) continue;
                if (fileName == Encounters) continue;              // 기본 세트는 이미 읽었다
                if (sets.ContainsKey(setName)) continue;

                string? content = read(fileName);
                if (content != null) sets[setName] = content;
            }

            return sets;
        }

        private static string Require(Func<string, string?> read, string fileName)
        {
            string? content = read(fileName);
            if (content == null)
                throw new DataValidationException(new[]
                {
                    new ValidationError("FILE", fileName, "파일을 읽지 못했다"),
                });

            return content;
        }
    }
}
