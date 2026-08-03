#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace DomoNinja.Sim
{
    /// <summary>
    /// <c>balance.json</c> — <b>커밋되는 밸런스 스냅샷</b> (`_schema` §11 · `D-55`).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <c>metrics.json</c> 과 <b>다른 물건이다.</b>
    /// <c>metrics.json</c> 은 실행할 때마다 바뀌는 작업 산출물이라 <c>.gitignore</c> 에 있다.
    /// 이건 <b><c>[BAL]</c> 커밋에 딸려 저장소에 남는 한 장</b>이고, 그래서
    /// <c>09</c> §1.3 이 요구하는 <c>근거:</c> 가 **실제로 검증 가능**해진다 —
    /// 심사자가 파일 하나로 *"이 커밋의 이 데이터로 이 시드에서 이 결과가 나왔다"* 를 읽는다.
    /// </para>
    /// <para>
    /// ★ <b><see cref="DataHasher"/> 가 이 파일의 핵심이다.</b>
    /// <c>simCommit</c> 만으로는 부족하다 — 최적화기가 <c>overrides</c> 로 값을 바꿔 돌린 결과라면
    /// <b>같은 커밋에서 다른 숫자가 나온다.</b> 실제로 로드된 데이터를 해싱해야
    /// *"어느 파라미터 세트의 결과인가"* 가 닫힌다.
    /// </para>
    /// </remarks>
    public static class BalanceReport
    {
        public const int SchemaVersion = 1;

        /// <summary>
        /// 실제로 <b>로드된</b> 데이터 전체의 지문을 낸다.
        /// </summary>
        /// <remarks>
        /// ★ <b>파일을 다시 읽지 않고 읽기 함수를 감싼다.</b> 다시 읽으면
        /// <c>overrides</c> 가 적용되기 <b>전</b> 내용을 해싱하게 되어,
        /// 최적화기가 돌린 모든 실행이 <b>같은 해시를 갖는다</b> — 지문이 지문 노릇을 못 한다.
        /// </remarks>
        public sealed class DataHasher
        {
            private readonly SortedDictionary<string, string> _seen =
                new SortedDictionary<string, string>(StringComparer.Ordinal);

            public Func<string, string?> Wrap(Func<string, string?> read) => name =>
            {
                string? raw = read(name);
                if (raw != null) _seen[name] = raw;
                return raw;
            };

            /// <summary>파일 이름 순으로 이어붙여 해싱한다. 읽는 순서가 바뀌어도 같은 값이 나온다.</summary>
            public string Hash()
            {
                if (_seen.Count == 0) return "sha256:none";

                var sb = new StringBuilder();
                foreach (var kv in _seen)
                {
                    // 개행 정규화. Windows 에서 만든 파일과 CI 의 체크아웃이 CRLF/LF 로 갈리는데,
                    // 그것 때문에 지문이 달라지면 "값이 바뀌었다" 와 구분되지 않는다.
                    sb.Append(kv.Key).Append('\n')
                      .Append(kv.Value.Replace("\r\n", "\n")).Append('\n');
                }

                using (var sha = SHA256.Create())
                {
                    byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                    var hex = new StringBuilder(64);
                    foreach (byte b in bytes) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                    return "sha256:" + hex.ToString();
                }
            }

            public IReadOnlyCollection<string> Files => _seen.Keys;
        }

        /// <summary>`--run` 이 낸 리포트를 `_schema` §11 형식으로 접는다.</summary>
        public static JObject From(JObject runReport, SimParams p, string dataHash, string simCommit)
        {
            var metrics = runReport["metrics"] as JObject ?? new JObject();
            var objective = runReport["objective"] as JObject ?? new JObject();

            var seeds = new JArray();
            for (int i = 0; i < p.Seeds; i++) seeds.Add((long)(p.SeedStart + (ulong)i));

            var d = new JObject();
            foreach (var term in objective["terms"] as JArray ?? new JArray())
                d[(string)term["metric"]!] = term["d"];

            return new JObject
            {
                ["schemaVersion"] = SchemaVersion,
                ["generatedAt"] = DateTime.UtcNow.ToString("o"),

                ["provenance"] = new JObject
                {
                    ["simCommit"] = simCommit,
                    ["dataHash"] = dataHash,

                    // 최적화기가 값을 바꿔 돌린 결과라면 무엇을 바꿨는지가 여기 남는다.
                    // dataHash 는 "다르다" 만 말하고 "무엇이" 는 말하지 않는다.
                    ["overridesHash"] = ParamOverrides.Hash(p.Overrides),
                    ["overrides"] = ParamOverrides.ToJson(p.Overrides),

                    ["seeds"] = seeds,
                    ["runsExecuted"] = (runReport["throughput"] ?? new JObject())["runs"] ?? 0,
                    ["buildSpaceTotal"] = (runReport["provenance"] ?? new JObject())["buildSpaceTotal"] ?? 0,

                    // ★ 전수 탐색이면 모든 빌드가 같은 시드 집합을 겪는다 = CRN 성립 (`17` §1).
                    //   buildLimit 을 두면 표본이 갈려 다른 실행과 비교할 수 없다.
                    ["crnEnabled"] = p.BuildLimit == 0,
                    ["_crnNote"] = p.BuildLimit == 0
                        ? "전수 탐색이라 모든 빌드가 같은 시드 집합을 겪는다."
                        : "buildLimit 이 걸려 있어 표본이 갈린다 — 다른 실행과 비교하지 말 것.",
                },

                ["metaPoint"] = p.Meta,
                ["_metaPointNote"] =
                    "어느 메타 진행도에서 측정했는지. meta0 / meta50 / metaMax. 이게 없으면 M1 값이 무의미하다.",

                ["metrics"] = metrics.DeepClone(),

                ["desirability"] = new JObject
                {
                    ["d"] = d,
                    ["D"] = objective["D"] ?? 0,
                    ["excluded"] = objective["excluded"]?.DeepClone() ?? new JArray(),
                    ["_note"] = "기하평균. 하나라도 0 이면 D=0 (`17` §2).",
                    ["_excludedWhy"] = objective["_excludedWhy"] ?? "",
                },

                ["verdict"] = objective["verdict"] ?? "unknown",
                ["violations"] = objective["violations"]?.DeepClone() ?? new JArray(),
            };
        }
    }
}
