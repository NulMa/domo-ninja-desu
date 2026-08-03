#nullable enable

using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DomoNinja.Sim.Tests
{
    /// <summary>
    /// 목적함수 <c>D</c> (`17` §2 Derringer–Suich). <b>P4 최적화기가 쫓는 유일한 숫자다.</b>
    /// </summary>
    [TestFixture]
    public class ObjectiveTests
    {
        /// <summary>모든 지표가 목표 한가운데인 리포트. 여기서 하나씩 망가뜨려 본다.</summary>
        private static JObject Perfect() => JObject.Parse(@"{
            'M1': { 'clearRate': 0.32 },
            'M2': [ { 'lossRate': 0.10 }, { 'lossRate': 0.20 }, { 'lossRate': 0.30 } ],
            'M3a': { 'C1': 0.5, 'C2': 0.5 },
            'M3b': { 'C1': { 'C1-A': 0.5, 'C1-B': 0.5 } },
            'M3c': { 'C1-P1': 0.4, 'C1-P2': 0.4 },
            'M4': { 'topShare': 0.10 },
            'M5': { 'runMinutesAvg': 4.0, 'combatMinutesAvg': 1.0 },
            'M6': { 'avgRound': 2.0 },
            'M7': { 'timeoutRate': 0.01 }
        }".Replace('\'', '"'));

        private static double D(JObject metrics) =>
            (double)Objective.Compute(metrics, "meta0")["D"]!;

        private static string[] Terms(JObject metrics) =>
            ((JArray)Objective.Compute(metrics, "meta0")["terms"]!)
                .Select(t => (string)t["metric"]!).ToArray();

        [Test]
        public void 전부_목표_안이면_D_가_1_이다()
        {
            Assert.That(D(Perfect()), Is.EqualTo(1.0).Within(1e-9));
            Assert.That((string)Objective.Compute(Perfect(), "meta0")["verdict"]!, Is.EqualTo("pass"));
        }

        [Test]
        public void M4_가_점수에_들어간다()
        {
            // D-71 이전에는 정의 미확정이라 빠져 있었다. 빠진 채로 최적화기를 돌리면
            // 지배 빌드를 만들어서라도 다른 지표를 채우는 해가 최적으로 뽑힌다.
            Assert.That(Terms(Perfect()), Does.Contain("M4"));

            var dominated = Perfect();
            dominated["M4"]!["topShare"] = 0.55;

            Assert.That(D(dominated), Is.LessThan(1.0), "상위 5% 가 클리어의 55% 인데 만점이다");
        }

        [Test]
        public void M6_는_점수에_안_들어간다()
        {
            // D-72 확정 — 봇이 저축을 안 해서 못 재는 지표다. 값은 리포트에 계속 낸다.
            Assert.That(Terms(Perfect()), Does.Not.Contain("M6"));

            var result = Objective.Compute(Perfect(), "meta0");
            Assert.That(((JArray)result["excluded"]!).Select(x => (string)x!), Is.EqualTo(new[] { "M6" }));
            Assert.That((string)result["_excludedWhy"]!, Does.Contain("D-72"));
        }

        [Test]
        public void M4_는_균등한_쪽으로_더_가도_감점되지_않는다()
        {
            // 하한을 두면 최적화기가 일부러 지배 빌드를 만들어 점수를 채우려 든다.
            var flat = Perfect();
            flat["M4"]!["topShare"] = 0.05;   // 완전 균등

            Assert.That(D(flat), Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void 하나가_0_이면_전체가_0_이다()
        {
            // ★ 기하평균을 고른 이유. 산술평균이면 "M5 를 포기하고 나머지로 벌충하는" 해가
            //   최적으로 뽑히는데, 최적화기는 그런 구멍을 반드시 찾아낸다.
            var broken = Perfect();
            broken["M5"]!["runMinutesAvg"] = 20.0;   // 허용 한계(8분) 밖

            Assert.That(D(broken), Is.EqualTo(0.0));
        }

        [Test]
        public void 목표_밖에서도_기울기가_남는다()
        {
            // 목표를 벗어난 순간 전부 0 이면 최적화기가 어느 쪽으로 가야 나아지는지 모른다.
            var near = Perfect(); near["M1"]!["clearRate"] = 0.20;
            var far = Perfect(); far["M1"]!["clearRate"] = 0.13;

            Assert.That(D(near), Is.LessThan(1.0));
            Assert.That(D(far), Is.LessThan(D(near)), "더 나쁜 쪽이 더 낮아야 방향이 생긴다");
            Assert.That(D(far), Is.GreaterThan(0.0));
        }

        [Test]
        public void 위반_목록이_실제로_점수를_깎은_지표와_같다()
        {
            var broken = Perfect();
            broken["M3a"]!["C2"] = 0.95;
            broken["M7"]!["timeoutRate"] = 0.09;

            var violations = ((JArray)Objective.Compute(broken, "meta0")["violations"]!)
                .Select(x => (string)x!).ToArray();

            Assert.That(violations, Is.EquivalentTo(new[] { "M3a", "M7" }));
        }

        [Test]
        public void M1_목표는_메타_측정점마다_다르다()
        {
            // 메타 강화가 쌓이면 클리어율이 올라가는 게 정상이다. 같은 목표를 대면
            // 최적화기가 metaMax 를 맞추려고 meta0 를 불가능하게 만든다.
            var metrics = Perfect();
            metrics["M1"]!["clearRate"] = 0.32;

            Assert.That((double)Objective.Compute(metrics, "meta0")["D"]!, Is.EqualTo(1.0).Within(1e-9));
            Assert.That((double)Objective.Compute(metrics, "metaMax")["D"]!, Is.LessThan(1.0));
        }
    }
}
