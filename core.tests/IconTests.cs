using System.Linq;
using DomoNinja.Core.Data;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace DomoNinja.Core.Tests
{
    /// <summary>
    /// 스킬 아이콘 경로 (`R22`).
    /// </summary>
    /// <remarks>
    /// 팀원 제안(2026-08-02)으로 들어온 필드다. 값은 아트 작업과 함께 채워지므로
    /// <b>비어 있어도 통과</b>하고, 채워진 것만 규약과 대조한다.
    /// </remarks>
    [TestFixture]
    public class IconTests
    {
        /// <summary>모든 스킬에 규약대로 아이콘을 채운 `skills.json`.</summary>
        private static string WithIcons(string? overrideId = null, string? overrideValue = null)
        {
            var skills = RepoData.Json("skills.json");
            var characters = RepoData.Json("characters.json");

            string Folder(string charId)
            {
                var c = ((JArray)characters["characters"]!)
                    .First(x => (string?)x["id"] == charId);
                string sprite = (string)c["sprite"]!;
                return sprite.Substring(sprite.LastIndexOf('/') + 1);
            }

            void Fill(string key, string kind)
            {
                foreach (var s in (JArray)skills[key]!)
                {
                    string id = (string)s["id"]!;
                    s["icon"] = id == overrideId
                        ? overrideValue
                        : $"Skill/{Folder((string)s["character"]!)}/{kind}/{id}_{(string?)s["name"]}";
                }
            }

            Fill("skills", "Main");
            Fill("supportSkills", "Support");
            return skills.ToString();
        }

        private static GameData Load(string skillsJson) =>
            GameDataLoader.Load(RepoData.Characters, skillsJson,
                                RepoData.Encounters, RepoData.Economy, RepoData.Meta);

        [Test]
        public void 아이콘이_없어도_통과한다()
        {
            // 아이콘은 아트 작업과 함께 들어온다. 필수로 걸면 그림이 다 나올 때까지
            // 시뮬을 못 돌린다 — 그건 밸런스 루프를 아트에 묶는 것이다.
            //
            // ★ 처음엔 "지금 데이터에 아이콘이 없다"로 적었는데, 팀원이 30개를 넣자마자 깨졌다.
            //   확인하려던 건 데이터의 현재 상태가 아니라 "없어도 된다"는 성질이므로,
            //   일부러 지운 데이터로 본다. 상태를 박아 둔 테스트는 상대가 일하면 깨진다.
            var stripped = RepoData.Json("skills.json");
            foreach (string key in new[] { "skills", "supportSkills" })
                foreach (var s in (JArray)stripped[key]!)
                    ((JObject)s).Remove("icon");

            var data = Load(stripped.ToString());

            Assert.That(data.Skills.All(s => s.Icon == null), Is.True);
            Assert.That(data.Skills.Count, Is.EqualTo(12), "아이콘이 없어도 로드는 끝까지 간다");
        }

        [Test]
        public void 저장소의_실제_아이콘_30개가_규약과_맞는다()
        {
            // 팀원이 넣은 실제 값을 검사한다. R22 를 만든 이유가 이 순간이다 —
            // 오타나 개명 누락이 있으면 화면에서 아이콘만 조용히 사라진다.
            var data = Load(RepoData.Skills);
            var all = data.Skills.Concat(data.SupportSkills).ToList();

            Assert.That(all.Count(s => s.Icon != null), Is.EqualTo(30));
            Assert.That(data.FindSkill("C1-A")!.Icon, Does.StartWith("Skill/Samurai/Main/"));
            Assert.That(data.FindSkill("C6-P3")!.Icon, Does.StartWith("Skill/Shaman/Support/"));
        }

        [Test]
        public void 규약대로_채우면_통과하고_core_가_그대로_실어_보낸다()
        {
            var data = Load(WithIcons());

            Assert.That(data.FindSkill("C1-A")!.Icon, Is.EqualTo("Skill/Samurai/Main/C1-A_일격"));
            Assert.That(data.FindSkill("C5-P2")!.Icon,
                        Is.EqualTo("Skill/NinjaMageBlack/Support/C5-P2_결계"));
        }

        [Test]
        public void 액티브와_보조가_다른_폴더로_간다()
        {
            var data = Load(WithIcons());

            Assert.That(data.FindSkill("C6-B")!.Icon, Does.Contain("/Main/"));
            Assert.That(data.FindSkill("C6-P3")!.Icon, Does.Contain("/Support/"));
        }

        [Test]
        public void 이름을_바꾸고_파일명을_안_바꾸면_잡힌다()
        {
            // ★ 이 규칙을 만든 이유 그 자체다.
            //   경로가 id 와 name 을 중복해서 담으므로, 이름만 고치면
            //   컴파일도 로드도 안 걸리고 화면에서 아이콘만 조용히 사라진다.
            var skills = JObject.Parse(WithIcons());
            ((JArray)skills["skills"]!).First(s => (string?)s["id"] == "C1-A")["name"] = "일섬";

            var ex = Assert.Throws<DataValidationException>(() => Load(skills.ToString()))!;

            Assert.That(ex.Errors.Any(e => e.Rule == "R22" && e.Where.Contains("C1-A")), Is.True);
        }

        [Test]
        public void 오타난_경로를_잡는다()
        {
            var ex = Assert.Throws<DataValidationException>(
                () => Load(WithIcons("C3-A", "Skill/NinjaRed/Main/C3-A_그림자.png")))!;

            Assert.That(ex.Errors.Any(e => e.Rule == "R22"), Is.True, "확장자가 붙으면 규약 위반이다");
        }

        [Test]
        public void 캐릭터_폴더가_틀리면_잡는다()
        {
            // C3 는 NinjaRed 인데 Samurai 밑을 가리키는 경우.
            var ex = Assert.Throws<DataValidationException>(
                () => Load(WithIcons("C3-B", "Skill/Samurai/Main/C3-B_표창")))!;

            Assert.That(ex.Errors.Any(e => e.Rule == "R22" && e.Where.Contains("C3-B")), Is.True);
        }
    }
}
