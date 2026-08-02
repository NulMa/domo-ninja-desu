using System.Collections.Generic;
using System.Linq;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Events;
using DomoNinja.Core.Skills;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace DomoNinja.Core.Tests
{
    /// <summary>트리거 효과 실행 (`_schema` §3 원자 동작·대상 지정).</summary>
    [TestFixture]
    public class EffectExecutorTests
    {
        private ListEventSink _sink = null!;
        private List<Unit> _team = null!;
        private List<Unit> _foes = null!;

        [SetUp]
        public void SetUp()
        {
            _sink = new ListEventSink();
            _team = new List<Unit>
            {
                U(0, Team.Ally, 200),
                U(1, Team.Ally, 100),
                U(2, Team.Ally, 80),
            };
            _foes = new List<Unit> { U(5, Team.Enemy, 120), U(6, Team.Enemy, 60) };
        }

        private static Unit U(int id, Team team, int maxHp) =>
            new Unit(id, team, team == Team.Ally ? "C1" : "slime", maxHp,
                     attack: 10, attackInterval: 20, range: 1, moveInterval: 5,
                     at: new Coord(id % 8, 0));

        private EffectContext Ctx(int selfIndex = 0, int lastDamage = 0, Unit? target = null) =>
            new EffectContext(_team[selfIndex], target ?? _foes[0], lastDamage, 100, _team, _foes, _sink);

        private static JObject E(string json) => JObject.Parse(json);

        // ────────────────────────────── heal

        [Test]
        public void permille_은_대상_최대체력의_천분율이다()
        {
            _team[1].Hp = 50;
            EffectExecutor.Execute(
                E(@"{ ""template"": ""heal"", ""target"": ""allies"", ""permille"": 60 }"),
                Ctx(), 1000);

            Assert.That(_team[1].Hp, Is.EqualTo(56), "100 의 6% = 6");
            Assert.That(_team[0].Hp, Is.EqualTo(200), "만피는 그대로");
        }

        [Test]
        public void fromDamagePermille_은_방금_발생한_피해의_천분율이다()
        {
            // C1-B 연격 흡혈 — 가한 피해의 15%.
            _team[0].Hp = 100;
            EffectExecutor.Execute(
                E(@"{ ""template"": ""heal"", ""target"": ""self"", ""fromDamagePermille"": 150 }"),
                Ctx(lastDamage: 40), 1000);

            Assert.That(_team[0].Hp, Is.EqualTo(106), "40 의 15% = 6");
        }

        [Test]
        public void lowestHpAlly_는_절대값이_아니라_비율로_고른다()
        {
            _team[0].Hp = 90;    // 200 중 45%
            _team[1].Hp = 60;    // 100 중 60%
            _team[2].Hp = 60;    // 80 중 75%

            EffectExecutor.Execute(
                E(@"{ ""template"": ""heal"", ""target"": ""lowestHpAlly"", ""permille"": 100 }"),
                Ctx(), 1000);

            Assert.That(_team[0].Hp, Is.EqualTo(110), "절대값으로는 90 이 가장 크지만 비율로는 최저다");
            Assert.That(_team[1].Hp, Is.EqualTo(60));
        }

        [Test]
        public void 죽은_아군은_회복_대상에서_빠진다()
        {
            // A6 — 부활 없음. 광역 회복이 시체를 일으키면 안 된다.
            _team[1].Hp = 0;
            EffectExecutor.Execute(
                E(@"{ ""template"": ""heal"", ""target"": ""allies"", ""permille"": 500 }"),
                Ctx(), 1000);

            Assert.That(_team[1].IsAlive, Is.False);
        }

        [Test]
        public void skillPower_는_회복량을_키운다()
        {
            _team[1].Hp = 10;
            EffectExecutor.Execute(
                E(@"{ ""template"": ""heal"", ""target"": ""allies"", ""permille"": 100 }"),
                Ctx(), skillPowerPermille: 1500);

            Assert.That(_team[1].Hp, Is.EqualTo(25), "100 의 10% x 1.5 = 15");
        }

        // ────────────────────────────── self_damage

        [Test]
        public void self_damage_는_자신의_최대체력_비율을_태운다()
        {
            // C2-B 파동 — 발동마다 자신의 체력 8% 소모.
            EffectExecutor.Execute(
                E(@"{ ""template"": ""self_damage"", ""value"": 0.08 }"), Ctx(), 1000);

            Assert.That(_team[0].Hp, Is.EqualTo(184), "200 의 8% = 16");
        }

        [Test]
        public void self_damage_는_보호막으로_막히지_않는다()
        {
            // 자기 체력을 태우는 게 이 효과의 값이다. 방어 수단으로 막히면 대가가 사라진다.
            _team[0].Shield = 100;
            EffectExecutor.Execute(
                E(@"{ ""template"": ""self_damage"", ""value"": 0.08 }"), Ctx(), 1000);

            Assert.That(_team[0].Shield, Is.EqualTo(100));
            Assert.That(_team[0].Hp, Is.EqualTo(184));
        }

        [Test]
        public void self_damage_는_skillPower_를_타지_않는다()
        {
            // ★ 대가다. 타게 하면 보조의 skillPower 가 1 미만일 때 부호가 뒤집힌다
            //   — 위력을 깎는 대가가 자기 체력 소모까지 줄여주는 이득이 된다.
            EffectExecutor.Execute(
                E(@"{ ""template"": ""self_damage"", ""value"": 0.08 }"), Ctx(), skillPowerPermille: 1500);

            Assert.That(_team[0].Hp, Is.EqualTo(184), "1.5배였다면 176 이 된다");
        }

        // ────────────────────────────── status

        [Test]
        public void 적_대상_상태이상은_현재_표적에게만_걸린다()
        {
            // C3-B 표창 — 적중한 적을 둔화.
            EffectExecutor.Execute(
                E(@"{ ""template"": ""status"", ""target"": ""enemy"", ""kind"": ""slow"",
                      ""moveIntervalMult"": 1.35, ""duration"": 60 }"),
                Ctx(target: _foes[1]), 1000);

            Assert.That(_foes[1].Status.Has(StatusKind.Slow), Is.True);
            Assert.That(_foes[0].Status.Has(StatusKind.Slow), Is.False);
            Assert.That(_foes[1].Status.MoveIntervalDeltaPermille, Is.EqualTo(350));
        }

        [Test]
        public void all_enemies_는_적_전체에_걸린다()
        {
            // C6-A 주술.
            EffectExecutor.Execute(
                E(@"{ ""template"": ""status"", ""target"": ""all_enemies"", ""kind"": ""weaken"",
                      ""attackMult"": 0.85, ""damageTakenMult"": 1.15 }"),
                Ctx(), 1000);

            Assert.That(_foes.All(f => f.Status.Has(StatusKind.Weaken)), Is.True);
            Assert.That(_foes[0].Status.AttackDeltaPermille, Is.EqualTo(-150));
            Assert.That(_foes[0].Status.DamageTakenDeltaPermille, Is.EqualTo(150));
        }

        [Test]
        public void allies_except_self_는_자신을_뺀다()
        {
            // C6-B 가호 — 자신을 제외한 아군이 매 초 회복.
            EffectExecutor.Execute(
                E(@"{ ""template"": ""status"", ""target"": ""allies_except_self"", ""kind"": ""regen"",
                      ""healPermille"": 30, ""everyTicks"": 20 }"),
                Ctx(selfIndex: 0), 1000);

            Assert.That(_team[0].Status.Has(StatusKind.Regen), Is.False);
            Assert.That(_team[1].Status.Has(StatusKind.Regen), Is.True);
            Assert.That(_team[2].Status.Has(StatusKind.Regen), Is.True);
        }

        [Test]
        public void duration_이_있으면_만료_틱이_절대값으로_박힌다()
        {
            EffectExecutor.Execute(
                E(@"{ ""template"": ""status"", ""target"": ""enemy"", ""kind"": ""slow"",
                      ""moveIntervalMult"": 1.35, ""duration"": 60 }"),
                Ctx(target: _foes[0]), 1000);

            _foes[0].Status.TryGet(StatusKind.Slow, out var slow);
            Assert.That(slow.ExpireTick, Is.EqualTo(160), "현재 틱 100 + 60");
        }

        [Test]
        public void duration_이_없으면_전투_내내_유지된다()
        {
            EffectExecutor.Execute(
                E(@"{ ""template"": ""status"", ""target"": ""self"", ""kind"": ""taunt"",
                      ""threatMult"": 3.0 }"),
                Ctx(), 1000);

            _team[0].Status.TryGet(StatusKind.Taunt, out var taunt);
            Assert.That(taunt.ExpireTick, Is.EqualTo(StatusEffect.Never));
            Assert.That(_team[0].Status.ThreatPermille, Is.EqualTo(3000));
        }

        [Test]
        public void 보호막은_전용_경로로_가서_상한과_초과분을_처리한다()
        {
            // C3-A 그림자 — 회피 시 보호막, 가득 차면 체력으로.
            _team[1].Hp = 50;
            EffectExecutor.Execute(
                E(@"{ ""template"": ""status"", ""target"": ""allies"", ""kind"": ""shield"",
                      ""gainPermille"": 800, ""maxPermille"": 350, ""overflowToHp"": true }"),
                Ctx(), 1000);

            // 100 의 80% = 80 획득, 상한은 35% = 35 → 초과 45 가 체력으로
            Assert.That(_team[1].Shield, Is.EqualTo(35));
            Assert.That(_team[1].Hp, Is.EqualTo(95));
        }

        // ────────────────────────────── 추가 행동

        [Test]
        public void extra_attack_과_recast_는_실행하지_않고_요청만_돌려준다()
        {
            // ★ 여기서 직접 공격을 부르면 재귀 깊이가 데이터에 따라 정해지고
            //   어디서 멈추는지가 코드에 안 보인다. 상한은 전투 루프 한 군데에 둔다.
            var extra = EffectExecutor.Execute(
                E(@"{ ""template"": ""extra_attack"", ""count"": 1 }"), Ctx(), 1000);
            var recast = EffectExecutor.Execute(
                E(@"{ ""template"": ""recast"", ""maxChain"": 3 }"), Ctx(), 1000);

            Assert.That(extra.ExtraAttacks, Is.EqualTo(1));
            Assert.That(recast.RecastChain, Is.EqualTo(3));
            Assert.That(_sink.Events, Is.Empty, "요청만 돌려주지 아무 일도 일으키지 않는다");
        }

        // ────────────────────────────── 실제 데이터 대조

        [Test]
        public void 실제_데이터의_중첩_효과가_전부_구현되어_있다()
        {
            // ★ 모르는 효과를 조용히 넘기면 그 스킬은 아무 일도 안 하면서
            //   밸런스 지표에는 잡힌다 — 시뮬 결과 전체가 조용히 오염된다.
            var data = GameDataLoader.Load(RepoData.Characters, RepoData.Skills,
                                           RepoData.Encounters, RepoData.Economy, RepoData.Meta);

            var missing = new List<string>();
            foreach (var skill in data.Skills.Concat(data.SupportSkills))
            {
                foreach (var token in skill.Effects)
                {
                    if (!(token is JObject e)) continue;
                    if ((string?)e["template"] != "conditional") continue;
                    if (!(e["effect"] is JObject nested)) continue;

                    string? template = (string?)nested["template"];
                    if (!EffectExecutor.IsSupported(template))
                        missing.Add($"{skill.Id}:{template}");
                }
            }

            Assert.That(missing, Is.Empty,
                "구현되지 않은 효과가 데이터에 있다: " + string.Join(", ", missing));
        }

        [Test]
        public void 실제_데이터의_모든_대상_지정이_풀린다()
        {
            var data = GameDataLoader.Load(RepoData.Characters, RepoData.Skills,
                                           RepoData.Encounters, RepoData.Economy, RepoData.Meta);

            var unresolved = new List<string>();
            foreach (var skill in data.Skills.Concat(data.SupportSkills))
            {
                foreach (var token in skill.Effects)
                {
                    if (!(token is JObject e)) continue;

                    // 중첩된 것까지 훑는다.
                    for (JObject? cur = e; cur != null; cur = cur["effect"] as JObject)
                    {
                        string? target = (string?)cur["target"];
                        if (target == null || target == "mainSkill") continue;   // 가상 대상

                        if (EffectExecutor.Resolve(target, Ctx()).Count == 0
                            && target != "enemy")
                        {
                            unresolved.Add($"{skill.Id}:{target}");
                        }
                    }
                }
            }

            Assert.That(unresolved, Is.Empty,
                "풀리지 않는 대상 지정: " + string.Join(", ", unresolved));
        }
    }
}
