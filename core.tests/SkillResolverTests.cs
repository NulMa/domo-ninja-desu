using System.Collections.Generic;
using System.Linq;
using DomoNinja.Core.Data;
using DomoNinja.Core.Skills;
using NUnit.Framework;

namespace DomoNinja.Core.Tests
{
    /// <summary>스킬 → 전투 시작 스탯 (`08` §3 · `_schema` §3 · §8).</summary>
    [TestFixture]
    public class SkillResolverTests
    {
        private static GameData _data = null!;

        [OneTimeSetUp]
        public void Load() => _data = GameDataLoader.Load(
            RepoData.Characters, RepoData.Skills, RepoData.Encounters, RepoData.Economy, RepoData.Meta);

        private static CharacterDef Ch(string id) => _data.FindCharacter(id)!;
        private static SkillDef Sk(string id) => _data.FindSkill(id)!;
        private static List<SkillDef> Supports(params string[] ids) => ids.Select(Sk).ToList();

        [Test]
        public void 스킬이_없으면_기본_스탯_그대로다()
        {
            var c1 = Ch("C1");
            var stats = SkillResolver.BuildStats(c1, null);

            Assert.That(stats.Hp, Is.EqualTo(c1.Hp));
            Assert.That(stats.Attack, Is.EqualTo(c1.Attack));
            Assert.That(stats.Range, Is.EqualTo(c1.Range));
        }

        [Test]
        public void 액티브의_이득과_대가가_둘_다_반영된다()
        {
            // C1-A 일격 — 공격력 +50% / 공격 간격 +40%(느려진다).
            // ★ 기대값을 박지 않고 캐릭터 기본값에서 유도한다 — [BAL] 커밋이
            //   C1 스탯을 바꾸면 박아둔 숫자가 그때마다 깨진다.
            var c1 = Ch("C1");
            var stats = SkillResolver.BuildStats(c1, Sk("C1-A"));

            Assert.That(stats.Attack, Is.EqualTo(c1.Attack * 150 / 100), "이득 +50%");
            Assert.That(stats.AttackInterval, Is.EqualTo(c1.AttackInterval * 140 / 100), "대가 +40%");
        }

        [Test]
        public void setRange_는_기본_사거리를_덮어쓴다()
        {
            // C4-A 저격 — 사거리 7 지정. C4 기본 사거리는 5 다.
            var stats = SkillResolver.BuildStats(Ch("C4"), Sk("C4-A"));
            Assert.That(stats.Range, Is.EqualTo(7));

            // C3-B 표창 — 1 → 4.
            Assert.That(SkillResolver.BuildStats(Ch("C3"), Sk("C3-B")).Range, Is.EqualTo(4));
        }

        [Test]
        public void rangeBonus_는_지정된_사거리_위에_더해진다()
        {
            // 보조는 가산을 쓴다 — 메인이 사거리를 바꿔도 그 위에 얹힌다 (`_schema` §3).
            // C3-B 표창(setRange 4) + C3-P1 비도(rangeBonus +2) = 6
            var stats = SkillResolver.BuildStats(Ch("C3"), Sk("C3-B"), Supports("C3-P1"));
            Assert.That(stats.Range, Is.EqualTo(6));
        }

        // ────────────────────────────── skillPower

        [Test]
        public void 보조가_없으면_skillPower_는_1이다()
        {
            Assert.That(SkillResolver.ResolveSkillPower(null), Is.EqualTo(1000));
            Assert.That(SkillResolver.ResolveSkillPower(Supports("C1-P2")), Is.EqualTo(1000),
                "skillPower 를 안 건드리는 보조는 배율을 바꾸지 않는다");
        }

        [Test]
        public void 여러_보조의_skillPower_는_곱이_아니라_합이다()
        {
            // 곱하면 1.5 × 1.6 = 2.4 로 튄다. 보조는 캐릭터당 2개까지라
            // 곱연산이면 조합에 따라 위력이 폭발한다 (`_schema` §8).
            // C2-P1(1.6) + C2-P2(0.9) → 1 + 0.6 - 0.1 = 1.5
            Assert.That(SkillResolver.ResolveSkillPower(Supports("C2-P1", "C2-P2")), Is.EqualTo(1500));
        }

        [Test]
        public void skillPower_는_메인의_이득만_키운다()
        {
            // ★ 이 프로젝트에서 skillPower 의 의미를 결정하는 테스트다.
            //   C1-A 일격 = 공격력 +50%(이득) / 공격 간격 +40%(대가).
            //   C1-P1 극 = skillPower 1.5.
            //   이득만 1.5배 → 공격력 +75%, 간격은 +40% 그대로.
            //   ★ 기대값을 박지 않고 캐릭터 기본값에서 유도한다 ([BAL] 이 스탯을 바꾼다).
            var c1 = Ch("C1");
            var stats = SkillResolver.BuildStats(c1, Sk("C1-A"), Supports("C1-P1"));

            // 이득 +50% × 1.5 = +75%
            Assert.That(stats.Attack, Is.EqualTo(c1.Attack * 175 / 100));

            // 간격은 메인의 대가(+40%)에 보조 자신의 대가(+15%)만 더해진다 → +55%.
            // 대가까지 1.5배였다면 +60% + 15% = +75% 가 나온다 — 그 차이가 이 테스트의 요점이다.
            Assert.That(stats.AttackInterval, Is.EqualTo(c1.AttackInterval * 155 / 100));
            Assert.That(stats.AttackInterval, Is.Not.EqualTo(c1.AttackInterval * 175 / 100),
                        "대가까지 skillPower 를 탔다");
        }

        [Test]
        public void skillPower_가_1보다_작으면_메인의_이득만_깎는다()
        {
            // ★ 이게 "이득만 키운다"로 정한 근거다.
            //   대가까지 함께 줄이면 skillPower 0.85 는 메인의 페널티를 덜어주는
            //   이득이 되어, 대가로 적어둔 값이 부분적으로 버프가 된다.
            //
            //   C2-A 철벽 = 받는 피해 -40%(이득) / 공격력 -35%(대가)
            //   C2-P2 금강 = 받는 피해 -22%(이득) / skillPower 0.9(대가)
            var c2 = Ch("C2");   // attack 22
            var withSupport = SkillResolver.BuildStats(c2, Sk("C2-A"), Supports("C2-P2"));
            var withoutSupport = SkillResolver.BuildStats(c2, Sk("C2-A"));

            // 메인의 공격력 대가(-35%)는 skillPower 를 타지 않으므로 그대로 남는다.
            // 보조를 끼워도 공격력이 오르지 않아야 한다.
            Assert.That(withSupport.Attack, Is.LessThanOrEqualTo(withoutSupport.Attack),
                "대가까지 깎였다면 공격력이 올라간다 — skillPower 0.9 가 버프가 된 것이다");
        }

        [Test]
        public void 보조_자신의_효과는_skillPower_를_타지_않는다()
        {
            // 자기가 자기를 키우면 제곱이 된다.
            // C6-P1 공명 = skillPower 1.5 / 자신 공격력 -20%(보조 자신의 대가).
            var c6 = Ch("C6");   // attack 20
            var stats = SkillResolver.BuildStats(c6, null, Supports("C6-P1"));

            // 메인이 없으므로 skillPower 는 아무 데도 안 걸리고, 보조의 대가만 남는다.
            Assert.That(stats.Attack, Is.EqualTo(16));   // 20 * 0.8
        }

        // ────────────────────────────── 안전장치

        [Test]
        public void 간격은_0_밑으로_내려가지_않는다()
        {
            // 0 이면 매 틱 행동하게 되어 사실상 무한 공격이 된다.
            // 데이터 검증(R19)이 원본을 막지만 배율을 곱한 결과도 같은 함정에 빠질 수 있다.
            var stats = new UnitStats(hp: 0, attack: -5, attackInterval: 0, range: -1,
                                      moveInterval: -3, damageTakenDeltaPermille: 0);

            Assert.That(stats.Hp, Is.EqualTo(1));
            Assert.That(stats.Attack, Is.EqualTo(0));
            Assert.That(stats.AttackInterval, Is.EqualTo(1));
            Assert.That(stats.MoveInterval, Is.EqualTo(1));
            Assert.That(stats.Range, Is.EqualTo(0));
        }

        [Test]
        public void 실제_데이터의_모든_액티브가_풀린다()
        {
            // 스킬 하나하나에 대한 분기가 코드에 없다는 것을 실제 12개로 확인한다.
            // 새 스킬이 생겨도 이 코드가 바뀌지 않아야 데이터로 밸런스를 돌릴 수 있다 (D-30 전제).
            foreach (var skill in _data.Skills)
            {
                var character = _data.FindCharacter(skill.CharacterId)!;
                var stats = SkillResolver.BuildStats(character, skill);

                Assert.That(stats.Hp, Is.GreaterThan(0), $"{skill.Id} 의 체력이 0 이하다");
                Assert.That(stats.AttackInterval, Is.GreaterThan(0), $"{skill.Id} 의 공격 간격이 0 이하다");
            }
        }

        [Test]
        public void 실제_데이터의_모든_보조_조합이_풀린다()
        {
            // 캐릭터당 보조 3종 중 2개 선택 = 조합 3가지. 6명이면 18조합 x 액티브 2 = 36.
            foreach (var character in _data.Characters)
            {
                var mine = _data.SupportSkills.Where(s => s.CharacterId == character.Id).ToList();
                Assert.That(mine.Count, Is.EqualTo(3));

                for (int i = 0; i < mine.Count; i++)
                {
                    for (int j = i + 1; j < mine.Count; j++)
                    {
                        foreach (string activeId in character.SkillIds)
                        {
                            var stats = SkillResolver.BuildStats(
                                character, Sk(activeId), new List<SkillDef> { mine[i], mine[j] });

                            Assert.That(stats.Hp, Is.GreaterThan(0));
                            Assert.That(stats.MoveInterval, Is.GreaterThan(0));
                        }
                    }
                }
            }
        }
    }
}
