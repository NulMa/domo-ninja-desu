using System;
using System.Collections.Generic;
using System.Linq;
using DomoNinja.Core.Data;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace DomoNinja.Core.Tests
{
    /// <summary>
    /// 파일 조립 — <b>어느 파일이 어느 스테이지인지는 데이터가 안다</b> (`GameDataFiles`).
    /// </summary>
    [TestFixture]
    public class GameDataFilesTests
    {
        /// <summary>스테이지 2 파일이 있는 것처럼 꾸민 읽기 함수.</summary>
        private static Func<string, string?> ReaderWithStage2(string? stage2Json)
        {
            return name => name == "encountersStage2.json" ? stage2Json : RepoData.TryRead(name);
        }

        private static string Stage2Json(int? declaredStage = 2)
        {
            var rounds = new JArray();
            for (int i = 1; i <= 8; i++)
            {
                rounds.Add(new JObject
                {
                    ["round"] = i,
                    ["axisTested"] = "s2",
                    ["variants"] = new JArray
                    {
                        new JObject
                        {
                            ["id"] = $"S2R{i}",
                            ["units"] = new JArray
                            {
                                new JObject { ["type"] = "kappa", ["x"] = 4, ["y"] = 2 },
                            },
                        },
                    },
                });
            }

            var root = new JObject { ["rounds"] = rounds };
            if (declaredStage != null) root["stage"] = declaredStage.Value;
            return root.ToString();
        }

        [Test]
        public void 저장소의_실제_데이터를_파일_이름_없이_읽는다()
        {
            // ★ 호출부가 파일 이름을 하나도 몰라도 된다. 그게 이 타입의 존재 이유다.
            var data = RepoData.LoadAll();

            Assert.That(data.Characters.Count, Is.EqualTo(6));
            Assert.That(data.Rounds.Count, Is.EqualTo(8));
        }

        [Test]
        public void 없는_스테이지_파일은_조용히_건너뛴다()
        {
            // 저작이 안 들어온 스테이지 때문에 시뮬 전체가 멈추면
            // 저작 진행이 밸런스 루프를 막는다.
            //
            // ★ 두 번째로 같은 실수를 했다. 처음엔 "encountersStage2.json 은 아직 main 에 없다"로
            //   저장소의 현재 상태를 단언했는데, 팀원이 파일을 올리자마자 깨졌다.
            //   아이콘 테스트도 같은 형태였다(팀원이 icon 30개를 넣자 깨졌다).
            //   → 공유 데이터에 대한 테스트는 "지금 이렇다"가 아니라 "이래야 한다"를 적는다.
            //     상태를 박아 두면 상대가 일할 때마다 깨진다.
            var data = GameDataFiles.Load(name => name == "encountersStage2.json" ? null : RepoData.TryRead(name));

            Assert.That(data.HasEncounterSetFor("S1"), Is.True);
            Assert.That(data.HasEncounterSetFor("S2"), Is.False);
            Assert.That(data.RoundsFor("S2"), Is.SameAs(data.Rounds), "없으면 스테이지 1 로 떨어진다");
        }

        [Test]
        public void 저장소에_들어온_스테이지2가_자동으로_붙는다()
        {
            // 팀원이 파일을 올리기만 했고 core 는 손대지 않았다.
            // economy.stages.list 의 encounterFile 매핑만으로 연결된다.
            var data = RepoData.LoadAll();

            Assert.That(data.HasEncounterSetFor("S2"), Is.True);
            Assert.That(data.RoundsFor("S2").Count, Is.EqualTo(8));
            Assert.That(data.RoundsFor("S2").Sum(r => r.Variants.Count), Is.EqualTo(14));
        }

        [Test]
        public void 파일이_생기면_economy_의_매핑을_따라_자동으로_붙는다()
        {
            // 팀원이 파일을 넣기만 하면 core 는 손댈 게 없다.
            var data = GameDataFiles.Load(ReaderWithStage2(Stage2Json()));

            Assert.That(data.HasEncounterSetFor("S2"), Is.True);
            Assert.That(data.RoundsFor("S2")[0].Variants[0].Id, Is.EqualTo("S2R1"));
        }

        [Test]
        public void 필수_파일이_없으면_바로_실패한다()
        {
            var ex = Assert.Throws<DataValidationException>(
                () => GameDataFiles.Load(name => name == "skills.json" ? null : RepoData.TryRead(name)))!;

            Assert.That(ex.Message, Does.Contain("skills.json"));
        }

        // ────────────────────────────── R23

        [Test]
        public void 파일이_적어둔_stage_가_연결된_자리와_다르면_잡는다()
        {
            // ★ 파일을 잘못된 세트에 연결하면 스테이지 2 를 골랐는데 다른 관문이 나오고,
            //   그건 로드도 전투도 정상으로 보인다. 자기 이름을 적어둔 파일에서만 가능한 검사다.
            var ex = Assert.Throws<DataValidationException>(
                () => GameDataFiles.Load(ReaderWithStage2(Stage2Json(declaredStage: 3))))!;

            Assert.That(ex.Errors, Has.Some.Matches<ValidationError>(e => e.Rule == "R23"));
        }

        [Test]
        public void stage_를_안_적어도_통과한다()
        {
            // 필드는 선택이다. 있는데 어긋날 때만 잡는다.
            var data = GameDataFiles.Load(ReaderWithStage2(Stage2Json(declaredStage: null)));

            Assert.That(data.HasEncounterSetFor("S2"), Is.True);
        }

        [Test]
        public void 매핑이_데이터에만_있고_코드에는_없다()
        {
            // 파일 이름이 코드에 흩어지면 스테이지가 늘 때마다 sim·Unity·테스트 세 곳을 고쳐야 하고,
            // 한 곳만 빠뜨리면 그쪽에서만 스테이지가 없는 채로 조용히 돌아간다.
            var economy = JObject.Parse(RepoData.Economy);
            var list = (JArray)economy["stages"]!["list"]!;

            foreach (var entry in list)
                Assert.That((string?)entry["encounterFile"], Is.Not.Null,
                    $"{(string?)entry["id"]} 에 encounterFile 이 없다 — core 가 그 스테이지를 못 찾는다");
        }

        [Test]
        public void 읽기_함수를_받는다_파일을_직접_열지_않는다()
        {
            // WebGL 에는 파일 시스템이 없다. core 가 File 을 부르면 dotnet 에서만 되고
            // 브라우저에서 깨지는데, 그 차이는 WebGL 빌드를 돌리기 전까지 안 드러난다.
            var requested = new List<string>();
            GameDataFiles.Load(name => { requested.Add(name); return RepoData.TryRead(name); });

            Assert.That(requested, Does.Contain("characters.json"));
            Assert.That(requested, Does.Contain("encountersStage2.json"),
                "economy 가 가리킨 파일은 없더라도 요청은 해야 한다");
        }
    }
}
