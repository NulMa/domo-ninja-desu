using System;
using System.Collections.Generic;
using System.Linq;
using DomoNinja.Core.Data;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace DomoNinja.Core.Tests
{
    /// <summary>
    /// 데이터 로더와 `_schema` §6 검증 규칙 20개(+ 추가 1개).
    /// </summary>
    /// <remarks>
    /// ★ 이 파일의 절반은 <b>일부러 데이터를 깨뜨린다.</b>
    /// "정상 데이터가 통과한다"만 보면 규칙이 <b>한 줄도 실행되지 않아도</b> 초록이 된다.
    /// 검증기의 값어치는 통과가 아니라 <b>걸러내는 것</b>에 있으므로,
    /// 규칙마다 그 규칙에만 걸리는 위반을 하나씩 만들어 실제로 잡히는지 본다.
    /// </remarks>
    [TestFixture]
    public class DataLoaderTests
    {
        // ────────────────────────────── 정상 경로

        [Test]
        public void 저장소의_실제_데이터가_계약을_만족한다()
        {
            // 이게 깨지면 코드가 아니라 /data 가 잘못된 것이다. 오류 목록이 어디를 고칠지 알려준다.
            var data = LoadReal();

            Assert.That(data.Characters.Count, Is.EqualTo(6));
            Assert.That(data.Skills.Count, Is.EqualTo(12));
            Assert.That(data.SupportSkills.Count, Is.EqualTo(18));
            Assert.That(data.Rounds.Count, Is.EqualTo(8));
        }

        [Test]
        public void 보드_계약이_코드_상수와_일치한다()
        {
            var data = LoadReal();

            Assert.That(data.Economy.BoardCols * 2, Is.EqualTo(8));
            Assert.That(data.Economy.BoardRows, Is.EqualTo(6));
        }

        [Test]
        public void 조회는_id_로_되고_리스트_순서는_보존된다()
        {
            var data = LoadReal();

            Assert.That(data.FindCharacter("C3")!.Name, Is.EqualTo("적영"));
            Assert.That(data.FindSkill("C1-A")!.Name, Is.EqualTo("일격"));
            Assert.That(data.FindCharacter("없는캐릭"), Is.Null);

            // 순서가 곧 슬롯 인덱스이고 모든 동률 판정의 최종 타이브레이커다 (_schema §7).
            Assert.That(data.Characters[0].Id, Is.EqualTo("C1"));
            Assert.That(data.Characters[5].Id, Is.EqualTo("C6"));
        }

        [Test]
        public void 고정포대는_moveInterval_없이도_통과한다()
        {
            var data = LoadReal();
            var totem = data.EnemyTypes["totem"];

            Assert.That(totem.Immobile, Is.True);
            Assert.That(totem.MoveInterval, Is.Null, "immobile 인 적은 moveInterval 이 없어도 된다 (R19 예외)");
        }

        // ────────────────────────────── 깨진 데이터

        [Test]
        public void 깨진_JSON_은_파싱_단계에서_잡힌다()
        {
            var ex = Assert.Throws<DataValidationException>(() =>
                GameDataLoader.Load("{ 이건 JSON 이 아니다", RepoData.Skills,
                                    RepoData.Encounters, RepoData.Economy, RepoData.Meta))!;

            Assert.That(ex.Errors.Any(e => e.Rule == "PARSE"), Is.True);
        }

        [Test]
        public void 필드가_빠지면_기본값으로_넘어가지_않는다()
        {
            // ★ 이게 타입 자동 역직렬화를 안 쓰는 이유 그 자체다.
            //    ToObject<T> 였다면 hp 가 0 인 캐릭터가 조용히 만들어지고,
            //    "데이터가 빠졌다"가 아니라 "첫 틱에 죽는 밸런스 버그"로 보였을 것이다.
            var errors = ErrorsAfter(f =>
                ((JArray)f.Characters["characters"]!)[0]!.Value<JObject>()!.Remove("hp"));

            Assert.That(errors.Any(e => e.Rule == "PARSE" && e.Message.Contains("hp")), Is.True);
        }

        [Test]
        public void R01_캐릭터_수가_poolSize_와_다르면_잡는다()
        {
            var errors = ErrorsAfter(f => ((JArray)f.Characters["characters"]!).RemoveAt(5));
            AssertCaught(errors, "R01");
        }

        [Test]
        public void R02_액티브가_2개가_아니면_잡는다()
        {
            var errors = ErrorsAfter(f =>
            {
                var skills = (JArray)f.Skills["skills"]!;
                skills.Add(new JObject
                {
                    ["id"] = "C1-C", ["character"] = "C1", ["name"] = "덤", ["role"] = "브루저",
                    ["effects"] = new JArray(),
                    ["text"] = new JObject { ["gain"] = "x", ["cost"] = "y" },
                });
            });

            AssertCaught(errors, "R02");
        }

        [Test]
        public void R03_얻는_것만_있는_스킬을_잡는다()
        {
            // 한쪽이 그냥 좋은 스킬은 배타 선택을 가짜로 만든다 — M3b 가 즉시 깨진다.
            var errors = ErrorsAfter(f =>
                ((JObject)((JArray)f.Skills["skills"]!)[0]!["text"]!).Remove("cost"));

            AssertCaught(errors, "R03");
        }

        [Test]
        public void R04_보조가_3개가_아니면_잡는다()
        {
            var errors = ErrorsAfter(f => ((JArray)f.Skills["supportSkills"]!).RemoveAt(0));
            AssertCaught(errors, "R04");
        }

        [Test]
        public void R05_보조_세_자리가_공격_생존_팀_하나씩이_아니면_잡는다()
        {
            // C1-P2(생존) 를 공격으로 바꾸면 공격 2 · 생존 0 이 된다.
            var errors = ErrorsAfter(f => ((JArray)f.Skills["supportSkills"]!)[1]!["slot"] = "공격");
            AssertCaught(errors, "R05");
        }

        [Test]
        public void R06_알_수_없는_role_을_잡는다()
        {
            var errors = ErrorsAfter(f => ((JArray)f.Skills["skills"]!)[0]!["role"] = "만능딜러");
            AssertCaught(errors, "R06");
        }

        [Test]
        public void R07_상한_없는_보호막_초과분을_잡는다()
        {
            // overflowToHp 는 "상한을 넘은 만큼"인데 상한이 없으면 정의 자체가 성립하지 않는다.
            var errors = ErrorsAfter(f =>
            {
                var eff = FindEffectWithShield(f.Skills);
                eff.Remove("maxPermille");
            });

            AssertCaught(errors, "R07");
        }

        [Test]
        public void R08_heal_이_두_방식을_동시에_쓰면_잡는다()
        {
            var errors = ErrorsAfter(f =>
            {
                var heal = FindHealEffect(f.Skills);
                heal["permille"] = 60;
                heal["fromDamagePermille"] = 150;
            });

            AssertCaught(errors, "R08");
        }

        [Test]
        public void R09_폐기된_upgrades_가_남아_있으면_잡는다()
        {
            // v0.6 에서 3단계 누적 강화를 폐기하고 보조 스킬로 통합했다.
            // 남아 있다면 그건 마이그레이션 누락이지 설계가 아니다.
            var errors = ErrorsAfter(f =>
                ((JObject)((JArray)((JArray)f.Skills["skills"]!)[0]!["effects"]!)[0]!)["upgrades"] = new JArray());

            AssertCaught(errors, "R09");
        }

        [Test]
        public void R10_템플릿_5종_밖의_값을_잡는다()
        {
            var errors = ErrorsAfter(f =>
                ((JArray)((JArray)f.Skills["skills"]!)[0]!["effects"]!)[0]!["template"] = "teleport");

            AssertCaught(errors, "R10");
        }

        [Test]
        public void R11_트리거_상태이상_타겟팅의_알_수_없는_값을_잡는다()
        {
            var badTrigger = ErrorsAfter(f => FindTriggerEffect(f.Skills)["trigger"]!["type"] = "on_sneeze");
            AssertCaught(badTrigger, "R11");

            var badStatus = ErrorsAfter(f => FindStatusEffect(f.Skills)["kind"] = "confused");
            AssertCaught(badStatus, "R11");

            var badPriority = ErrorsAfter(f => FindTargetingEffect(f.Skills)["priority"] = "random");
            AssertCaught(badPriority, "R11");
        }

        [Test]
        public void R12_라운드가_비면_잡는다()
        {
            var errors = ErrorsAfter(f => ((JArray)f.Encounters["rounds"]!).RemoveAt(4));
            AssertCaught(errors, "R12");
        }

        [Test]
        public void R13_정의되지_않은_적_타입을_잡는다()
        {
            var errors = ErrorsAfter(f => FirstUnit(f.Encounters)["type"] = "dragon");
            AssertCaught(errors, "R13");
        }

        [Test]
        public void R14_적_진영_밖의_좌표를_잡는다()
        {
            // x=1 은 아군 진영이다. 여기 적을 두면 배치 화면부터 규칙이 무너진다.
            var errors = ErrorsAfter(f => FirstUnit(f.Encounters)["x"] = 1);
            AssertCaught(errors, "R14");
        }

        [Test]
        public void R15_한_구성_안의_좌표_중복을_잡는다()
        {
            var errors = ErrorsAfter(f =>
            {
                var units = (JArray)((JArray)((JArray)f.Encounters["rounds"]!)[0]!["variants"]!)[0]!["units"]!;
                units[1]!["x"] = units[0]!["x"];
                units[1]!["y"] = units[0]!["y"];
            });

            AssertCaught(errors, "R15");
        }

        [Test]
        public void R16_진영_칸_수를_넘는_적_수를_잡는다()
        {
            var errors = ErrorsAfter(f =>
            {
                var units = (JArray)((JArray)((JArray)f.Encounters["rounds"]!)[0]!["variants"]!)[0]!["units"]!;
                for (int i = 0; i < 30; i++)
                    units.Add(new JObject { ["type"] = "slime", ["x"] = 4, ["y"] = 0 });
            });

            AssertCaught(errors, "R16");
        }

        [Test]
        public void R17_보조_스킬_총수가_기대값과_다르면_잡는다()
        {
            var errors = ErrorsAfter(f => ((JArray)f.Skills["supportSkills"]!).RemoveAt(0));
            AssertCaught(errors, "R17");
        }

        [Test]
        public void R18_없는_캐릭터를_가리키는_보조를_잡는다()
        {
            var errors = ErrorsAfter(f => ((JArray)f.Skills["supportSkills"]!)[0]!["character"] = "C9");
            AssertCaught(errors, "R18");
        }

        [Test]
        public void R19_moveInterval_0_을_잡는다()
        {
            // 0 이면 매 틱 이동하게 되어 사실상 무한 이동이 된다.
            var errors = ErrorsAfter(f => ((JArray)f.Characters["characters"]!)[0]!["moveInterval"] = 0);
            AssertCaught(errors, "R19");

            // immobile 이 아닌 적이 moveInterval 을 아예 안 갖는 경우도 같은 규칙이다.
            var missing = ErrorsAfter(f => ((JObject)f.Encounters["enemyTypes"]!["slime"]!).Remove("moveInterval"));
            AssertCaught(missing, "R19");
        }

        [Test]
        public void R20_고정포대_속성이_아군에_붙으면_잡는다()
        {
            // A5 — 고정포대는 적 전용이다. 아군이 가지면 플레이어 빌드 공간이 통째로 달라진다.
            var errors = ErrorsAfter(f => ((JArray)f.Characters["characters"]!)[0]!["immobile"] = true);
            AssertCaught(errors, "R20");
        }

        [Test]
        public void R21_보드_크기가_코드_상수와_어긋나면_잡는다()
        {
            // `_schema` §6 에 없는 우리 규칙이다. Coord 가 보드 크기를 const 로 들고 있어서
            // economy.json 만 바꾸면 배치·이동이 조용히 틀린다.
            var errors = ErrorsAfter(f => f.Economy["board"]!["perSide"]!["cols"] = 5);
            AssertCaught(errors, "R21");
        }

        [Test]
        public void R23_제자리_보호막이_되돌릴_양을_안_가지면_잡는다()
        {
            // whileStationary 는 이동할 때 자기가 준 만큼을 거둔다. 그 양이 maxPermille 이라
            // 없으면 0 을 거두게 되고, 한 번 켜지면 영영 안 꺼진다 — 조건부인데 상시가 된다.
            var errors = ErrorsAfter(f => FindStationaryShield(f.Skills).Remove("maxPermille"));
            AssertCaught(errors, "R23");
        }

        [Test]
        public void R23_제자리_보호막을_남에게_걸면_잡는다()
        {
            // 남의 이동은 이 유닛이 관측할 수 없다. 켜지기만 하고 꺼지지 않는다.
            var errors = ErrorsAfter(f => FindStationaryShield(f.Skills)["target"] = "allies");
            AssertCaught(errors, "R23");
        }

        [Test]
        public void R23_두_지속_방식을_같이_쓰면_잡는다()
        {
            // 켜고 끄는 것과 주기로 채우는 것은 리듬이 다르다. 섞으면 어느 쪽이 이겼는지가
            // 코드 순서에 달리고, 그건 데이터를 읽어서는 알 수 없는 규칙이 된다.
            var errors = ErrorsAfter(f => FindStationaryShield(f.Skills)["refreshEveryTicks"] = 200);
            AssertCaught(errors, "R23");
        }

        [Test]
        public void R23_재충전_주기가_0_이면_잡는다()
        {
            // 0 은 "주기 없음"이 아니라 매 틱 상한까지 재충전이라 사실상 무적이다.
            var errors = ErrorsAfter(f =>
                FindEffect(f.Skills, o => o["refreshEveryTicks"] != null)["refreshEveryTicks"] = 0);
            AssertCaught(errors, "R23");
        }

        [Test]
        public void 위반이_여러_건이면_한_번에_전부_보고한다()
        {
            // 최적화기가 값을 써넣는 파일들이라, 한 번에 하나씩 알려주면 CI 를 그만큼 반복하게 된다.
            var errors = ErrorsAfter(f =>
            {
                ((JArray)f.Skills["skills"]!)[0]!["role"] = "만능딜러";              // R06
                FirstUnit(f.Encounters)["type"] = "dragon";                          // R13
                ((JArray)f.Characters["characters"]!)[0]!["moveInterval"] = 0;       // R19
            });

            Assert.That(errors.Select(e => e.Rule).Distinct().Count(), Is.GreaterThanOrEqualTo(3));
        }

        // ────────────────────────────── 도구

        private sealed class Files
        {
            public JObject Characters = null!;
            public JObject Skills = null!;
            public JObject Encounters = null!;
            public JObject Economy = null!;
            public JObject Meta = null!;
        }

        private static GameData LoadReal() =>
            GameDataLoader.Load(RepoData.Characters, RepoData.Skills,
                                RepoData.Encounters, RepoData.Economy, RepoData.Meta);

        /// <summary>실제 데이터를 한 군데만 망가뜨린 뒤 나온 오류 목록.</summary>
        private static IReadOnlyList<ValidationError> ErrorsAfter(Action<Files> breakIt)
        {
            var f = new Files
            {
                Characters = RepoData.Json("characters.json"),
                Skills = RepoData.Json("skills.json"),
                Encounters = RepoData.Json("encounters.json"),
                Economy = RepoData.Json("economy.json"),
                Meta = RepoData.Json("meta.json"),
            };

            breakIt(f);

            var ex = Assert.Throws<DataValidationException>(() =>
                GameDataLoader.Load(f.Characters.ToString(), f.Skills.ToString(),
                                    f.Encounters.ToString(), f.Economy.ToString(), f.Meta.ToString()));

            return ex!.Errors;
        }

        private static void AssertCaught(IReadOnlyList<ValidationError> errors, string rule)
        {
            Assert.That(errors.Any(e => e.Rule == rule), Is.True,
                $"{rule} 이 잡히지 않았다. 실제 오류: {string.Join(" / ", errors.Select(e => e.ToString()))}");
        }

        private static JObject FirstUnit(JObject encounters) =>
            (JObject)((JArray)((JArray)((JArray)encounters["rounds"]!)[0]!["variants"]!)[0]!["units"]!)[0]!;

        private static JObject FindEffectWithShield(JObject skills) =>
            FindEffect(skills, o => (string?)o["kind"] == "shield" && o["overflowToHp"] != null);

        private static JObject FindStationaryShield(JObject skills) =>
            FindEffect(skills, o => (bool?)o["whileStationary"] == true);

        private static JObject FindHealEffect(JObject skills) =>
            FindEffect(skills, o => (string?)o["template"] == "heal");

        private static JObject FindTriggerEffect(JObject skills) =>
            FindEffect(skills, o => o["trigger"] is JObject);

        private static JObject FindStatusEffect(JObject skills) =>
            FindEffect(skills, o => (string?)o["template"] == "status");

        private static JObject FindTargetingEffect(JObject skills) =>
            FindEffect(skills, o => (string?)o["template"] == "targeting" && o["priority"] != null);

        /// <summary>조건에 맞는 효과 객체를 스킬 트리 전체에서 찾는다. 중첩된 effect 까지 내려간다.</summary>
        private static JObject FindEffect(JObject skills, Func<JObject, bool> match)
        {
            foreach (string key in new[] { "skills", "supportSkills" })
            {
                foreach (var skill in (JArray)skills[key]!)
                {
                    foreach (var eff in (JArray)skill["effects"]!)
                    {
                        var hit = Descend((JObject)eff, match);
                        if (hit != null) return hit;
                    }
                }
            }

            throw new InvalidOperationException("조건에 맞는 효과가 데이터에 없다 — 테스트 전제가 바뀌었다");
        }

        private static JObject? Descend(JObject o, Func<JObject, bool> match)
        {
            if (match(o)) return o;
            if (o["effect"] is JObject nested) return Descend(nested, match);
            return null;
        }
    }
}
