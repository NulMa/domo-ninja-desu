using System.Collections.Generic;
using DomoNinja.Core.Rng;
using NUnit.Framework;

namespace DomoNinja.Core.Tests
{
    /// <summary>
    /// 결정론 회귀. <b>이 파일이 깨지면 밸런스 수치 전부가 무효다.</b>
    /// </summary>
    /// <remarks>
    /// 밸런스 검증은 "sim 이 낸 결과 == 실제 게임 결과" 위에 서 있다.
    /// 그 전제를 지키는 것이 여기 있는 테스트들이고, 그래서 P0 에 넣었다.
    /// 전투 로직보다 먼저 있어야 하는 테스트다.
    /// </remarks>
    [TestFixture]
    public class DeterminismTests
    {
        private const ulong Seed = 20260802UL;

        [Test]
        public void 같은_시드로_두_번_돌리면_수열이_같다()
        {
            var a = new DeterministicRandom(Seed);
            var b = new DeterministicRandom(Seed);

            for (int i = 0; i < 10_000; i++)
            {
                Assert.That(b.NextUInt64(), Is.EqualTo(a.NextUInt64()),
                    $"{i}번째 난수에서 갈라졌다.");
            }

            Assert.That(b.StateHash(), Is.EqualTo(a.StateHash()));
        }

        [Test]
        public void 시드가_다르면_수열이_다르다()
        {
            var a = new DeterministicRandom(Seed);
            var b = new DeterministicRandom(Seed + 1);

            Assert.That(b.NextUInt64(), Is.Not.EqualTo(a.NextUInt64()));
        }

        [Test]
        public void 시드가_0이어도_상태가_죽지_않는다()
        {
            // SplitMix64 시딩을 쓰는 이유. 단순 대입이면 0 시드에서 상태가 0 이 되어
            // 이후 전부 0 이 나온다. 시드는 외부(CI 파라미터·URL)에서 들어올 수 있다.
            var r = new DeterministicRandom(0UL);

            var seen = new HashSet<ulong>();
            for (int i = 0; i < 100; i++) seen.Add(r.NextUInt64());

            Assert.That(seen.Count, Is.GreaterThan(90), "0 시드에서 수열이 죽었다.");
        }

        [Test]
        public void Fork_는_부모_수열을_소비하지_않는다()
        {
            // "전투 스트림만 바꿔 다시 돌린다" 를 가능하게 하는 성질이다.
            var parentA = new DeterministicRandom(Seed);
            var parentB = new DeterministicRandom(Seed);

            parentB.Fork(RngStream.Shop);
            parentB.Fork(RngStream.Encounter);

            Assert.That(parentB.NextUInt64(), Is.EqualTo(parentA.NextUInt64()));
        }

        [Test]
        public void 서로_다른_스트림은_독립이다()
        {
            var root = new DeterministicRandom(Seed);
            var combat = root.Fork(RngStream.Combat);
            var shop = root.Fork(RngStream.Shop);

            bool diverged = false;
            for (int i = 0; i < 100 && !diverged; i++)
            {
                if (combat.NextUInt64() != shop.NextUInt64()) diverged = true;
            }

            Assert.That(diverged, Is.True, "두 스트림이 같은 수열을 준다 — 스트림 분리가 안 되고 있다.");
        }

        [Test]
        public void 같은_스트림_식별자는_항상_같은_스트림을_준다()
        {
            var a = new DeterministicRandom(Seed).Fork(RngStream.Combat);
            var b = new DeterministicRandom(Seed).Fork(RngStream.Combat);

            Assert.That(b.NextUInt64(), Is.EqualTo(a.NextUInt64()));
        }

        [Test]
        public void NextInt_는_범위를_벗어나지_않는다()
        {
            var r = new DeterministicRandom(Seed);

            for (int i = 0; i < 50_000; i++)
            {
                int v = r.NextInt(7);
                Assert.That(v, Is.InRange(0, 6));
            }
        }

        [Test]
        public void NextInt_는_편향되지_않는다()
        {
            // 상점 추첨을 수만 번 돌려 채택률을 재는 설계라, % 연산 편향이
            // 지표에 그대로 실린다. 기각 표본법이 실제로 동작하는지 본다.
            const int buckets = 7;
            const int draws = 700_000;

            var r = new DeterministicRandom(Seed);
            var counts = new int[buckets];

            for (int i = 0; i < draws; i++) counts[r.NextInt(buckets)]++;

            int expected = draws / buckets;
            foreach (int c in counts)
            {
                // 기대값 대비 2% 이내. 편향이 있으면 이보다 훨씬 크게 벌어진다.
                Assert.That(c, Is.InRange(expected * 98 / 100, expected * 102 / 100));
            }
        }

        [Test]
        public void Shuffle_은_원소를_잃거나_늘리지_않는다()
        {
            var r = new DeterministicRandom(Seed);
            var items = new[] { 1, 2, 3, 4, 5, 6, 7, 8 };

            r.Shuffle(items);

            Assert.That(items, Is.EquivalentTo(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        }

        [Test]
        public void Shuffle_은_같은_시드에서_같은_결과를_준다()
        {
            var a = new[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            var b = new[] { 1, 2, 3, 4, 5, 6, 7, 8 };

            new DeterministicRandom(Seed).Shuffle(a);
            new DeterministicRandom(Seed).Shuffle(b);

            Assert.That(b, Is.EqualTo(a));
        }
    }
}
