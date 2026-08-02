using System.Collections.Generic;
using System.Linq;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using DomoNinja.Core.Economy;
using DomoNinja.Core.Events;
using DomoNinja.Core.Rng;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace DomoNinja.Core.Tests
{
    /// <summary>
    /// 스테이지별 관문 세트 (`D-68`).
    /// </summary>
    /// <remarks>
    /// 팀원이 스테이지 2 를 <b>별도 파일</b>로 저작하면서
    /// *"core 로더가 아직 다중 스테이지를 모르므로 로딩 연결은 T1 과 별도 조율 필요"* 라고 남겼다.
    /// 여기가 그 연결이다. 실제 파일이 `main` 에 들어오기 전이라 <b>합성 세트로 계약을 먼저 세운다.</b>
    /// </remarks>
    [TestFixture]
    public class EncounterSetTests
    {
        /// <summary>팀원 파일과 같은 모양의 최소 세트 — <c>rounds</c> 만 있고 적 테이블은 공유한다.</summary>
        private static string Stage2Json(string enemyType = "kappa", int x = 4, int y = 2)
        {
            var rounds = new JArray();
            for (int i = 1; i <= 8; i++)
            {
                rounds.Add(new JObject
                {
                    ["round"] = i,
                    ["axisTested"] = $"S2 R{i}",
                    ["variants"] = new JArray
                    {
                        new JObject
                        {
                            ["id"] = $"S2R{i}",
                            ["units"] = new JArray
                            {
                                new JObject { ["type"] = enemyType, ["x"] = x, ["y"] = y },
                            },
                        },
                    },
                });
            }

            return new JObject { ["stage"] = 2, ["rounds"] = rounds }.ToString();
        }

        private static GameData Load(string? stage2 = null) =>
            GameDataLoader.Load(
                RepoData.Characters, RepoData.Skills, RepoData.Encounters, RepoData.Economy, RepoData.Meta,
                stage2 == null ? null : new Dictionary<string, string> { ["stage2"] = stage2 });

        [Test]
        public void 세트를_안_주면_스테이지1만_있다()
        {
            var data = Load();

            Assert.That(data.HasEncounterSetFor("S1"), Is.True);
            Assert.That(data.HasEncounterSetFor("S2"), Is.False);
        }

        [Test]
        public void 없는_스테이지는_스테이지1로_떨어진다()
        {
            // 예외를 던지지 않는 이유 — 저작이 아직 안 들어왔을 때 시뮬 전체가 멈추면
            // 아트·저작 진행이 밸런스 루프를 막는다.
            var data = Load();

            Assert.That(data.RoundsFor("S2"), Is.SameAs(data.Rounds));
        }

        [Test]
        public void 세트를_주면_스테이지2가_갈린다()
        {
            var data = Load(Stage2Json());

            Assert.That(data.HasEncounterSetFor("S2"), Is.True);
            Assert.That(data.RoundsFor("S2"), Is.Not.SameAs(data.Rounds));
            Assert.That(data.RoundsFor("S2").Count, Is.EqualTo(8));
            Assert.That(data.RoundsFor("S2")[0].Variants[0].Id, Is.EqualTo("S2R1"));
        }

        [Test]
        public void 적_타입_테이블은_스테이지가_공유한다()
        {
            // ★ 스테이지마다 적 스탯을 따로 두면 같은 이름의 적이 스테이지별로 다른 값을 갖고,
            //   밸런스 리포트에서 "슬라임이 약한가"를 물을 수 없게 된다.
            //   난이도는 개체 조합과 수량으로만 올린다 — 팀원 저작 노트와 같은 판단이다.
            var data = Load(Stage2Json());

            Assert.That(data.EnemyTypes.ContainsKey("kappa"), Is.True);
            Assert.That(data.RoundsFor("S2").All(r => r.Variants.All(
                v => v.Units.All(u => data.EnemyTypes.ContainsKey(u.Type)))), Is.True);
        }

        // ────────────────────────────── 검증이 똑같이 걸린다

        [Test]
        public void 스테이지2의_없는_적_타입도_잡는다()
        {
            var ex = Assert.Throws<DataValidationException>(() => Load(Stage2Json(enemyType: "dragon")))!;

            Assert.That(ex.Errors.Any(e => e.Rule == "R13" && e.Where.Contains("stage2")), Is.True,
                "한쪽만 검사하면 나중에 들어온 쪽이 무방비가 된다");
        }

        [Test]
        public void 스테이지2의_진영_밖_좌표도_잡는다()
        {
            var ex = Assert.Throws<DataValidationException>(() => Load(Stage2Json(x: 1)))!;

            Assert.That(ex.Errors.Any(e => e.Rule == "R14" && e.Where.Contains("stage2")), Is.True);
        }

        [Test]
        public void 스테이지2에_라운드가_비어도_잡는다()
        {
            var partial = JObject.Parse(Stage2Json());
            ((JArray)partial["rounds"]!).RemoveAt(3);

            var ex = Assert.Throws<DataValidationException>(() => Load(partial.ToString()))!;

            Assert.That(ex.Errors.Any(e => e.Rule == "R12" && e.Where.Contains("stage2")), Is.True);
        }

        [Test]
        public void 오류_출처에_어느_세트인지_찍힌다()
        {
            // 세트가 둘 이상이면 "encounters.json" 한 줄로는 어디를 고칠지 알 수 없다.
            var ex = Assert.Throws<DataValidationException>(() => Load(Stage2Json(enemyType: "dragon")))!;

            Assert.That(ex.Message, Does.Contain("stage2"));
        }

        // ────────────────────────────── 런이 실제로 갈린다

        [Test]
        public void 스테이지2를_고르면_그_관문을_돈다()
        {
            var data = Load(Stage2Json());
            var engine = new RunEngine(data, CombatConfig.From(data.Economy, 20));
            var meta = new MetaProgress(data.Meta);

            var run = engine.StartRun("S2", new[] { "C1", "C2", "C4" }, meta);
            var summary = engine.PlayRun(run, meta, new DeterministicRandom(1), NullEventSink.Instance);

            Assert.That(summary.Rounds.All(r => r.VariantId.StartsWith("S2R")), Is.True,
                "스테이지 2 를 골랐는데 스테이지 1 관문을 돌면 D-68 이 무의미해진다");
        }

        [Test]
        public void 스테이지1은_그대로_돈다()
        {
            var data = Load(Stage2Json());
            var engine = new RunEngine(data, CombatConfig.From(data.Economy, 20));
            var meta = new MetaProgress(data.Meta);

            var run = engine.StartRun("S1", new[] { "C1", "C2", "C4" }, meta);
            var summary = engine.PlayRun(run, meta, new DeterministicRandom(1), NullEventSink.Instance);

            Assert.That(summary.Rounds.Any(r => r.VariantId.StartsWith("S2R")), Is.False);
        }
    }
}
