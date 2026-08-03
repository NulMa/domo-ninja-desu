#nullable enable

using System.Collections.Generic;
using System.Linq;
using DomoNinja.Core.Data;
using DomoNinja.Core.Tests;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DomoNinja.Sim.Tests
{
    /// <summary>
    /// `balance.json` — 커밋되는 밸런스 스냅샷 (`_schema` §11 · `D-55`).
    /// </summary>
    /// <remarks>
    /// ★ 이 파일의 값은 숫자가 아니라 <b>출처</b>다. <c>[BAL]</c> 커밋의 <c>근거:</c> 가
    /// 실제로 검증 가능해지려면 *"어느 코드로 · 어느 파라미터 세트로 · 어느 시드에서"* 가
    /// 파일 하나 안에서 닫혀야 한다.
    /// </remarks>
    [TestFixture]
    public class BalanceReportTests
    {
        private static JObject RunReport() => JObject.Parse(@"{
            'throughput': { 'runs': 21600 },
            'provenance': { 'buildSpaceTotal': 4320 },
            'metrics': { 'M1': { 'clearRate': 0.33 } },
            'objective': {
                'D': 0.6643,
                'terms': [ { 'metric': 'M1', 'd': 1.0 }, { 'metric': 'M5', 'd': 0.153 } ],
                'excluded': [ 'M6' ],
                '_excludedWhy': 'M6 은 이 봇으로 측정 불가 (D-72)',
                'verdict': 'fail',
                'violations': [ 'M5' ]
            }
        }".Replace('\'', '"'));

        private static SimParams Params(ParamOverrides.Set? overrides = null, int buildLimit = 0) =>
            new SimParams { Seeds = 3, SeedStart = 1, Meta = "meta0",
                            BuildLimit = buildLimit, Overrides = overrides };

        [Test]
        public void 계약이_요구하는_필드가_전부_있다()
        {
            var b = BalanceReport.From(RunReport(), Params(), "sha256:abc", "deadbee");

            foreach (string key in new[] { "schemaVersion", "generatedAt", "provenance",
                                           "metaPoint", "metrics", "desirability",
                                           "verdict", "violations" })
                Assert.That(b[key], Is.Not.Null, key);

            var p = (JObject)b["provenance"]!;
            foreach (string key in new[] { "simCommit", "dataHash", "seeds",
                                           "runsExecuted", "crnEnabled" })
                Assert.That(p[key], Is.Not.Null, $"provenance.{key} — `_schema` §11 이 필수로 적어둔 것");
        }

        [Test]
        public void 시드_목록이_실제로_돈_시드다()
        {
            // CRN 비교(`17` §1)를 다시 하려면 어느 시드였는지가 있어야 한다.
            var b = BalanceReport.From(RunReport(), Params(), "sha256:abc", "deadbee");
            var seeds = ((JArray)b["provenance"]!["seeds"]!).Select(x => (long)x!).ToArray();

            Assert.That(seeds, Is.EqualTo(new long[] { 1, 2, 3 }));
        }

        [Test]
        public void 부분_탐색이면_CRN_이_꺼진_것으로_표시된다()
        {
            // buildLimit 이 걸리면 표본이 갈려 다른 실행과 비교할 수 없다.
            // 그걸 표시 안 하면 비교하면 안 되는 두 리포트가 나란히 놓인다.
            var full = BalanceReport.From(RunReport(), Params(buildLimit: 0), "h", "c");
            var part = BalanceReport.From(RunReport(), Params(buildLimit: 200), "h", "c");

            Assert.That((bool)full["provenance"]!["crnEnabled"]!, Is.True);
            Assert.That((bool)part["provenance"]!["crnEnabled"]!, Is.False);
        }

        [Test]
        public void 만족도가_지표별로_펼쳐진다()
        {
            var b = BalanceReport.From(RunReport(), Params(), "h", "c");
            var d = (JObject)b["desirability"]!["d"]!;

            Assert.That((double)d["M1"]!, Is.EqualTo(1.0));
            Assert.That((double)d["M5"]!, Is.EqualTo(0.153));
            Assert.That((double)b["desirability"]!["D"]!, Is.EqualTo(0.6643));
        }

        [Test]
        public void 제외된_지표와_그_이유가_같이_실린다()
        {
            // D-72 — 무엇을 못 재는지 아는 것이 기록으로 남아야 한다.
            var b = BalanceReport.From(RunReport(), Params(), "h", "c");
            var des = (JObject)b["desirability"]!;

            Assert.That(((JArray)des["excluded"]!).Select(x => (string)x!), Is.EqualTo(new[] { "M6" }));
            Assert.That((string)des["_excludedWhy"]!, Does.Contain("D-72"));
        }

        [Test]
        public void 덮어쓰기가_있으면_무엇을_바꿨는지가_남는다()
        {
            // dataHash 는 "다르다" 만 말하고 "무엇이" 는 말하지 않는다.
            var overrides = new ParamOverrides.Set
            {
                ["characters.json"] = new Dictionary<string, JToken>
                {
                    ["$.characters[?(@.id=='C2')].hp"] = JToken.FromObject(171),
                },
            };

            var b = BalanceReport.From(RunReport(), Params(overrides), "h", "c");
            var p = (JObject)b["provenance"]!;

            Assert.That((string)p["overridesHash"]!, Is.Not.EqualTo("none"));
            Assert.That(p["overrides"]!["characters.json"], Is.Not.Null);
        }

        // ────────────────────────────── 데이터 지문

        [Test]
        public void 지문이_덮어쓰기_적용_후를_잰다()
        {
            // ★ 이 테스트가 이 파일에서 가장 중요하다.
            //   적용 전을 해싱하면 최적화기가 돌린 모든 실행이 같은 지문을 갖는다 —
            //   "어느 파라미터 세트의 결과인가" 가 닫히지 않고, 그래도 리포트는 멀쩡해 보인다.
            var overrides = new ParamOverrides.Set
            {
                ["characters.json"] = new Dictionary<string, JToken>
                {
                    ["$.characters[?(@.id=='C2')].hp"] = JToken.FromObject(171),
                },
            };

            string Load(ParamOverrides.Set? o)
            {
                var hasher = new BalanceReport.DataHasher();
                GameDataFiles.Load(hasher.Wrap(ParamOverrides.Wrap(RepoData.TryRead, o)));
                return hasher.Hash();
            }

            Assert.That(Load(overrides), Is.Not.EqualTo(Load(null)));
        }

        [Test]
        public void 같은_데이터는_같은_지문이다()
        {
            string Load()
            {
                var hasher = new BalanceReport.DataHasher();
                GameDataFiles.Load(hasher.Wrap(RepoData.TryRead));
                return hasher.Hash();
            }

            Assert.That(Load(), Is.EqualTo(Load()));
        }

        [Test]
        public void 개행_차이로_지문이_갈리지_않는다()
        {
            // Windows 에서 만든 파일과 CI 체크아웃이 CRLF/LF 로 갈리는데,
            // 그것 때문에 지문이 달라지면 "값이 바뀌었다" 와 구분되지 않는다.
            string Load(bool crlf)
            {
                var hasher = new BalanceReport.DataHasher();
                GameDataFiles.Load(hasher.Wrap(name =>
                {
                    string? raw = RepoData.TryRead(name);
                    if (raw == null) return null;
                    string lf = raw.Replace("\r\n", "\n");
                    return crlf ? lf.Replace("\n", "\r\n") : lf;
                }));
                return hasher.Hash();
            }

            Assert.That(Load(true), Is.EqualTo(Load(false)));
        }

        [Test]
        public void 데이터를_하나도_안_읽었으면_지문이_none_이다()
        {
            // 빈 문자열이면 리포트를 읽는 쪽이 "안 넣었나" 와 구분할 수 없다.
            Assert.That(new BalanceReport.DataHasher().Hash(), Is.EqualTo("sha256:none"));
        }
    }
}
