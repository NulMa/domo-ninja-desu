using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Economy;
using DomoNinja.Core.Events;
using DomoNinja.Core.Rng;
using NUnit.Framework;

namespace DomoNinja.Core.Tests
{
    /// <summary>
    /// 결정론 회귀 — <b>런 단위 결과 해시</b> (`19` §6.3 · `_schema` §7).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <c>BattleSimulatorTests</c> 의 「이벤트 열 일치」로는 부족하다. 그건 <b>전투 하나</b>를 본다.
    /// 런은 그 위에 <b>상점 추첨 · 관문 변형 · HP 누적 · 생명 · 메타</b> 가 얹히고,
    /// 그 층들은 각자 난수 스트림을 갖는다 — <b>전투가 완벽히 결정적이어도
    /// 스트림을 한 번 잘못 갈라 쓰면 런 결과가 갈린다.</b>
    /// </para>
    /// <para>
    /// 그리고 그 어긋남은 <b>지표로 안 보인다.</b> 클리어율은 여전히 그럴듯한 숫자를 내고,
    /// 밸런스 루프는 <b>재현되지 않는 값을 최적화한다.</b>
    /// </para>
    /// <para>
    /// ⚠️ <b>기대 해시를 코드에 박지 않는다.</b> 데이터가 바뀌면(최적화기가 값을 쓴다) 해시도 바뀌므로,
    /// 박아두면 <b>밸런스 패치마다 이 테스트가 깨진다</b> — 그러면 사람이 숫자를 갱신하게 되고
    /// 그 순간 이 테스트는 아무것도 지키지 않는다. <b>같은 실행 안에서 두 번 돌려 비교한다.</b>
    /// </para>
    /// </remarks>
    [TestFixture]
    public class RunDeterminismTests
    {
        private static GameData _data = null!;

        [OneTimeSetUp]
        public void Load() => _data = RepoData.LoadAll();

        private static CombatConfig Config() => CombatConfig.From(_data.Economy, 20);

        /// <summary>
        /// 런 하나의 결과를 <b>한 문자열</b>로 접는다. 라운드별 승패·틱·재화까지 들어간다.
        /// </summary>
        /// <remarks>
        /// 최종 클리어 여부만 비교하면 <b>중간이 다른데 결과가 같은</b> 경우를 놓친다 —
        /// 상점 추첨이 밀려 다른 스킬을 샀는데 우연히 똑같이 클리어하는 식이다.
        /// </remarks>
        private static string Fingerprint(RunSummary s)
        {
            var sb = new StringBuilder();
            sb.Append(s.Cleared ? 'C' : '-')
              .Append('|').Append(s.RoundsWon)
              .Append('|').Append(s.RoundsReached)
              .Append('|').Append(s.LivesLeft)
              .Append('|').Append(s.TotalTicks)
              .Append('|').Append(s.TotalUnitTicks)
              .Append('|').Append(s.FirstActivationRound);

            foreach (var r in s.Rounds)
                sb.Append("\n  ").Append(r.Round)
                  .Append(':').Append(r.VariantId)
                  .Append(':').Append(r.Won ? 'W' : 'L')
                  .Append(':').Append(r.Ticks)
                  .Append(':').Append(r.UnitTicks)
                  .Append(':').Append(r.CurrencyGained)
                  .Append(':').Append(r.EntryHpPermille)
                  .Append(':').Append(r.TimedOut ? 'T' : '-');

            return sb.ToString();
        }

        private static string Hash(IEnumerable<string> parts)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", parts)));
                var hex = new StringBuilder(16);
                for (int i = 0; i < 8; i++) hex.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        /// <summary>빌드 하나를 시드 하나로 끝까지 돌린다.</summary>
        private static RunSummary PlayOne(BuildTarget build, ulong seed)
        {
            var engine = new RunEngine(_data, Config());
            var meta = new MetaProgress(_data.Meta);
            var run = engine.StartRun("S1", build.CharacterIds, meta);

            return engine.PlayRun(run, meta, new DeterministicRandom(seed),
                                  NullEventSink.Instance, collectLogs: false, build: build);
        }

        private static IReadOnlyList<BuildTarget> Builds(int count) =>
            BuildSpace.Enumerate(_data).Take(count).ToList();

        // ──────────────────────────────

        [Test]
        public void 같은_시드로_런을_두_번_돌리면_해시가_같다()
        {
            var builds = Builds(40);

            string Once() => Hash(
                from b in builds
                from seed in new ulong[] { 1, 2, 3 }
                select Fingerprint(PlayOne(b, seed)));

            Assert.That(Once(), Is.EqualTo(Once()),
                        "런 단위 결과가 같은 시드에서 갈렸다 — 밸런스 수치 전체가 의미를 잃는다");
        }

        [Test]
        public void 시드가_다르면_런_결과가_달라진다()
        {
            // 위 테스트만 있으면 "전부 상수를 돌려주는" 구현도 통과한다.
            var build = Builds(1)[0];

            string a = Fingerprint(PlayOne(build, 1));
            string b = Fingerprint(PlayOne(build, 999));

            Assert.That(b, Is.Not.EqualTo(a), "시드를 바꿔도 결과가 같다 — 난수가 안 쓰이고 있다");
        }

        [Test]
        public void 빌드가_다르면_런_결과가_달라진다()
        {
            // 빌드 목표가 상점 구매를 통해 실제로 결과에 닿는지. 안 닿으면
            // 4,320 빌드를 전수 탐색해도 전부 같은 게임을 재게 된다.
            var builds = Builds(30);
            var seen = new HashSet<string>();

            foreach (var b in builds) seen.Add(Fingerprint(PlayOne(b, 7)));

            Assert.That(seen.Count, Is.GreaterThan(1), "빌드를 바꿔도 결과가 전부 같다");
        }

        [Test]
        public void 앞선_런이_뒤따르는_런에_영향을_주지_않는다()
        {
            // ★ 런 사이에 상태가 새면 "같은 시드 2회" 는 통과하면서
            //   순서만 바꾸면 결과가 달라진다. sim 은 런을 병렬로 돌리므로
            //   여기서 새는 게 있으면 병렬화 자체가 결정론을 깬다.
            var builds = Builds(12);

            string Solo(BuildTarget b) => Fingerprint(PlayOne(b, 5));

            var alone = builds.Select(Solo).ToList();

            // 순서를 뒤집어 다시 돌린다.
            var reversed = builds.AsEnumerable().Reverse().Select(Solo).Reverse().ToList();

            for (int i = 0; i < alone.Count; i++)
                Assert.That(reversed[i], Is.EqualTo(alone[i]),
                            $"{builds[i].Id} 가 실행 순서에 따라 다른 결과를 냈다");
        }

        [Test]
        public void 병렬로_돌려도_순차와_같은_결과가_나온다()
        {
            // sim 은 런 단위 병렬을 쓴다(`17` §7). "성능 때문에 재현성을 포기했다" 가
            // 없어야 05 §2.4 가 유지된다.
            var builds = Builds(24);

            var sequential = builds.Select(b => Fingerprint(PlayOne(b, 11))).ToList();

            var parallel = new string[builds.Count];
            System.Threading.Tasks.Parallel.For(0, builds.Count,
                i => parallel[i] = Fingerprint(PlayOne(builds[i], 11)));

            Assert.That(parallel, Is.EqualTo(sequential),
                        "병렬 실행이 다른 결과를 냈다 — 공유 상태가 있다");
        }
    }
}
