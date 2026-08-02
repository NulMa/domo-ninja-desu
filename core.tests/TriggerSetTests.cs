using System.Collections.Generic;
using System.Linq;
using DomoNinja.Core.Data;
using DomoNinja.Core.Skills;
using NUnit.Framework;

namespace DomoNinja.Core.Tests
{
    /// <summary>트리거 발동 조건 (`_schema` §3 <c>conditional</c>).</summary>
    [TestFixture]
    public class TriggerSetTests
    {
        private static GameData _data = null!;

        [OneTimeSetUp]
        public void Load() => _data = GameDataLoader.Load(
            RepoData.Characters, RepoData.Skills, RepoData.Encounters, RepoData.Economy, RepoData.Meta);

        private static SkillDef Sk(string id) => _data.FindSkill(id)!;
        private static List<SkillDef> Supports(params string[] ids) => ids.Select(Sk).ToList();

        [Test]
        public void 트리거가_없는_스킬은_빈_집합이다()
        {
            // C4-B 난사 — aoe 와 stat_mult 뿐이다.
            Assert.That(TriggerSet.Compile(Sk("C4-B")).Count, Is.EqualTo(0));
        }

        [Test]
        public void 액티브의_트리거를_뽑는다()
        {
            // C1-A 일격 — on_kill 로 재공격.
            var set = TriggerSet.Compile(Sk("C1-A"));

            Assert.That(set.Matching(TriggerType.OnKill).Count, Is.EqualTo(1));
            Assert.That(set.Matching(TriggerType.OnHit), Is.Empty);
        }

        [Test]
        public void 액티브와_보조의_트리거가_같이_들어온다()
        {
            // C1-B 연격(on_hit 흡혈) + C1-P2 불굴(on_damaged 보호막)
            var set = TriggerSet.Compile(Sk("C1-B"), Supports("C1-P2"));

            Assert.That(set.Matching(TriggerType.OnHit).Count, Is.EqualTo(1));
            Assert.That(set.Matching(TriggerType.OnDamaged).Count, Is.EqualTo(1));
        }

        [Test]
        public void 같은_트리거가_둘이면_둘_다_터진다()
        {
            // C5-B 연쇄 — on_kill 이 두 개다(재발동 + 아군 회복).
            Assert.That(TriggerSet.Compile(Sk("C5-B")).Matching(TriggerType.OnKill).Count, Is.EqualTo(2));
        }

        [Test]
        public void 메인의_트리거만_skillPower_를_들고_온다()
        {
            // 보조 자신의 트리거는 배율을 타지 않는다 — 자기가 자기를 키우면 제곱이 된다.
            var set = TriggerSet.Compile(Sk("C1-B"), Supports("C1-P1", "C1-P2"));   // C1-P1 = skillPower 1.5

            var fromMain = set.Matching(TriggerType.OnHit).Single();      // C1-B
            var fromSupport = set.Matching(TriggerType.OnDamaged).Single(); // C1-P2

            Assert.That(fromMain.SkillPowerPermille, Is.EqualTo(1500));
            Assert.That(fromSupport.SkillPowerPermille, Is.EqualTo(1000));
        }

        // ────────────────────────────── 주기

        [Test]
        public void 주기_트리거는_0틱이_아니라_N틱에_처음_터진다()
        {
            // C2-B 파동 — every_n_tick 60 (3초). 전투 시작하자마자 터지면 안 된다.
            var set = TriggerSet.Compile(Sk("C2-B"));
            var fired = new List<CompiledTrigger>();

            set.CollectPeriodic(0, fired);
            Assert.That(fired, Is.Empty);

            set.CollectPeriodic(59, fired);
            Assert.That(fired, Is.Empty);

            set.CollectPeriodic(60, fired);
            Assert.That(fired.Count, Is.EqualTo(1));
        }

        [Test]
        public void 주기_트리거는_일정_간격으로_계속_터진다()
        {
            var set = TriggerSet.Compile(Sk("C2-B"));   // 60틱 주기
            var fired = new List<CompiledTrigger>();

            int count = 0;
            for (int tick = 0; tick <= 300; tick++)
            {
                fired.Clear();
                set.CollectPeriodic(tick, fired);
                count += fired.Count;
            }

            Assert.That(count, Is.EqualTo(5), "60·120·180·240·300 다섯 번");
        }

        [Test]
        public void 틱을_건너뛰어도_주기가_밀리지_않는다()
        {
            // 절대 틱을 누적하지 않고 NextFireTick 을 미는 이유.
            // 전투가 한 틱에 여러 사건을 처리해도 주기가 어긋나면 안 된다.
            var set = TriggerSet.Compile(Sk("C2-B"));
            var fired = new List<CompiledTrigger>();

            set.CollectPeriodic(65, fired);    // 60 을 지나쳐서 들어왔다
            Assert.That(fired.Count, Is.EqualTo(1));

            fired.Clear();
            set.CollectPeriodic(119, fired);
            Assert.That(fired, Is.Empty, "다음은 120 이지 125 가 아니다");

            set.CollectPeriodic(120, fired);
            Assert.That(fired.Count, Is.EqualTo(1));
        }

        // ────────────────────────────── HP 임계

        [Test]
        public void HP_임계는_내려간_순간_한_번만_터진다()
        {
            // ⚠️ 현재 스킬 데이터에 hp_below 가 없다. 이 해석("경계 통과 시 1회")은
            //    아직 실사용으로 검증된 게 아니다 — 쓰는 스킬이 생기면 다시 본다.
            var set = TriggerSet.Compile(FakeHpBelow(0.5));
            var fired = new List<CompiledTrigger>();

            set.CollectHpBelow(600, fired);
            Assert.That(fired, Is.Empty, "아직 임계 위다");

            set.CollectHpBelow(400, fired);
            Assert.That(fired.Count, Is.EqualTo(1));

            fired.Clear();
            set.CollectHpBelow(300, fired);
            Assert.That(fired, Is.Empty, "계속 아래에 있다고 매 틱 터지면 안 된다");
        }

        [Test]
        public void HP_가_임계_위로_돌아오면_다시_장전된다()
        {
            var set = TriggerSet.Compile(FakeHpBelow(0.5));
            var fired = new List<CompiledTrigger>();

            set.CollectHpBelow(400, fired);
            fired.Clear();

            set.CollectHpBelow(700, fired);   // 회복했다
            Assert.That(fired, Is.Empty);

            set.CollectHpBelow(400, fired);   // 다시 내려갔다
            Assert.That(fired.Count, Is.EqualTo(1));
        }

        // ────────────────────────────── 실제 데이터

        [Test]
        public void 실제_데이터의_트리거가_전부_인식된다()
        {
            // 알 수 없는 트리거는 조용히 버려지므로, 개수로 확인해야 빠진 걸 안다.
            // 검증 규칙 R11 이 오타를 막지만, 그건 "목록 안에 있다"까지고
            // 컴파일러가 실제로 집어 드는지는 별개다.
            int compiled = _data.Skills.Concat(_data.SupportSkills)
                .Sum(s => TriggerSet.Compile(s).Count);

            // skills.json 의 conditional 개수와 같아야 한다.
            int declared = _data.Skills.Concat(_data.SupportSkills)
                .Sum(s => s.Effects.Count(t => (string?)t["template"] == "conditional"));

            Assert.That(compiled, Is.EqualTo(declared),
                "conditional 이 있는데 트리거로 안 잡힌 게 있다 — 알 수 없는 trigger.type 이 조용히 버려졌다");
            Assert.That(declared, Is.EqualTo(13), "현재 데이터 기준");
        }

        /// <summary>`hp_below` 를 쓰는 스킬이 아직 없어서 테스트용으로 만든다.</summary>
        private static SkillDef FakeHpBelow(double ratio)
        {
            var effects = Newtonsoft.Json.Linq.JArray.Parse($@"[
                {{ ""template"": ""conditional"",
                   ""trigger"": {{ ""type"": ""hp_below"", ""value"": {ratio} }},
                   ""effect"": {{ ""template"": ""stat_mult"", ""target"": ""self"",
                                 ""stat"": ""attack"", ""value"": 1.6 }} }}
            ]");

            return new SkillDef("TEST-A", "C1", "테스트", "브루저", null, "gain", "cost",
                                effects, new string[0]);
        }
    }
}
