using System.Linq;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using DomoNinja.Core.Economy;
using DomoNinja.Core.Rng;
using NUnit.Framework;

namespace DomoNinja.Core.Tests
{
    /// <summary>빌드 공간과 구매 봇 (`08` §6.1).</summary>
    [TestFixture]
    public class BuildSpaceTests
    {
        private static GameData _data = null!;

        [OneTimeSetUp]
        public void Load() => _data = RepoData.LoadAll();

        [Test]
        public void 빌드_공간이_정확히_4320이다()
        {
            // ★ 08 §6.1 이 "4,320개 빌드를 전부 돌렸다"를 말할 수 있다는 걸
            //   D-30 채택의 결정적 근거로 삼았다. 그 숫자가 코드에서도 같아야 한다.
            //   C(6,3)=20 x 2³=8 x C(3,2)³=27 = 4,320
            Assert.That(BuildSpace.Enumerate(_data).Count(), Is.EqualTo(4320));
        }

        [Test]
        public void 보조를_2개로_고정한_것이_이_숫자를_지킨다()
        {
            // 0·1개도 허용하면 캐릭터당 7가지 → 7³ = 343 → 총 54,880 이 되어 전수 탐색이 무너진다.
            // 모든 빌드가 캐릭터당 보조 정확히 2개인지 본다.
            foreach (var build in BuildSpace.Enumerate(_data).Take(500))
                foreach (string c in build.CharacterIds)
                    Assert.That(build.SupportsByCharacter[c].Count, Is.EqualTo(2), build.Id);
        }

        [Test]
        public void 빌드는_전부_서로_다르다()
        {
            var ids = BuildSpace.Enumerate(_data).Select(b => b.Id).ToList();

            Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count));
        }

        [Test]
        public void 열거_순서가_결정적이다()
        {
            // 같은 데이터면 항상 같은 순서여야 한다 — 시뮬 결과를 빌드 인덱스로 참조하는데
            // 순서가 흔들리면 리포트의 "3번 빌드"가 실행마다 다른 걸 가리킨다.
            var a = BuildSpace.Enumerate(_data).Select(b => b.Id).Take(200).ToList();
            var b = BuildSpace.Enumerate(_data).Select(x => x.Id).Take(200).ToList();

            Assert.That(b, Is.EqualTo(a));
        }

        [Test]
        public void 모든_빌드가_실제_데이터를_가리킨다()
        {
            foreach (var build in BuildSpace.Enumerate(_data).Take(1000))
            {
                Assert.That(build.CharacterIds.Count, Is.EqualTo(3));

                foreach (string c in build.CharacterIds)
                {
                    var character = _data.FindCharacter(c);
                    Assert.That(character, Is.Not.Null);
                    Assert.That(character!.SkillIds, Does.Contain(build.ActiveByCharacter[c]));

                    foreach (string s in build.SupportsByCharacter[c])
                        Assert.That(_data.FindSkill(s)!.CharacterId, Is.EqualTo(c));
                }
            }
        }

        // ────────────────────────────── 봇

        [Test]
        public void 봇은_목표_품목만_산다()
        {
            var build = BuildSpace.Enumerate(_data).First();
            var engine = new RunEngine(_data, CombatConfig.From(_data.Economy, 20));
            var meta = new MetaProgress(_data.Meta);
            var run = engine.StartRun("S1", build.CharacterIds, meta);
            run.Currency = 200;

            var shop = new Shop(_data);
            for (int round = 1; round <= 8; round++)
                ShopBot.Visit(run, shop, build, new DeterministicRandom((ulong)round).Fork(RngStream.Shop), round);

            foreach (var entry in run.Deployed)
            {
                if (entry.ActiveSkillId != null)
                    Assert.That(entry.ActiveSkillId, Is.EqualTo(build.ActiveByCharacter[entry.CharacterId]));

                foreach (string s in entry.SupportSkillIds)
                    Assert.That(build.SupportsByCharacter[entry.CharacterId], Does.Contain(s));
            }
        }

        [Test]
        public void 봇은_아이템을_사지_않는다()
        {
            // 아이템은 빌드 공간의 축이 아니다. 사면 M4 가 아이템 효과까지 섞어 재게 된다.
            var build = BuildSpace.Enumerate(_data).First();
            var engine = new RunEngine(_data, CombatConfig.From(_data.Economy, 20));
            var meta = new MetaProgress(_data.Meta);
            var run = engine.StartRun("S1", build.CharacterIds, meta);
            run.Currency = 500;

            var shop = new Shop(_data);
            for (int round = 1; round <= 8; round++)
                ShopBot.Visit(run, shop, build, new DeterministicRandom((ulong)round).Fork(RngStream.Shop), round);

            Assert.That(run.TeamItems, Is.Empty);
            Assert.That(run.Deployed.All(e => e.Items.Count == 0), Is.True);
        }

        [Test]
        public void 봇이_사면_배타_규칙이_따라온다()
        {
            var build = BuildSpace.Enumerate(_data).First();
            var engine = new RunEngine(_data, CombatConfig.From(_data.Economy, 20));
            var meta = new MetaProgress(_data.Meta);
            var run = engine.StartRun("S1", build.CharacterIds, meta);
            run.Currency = 200;

            var shop = new Shop(_data);
            for (int round = 1; round <= 8; round++)
                ShopBot.Visit(run, shop, build, new DeterministicRandom((ulong)round).Fork(RngStream.Shop), round);

            foreach (var entry in run.Deployed)
            {
                if (entry.ActiveSkillId == null) continue;

                var character = _data.FindCharacter(entry.CharacterId)!;
                string other = character.SkillIds.First(s => s != entry.ActiveSkillId);

                Assert.That(run.RemovedSkillIds, Does.Contain(other));
            }
        }

        [Test]
        public void 재화가_없으면_아무것도_못_산다()
        {
            // "못 삼"이 곧 밸런스 신호다. 재화를 넉넉히 주면 모든 빌드가 성립해
            // M3b(선택률)가 평평해지고 배타 선택이 의미를 잃는다.
            var build = BuildSpace.Enumerate(_data).First();
            var engine = new RunEngine(_data, CombatConfig.From(_data.Economy, 20));
            var meta = new MetaProgress(_data.Meta);
            var run = engine.StartRun("S1", build.CharacterIds, meta);
            run.Currency = 0;

            ShopBot.Visit(run, new Shop(_data), build, new DeterministicRandom(1).Fork(RngStream.Shop), 1);

            Assert.That(run.Deployed.All(e => e.ActiveSkillId == null), Is.True);
            Assert.That(run.RemovedSkillIds, Is.Empty);
        }

        [Test]
        public void 봇은_리롤하지_않는다()
        {
            // 리롤은 "원하는 게 나올 때까지 재화를 태우는" 판단이고, 그게 들어가면
            // 봇이 저축 전략을 흉내 내기 시작한다. 저축은 M6 가 따로 재는 축이다.
            var build = BuildSpace.Enumerate(_data).First();
            var engine = new RunEngine(_data, CombatConfig.From(_data.Economy, 20));
            var meta = new MetaProgress(_data.Meta);
            var run = engine.StartRun("S1", build.CharacterIds, meta);
            run.Currency = 100;

            int before = run.Currency;
            ShopBot.Visit(run, new Shop(_data), build, new DeterministicRandom(1).Fork(RngStream.Shop), 1);

            // 산 것 말고 리롤 비용이 추가로 빠지지 않았는지 본다.
            // ★ 가격을 박지 않고 데이터에서 읽는다 — [BAL] 커밋이 가격을 바꾸면
            //   박아둔 숫자는 그때마다 깨지고, 사람이 갱신하는 순간 이 테스트는
            //   아무것도 안 지킨다. 실제로 첫 [BAL](5 → 6)이 이 줄을 깨뜨렸다.
            int activatePrice = (int)_data.Economy.Raw["prices"]!["skillActivate"]!;
            int spent = before - run.Currency;

            Assert.That(spent % activatePrice, Is.Zero,
                        $"1라운드에는 액티브({activatePrice})만 살 수 있다 — 쓴 돈이 그 배수가 아니다");
        }
    }
}
