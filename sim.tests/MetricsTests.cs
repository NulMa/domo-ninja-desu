#nullable enable

using System.Collections.Generic;
using System.Linq;
using DomoNinja.Core.Data;
using DomoNinja.Core.Economy;
using DomoNinja.Core.Tests;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DomoNinja.Sim.Tests
{
    /// <summary>
    /// 지표 계산 (`08` §6.2). <b>`M4` 정의(`D-71`)가 여기 박혀 있다.</b>
    /// </summary>
    /// <remarks>
    /// ★ 지금까지 <c>sim</c> 에는 테스트가 하나도 없었다. <c>core</c> 는 313개인데
    /// 그 위에서 숫자를 접는 층은 0 이었다 — <b>P4 최적화기는 그 숫자 하나만 보고
    /// 수천 번 값을 고친다.</b> 지표가 틀리면 "최적화기가 무엇을 개선했는가" 의 근거가
    /// 통째로 사라지는데, <b>그 사실이 어디에도 안 나타난다.</b> 숫자는 계속 나오기 때문이다.
    /// </remarks>
    [TestFixture]
    public class MetricsTests
    {
        /// <summary>이름만 다른 빌드. 지표 계산은 빌드의 내용을 안 본다 — id 로만 묶는다.</summary>
        private static BuildTarget Build(string id) => new BuildTarget(
            new[] { id },
            new Dictionary<string, string> { [id] = id },
            new Dictionary<string, IReadOnlyList<string>> { [id] = new string[0] });

        private static Metrics.Sample Sample(string buildId, bool cleared, int ticks = 100,
                                             int firstActivation = 2) =>
            new Metrics.Sample
            {
                Build = Build(buildId),
                Summary = new RunSummary(
                    roundsWon: cleared ? 8 : 3, roundsReached: 8, cleared: cleared,
                    livesLeft: cleared ? 3 : 0, totalTicks: ticks, totalUnitTicks: ticks * 6,
                    firstActivationRound: firstActivation, rounds: new RoundOutcome[0]),
            };

        /// <summary><paramref name="clearsPerBuild"/> 번째 빌드가 그 횟수만큼 클리어한다.</summary>
        private static List<Metrics.Sample> Spread(int seedsPerBuild, params int[] clearsPerBuild)
        {
            var list = new List<Metrics.Sample>();
            for (int b = 0; b < clearsPerBuild.Length; b++)
            {
                string id = $"B{b:D4}";
                for (int s = 0; s < seedsPerBuild; s++)
                    list.Add(Sample(id, cleared: s < clearsPerBuild[b]));
            }
            return list;
        }

        private static GameData _data = null!;

        [OneTimeSetUp]
        public void Load() => _data = RepoData.LoadAll();

        /// <remarks>
        /// `M3a`~`M3c` 가 실제 캐릭터·스킬 목록을 훑기 때문에 데이터가 필요하다.
        /// 여기 쓰는 가짜 빌드는 그 목록에 없으므로 `M3` 계열은 0 이 나오고,
        /// <b>이 테스트가 보는 `M4`·`M6` 에는 영향을 주지 않는다.</b>
        /// </remarks>
        private static JObject M4Of(IReadOnlyList<Metrics.Sample> samples) =>
            (JObject)Metrics.Compute(_data, samples, 20)["M4"]!;

        // ────────────────────────────── M4 (D-71)

        [Test]
        public void 완전히_균등하면_상위_5퍼센트가_클리어의_5퍼센트를_가져간다()
        {
            // 100 빌드가 전부 5시드 중 2번 클리어. 상위 5개 = 클리어의 5%.
            var clears = Enumerable.Repeat(2, 100).ToArray();
            var m4 = M4Of(Spread(5, clears));

            Assert.That((int)m4["topCount"]!, Is.EqualTo(5));
            Assert.That((double)m4["topShare"]!, Is.EqualTo(0.05).Within(0.001));
        }

        [Test]
        public void 한_줌이_독식하면_점유율이_올라간다()
        {
            // 100 빌드 중 5개만 클리어한다 → 상위 5% 가 클리어의 100%.
            var clears = new int[100];
            for (int i = 0; i < 5; i++) clears[i] = 5;

            var m4 = M4Of(Spread(5, clears));

            Assert.That((double)m4["topShare"]!, Is.EqualTo(1.0).Within(0.001));
        }

        [Test]
        public void 난이도가_올라가도_분포가_균등하면_값이_유지된다()
        {
            // ★ 이게 D-71 에서 B안(클리어 절반을 가져가는 빌드 비율)을 버린 이유다.
            //   B안은 분모가 전체 빌드 수라 클리어 가능한 빌드가 줄면 값이 같이 떨어졌다 —
            //   최적화기가 M1 을 고치는 방향이 곧 M4 를 깎는 방향이 되어
            //   기하평균 안에서 두 지표가 서로 싸운다.
            var easy = M4Of(Spread(5, Enumerable.Repeat(4, 100).ToArray()));   // 클리어율 80%
            var hard = M4Of(Spread(5, Enumerable.Repeat(1, 100).ToArray()));   // 클리어율 20%

            Assert.That((double)hard["topShare"]!,
                        Is.EqualTo((double)easy["topShare"]!).Within(0.001),
                        "난이도만 달라졌는데 M4 가 움직였다 — M1 과 결합돼 있다");
        }

        [Test]
        public void 상위_빌드가_하나도_없어도_0으로_나눠지지_않는다()
        {
            var m4 = M4Of(Spread(5, Enumerable.Repeat(0, 100).ToArray()));

            Assert.That((double)m4["topShare"]!, Is.EqualTo(0.0));
            Assert.That((int)m4["topCount"]!, Is.EqualTo(5));
        }

        [Test]
        public void 빌드가_아주_적어도_상위가_0개가_되지_않는다()
        {
            // 올림이 아니라 반올림이면 빌드 10개일 때 상위 0개가 되어 M4 가 항상 0 이다.
            var m4 = M4Of(Spread(5, 5, 0, 0, 0, 0, 0, 0, 0, 0, 0));

            Assert.That((int)m4["topCount"]!, Is.EqualTo(1));
            Assert.That((double)m4["topShare"]!, Is.EqualTo(1.0).Within(0.001));
        }

        [Test]
        public void 동률_빌드의_상위_경계가_흔들리지_않는다()
        {
            // 전부 동률이면 어느 216개가 "상위" 인지는 정렬이 정한다. id 로 끊지 않으면
            // 같은 입력이 다른 M4 를 낼 수 있고, 그건 결정론 요구사항 위반이다.
            var samples = Spread(5, Enumerable.Repeat(2, 100).ToArray());

            var a = M4Of(samples);
            var b = M4Of(samples.AsEnumerable().Reverse().ToList());

            Assert.That((string)b["topBuildId"]!, Is.EqualTo((string)a["topBuildId"]!));
            Assert.That((double)b["topShare"]!, Is.EqualTo((double)a["topShare"]!));
        }

        // ────────────────────────────── M5 (D-74)

        private static JObject M5Of(IReadOnlyList<Metrics.Sample> samples) =>
            (JObject)Metrics.Compute(_data, samples, 20)["M5"]!;

        [Test]
        public void M5_는_전투에_라운드당_조작_시간을_더한다()
        {
            // ★ D-74. 전에는 전투 틱만 재면서 목표는 "1런 3~5분"(05 §1.8) 을 댔다.
            //   §1.8 에서 한 라운드는 상점 + 배치 + 전투다 — 잰 것과 목표가 달랐다.
            //   그 오독이 d=0.153 으로 나왔고 "전투를 6.5배 늘려야 한다" 는 결론까지 갔다.
            var m5 = M5Of(Spread(1, 5));

            double combat = (double)m5["combatMinutesAvg"]!;
            double run = (double)m5["runMinutesAvg"]!;
            double rounds = (double)m5["avgRoundsReached"]!;
            double perRound = (int)m5["interactionSecondsPerRound"]! / 60.0;

            Assert.That(run, Is.EqualTo(combat + rounds * perRound).Within(0.01));
            Assert.That(run, Is.GreaterThan(combat), "조작 시간이 안 더해졌다");
        }

        [Test]
        public void M5_는_기계가_잰_것과_사람이_가정한_것을_나눠_낸다()
        {
            // 합쳐서만 내면 나중에 상수를 실측으로 교체할 때 무엇이 바뀌었는지 못 읽는다.
            var m5 = M5Of(Spread(1, 5));

            foreach (string key in new[] { "combatMinutesAvg", "interactionSecondsPerRound",
                                           "avgRoundsReached", "runMinutesAvg" })
                Assert.That(m5[key], Is.Not.Null, key);

            Assert.That((string)m5["_provisional"]!, Does.Contain("D+6"),
                        "잠정값이라는 사실과 교체 시점이 리포트에 없다");
        }

        [Test]
        public void 조작_시간_상수는_데이터에_없다()
        {
            // ★★ 일부러 /data 에 두지 않았다. 그쪽은 최적화기가 값을 쓰는 표면이라,
            //    거기 있으면 최적화기가 이 숫자를 올려서 M5 를 통과시킬 수 있다 —
            //    게임은 하나도 안 바꾸고 "플레이어가 느리다" 고 주장해서 점수를 얻는다.
            //    측정 모델의 가정은 최적화 대상이 아니다.
            foreach (string file in new[] { "economy.json", "characters.json", "meta.json" })
                Assert.That(RepoData.Read(file), Does.Not.Contain("interactionSeconds"),
                            $"{file} 에 조작 시간 상수가 들어갔다 — 최적화기가 지표를 살 수 있게 된다");
        }

        // ────────────────────────────── M6 (D-72)

        [Test]
        public void M6_는_못_잰다는_사실을_값_옆에_붙여_낸다()
        {
            // 숫자만 보면 "2라운드에 몰린다" 로 읽힌다. 그건 게임의 성질이 아니라
            // 봇이 저축을 안 하도록 설계된 결과다(08 §6.1). 리포트를 읽는 사람이
            // 그 구분을 스스로 해내야 하는 상태로 두지 않는다.
            var m6 = (JObject)Metrics.Compute(_data, Spread(5, 2, 2, 2), 20)["M6"]!;

            Assert.That(m6["_notMeasurable"], Is.Not.Null);
            Assert.That((string)m6["_notMeasurable"]!, Does.Contain("D-72"));
        }
    }
}
