// Unity 는 Directory.Build.props 를 읽지 않는다 — asmdef 컴파일 경로가 다르다.
// 그래서 nullable 문맥이 dotnet 쪽에만 켜지고 Unity 쪽에는 꺼진 채로 갈라졌고,
// 실제로 WebGL 빌드에서 CS8632 로 드러났다. 파일마다 명시해 두 컴파일러를 맞춘다.
#nullable enable

using System;

namespace DomoNinja.Core.Rng
{
    /// <summary>
    /// 시드 주입 PRNG. <b><see cref="System.Random"/> 을 쓰지 않는다.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="System.Random"/> 을 금지하는 이유는 "느려서"가 아니다.
    /// 구현이 런타임 버전에 따라 바뀐 전례가 있고(.NET Core 3.0 에서 실제로 바뀌었다),
    /// Unity(Mono/IL2CPP)와 dotnet 이 같은 수열을 준다는 보장이 없다.
    /// 시뮬과 게임이 같은 시드로 다른 결과를 내면 밸런스 수치가 전부 무의미해진다.
    /// 그래서 알고리즘을 저장소 안에 고정한다 — xoshiro256** + SplitMix64 시딩.
    /// </para>
    /// <para>
    /// ★ 스트림 분리가 이 타입의 두 번째 목적이다.
    /// 전투·상점·관문 추첨이 같은 난수열을 나눠 쓰면, 전투에서 난수 소비 횟수가
    /// 한 번만 달라져도 그 뒤 상점 결과가 통째로 밀린다. 그러면 파라미터를 하나
    /// 바꿨을 때 관측된 차이가 그 파라미터 때문인지 난수 정렬이 밀려서인지 구분할 수 없다.
    /// CRN(공통 난수, 17 §6)으로 분산을 줄이려는 설계가 여기서 무너진다.
    /// <see cref="Fork"/> 로 용도별 독립 스트림을 만들어 쓴다.
    /// </para>
    /// </remarks>
    public sealed class DeterministicRandom
    {
        private ulong _s0, _s1, _s2, _s3;

        /// <summary>이 스트림이 지금까지 소비한 난수 개수. 결정론 회귀 테스트에 쓴다.</summary>
        public long DrawCount { get; private set; }

        public DeterministicRandom(ulong seed)
        {
            // SplitMix64 로 상태 4개를 펼친다. 시드가 0 이어도 상태가 0 이 되지 않는다.
            ulong z = seed;
            _s0 = SplitMix64(ref z);
            _s1 = SplitMix64(ref z);
            _s2 = SplitMix64(ref z);
            _s3 = SplitMix64(ref z);
        }

        /// <summary>
        /// 같은 시드에서 갈라져 나온 <b>독립 스트림</b>을 만든다.
        /// </summary>
        /// <param name="streamId">용도 식별자. 같은 값이면 항상 같은 스트림이 나온다.</param>
        /// <remarks>
        /// 부모 스트림의 상태를 소비하지 않는다 — 부모의 수열은 자식을 몇 개 만들든 그대로다.
        /// 이래야 "전투 스트림만 바꿔서 다시 돌린다" 같은 조작이 가능해진다.
        /// </remarks>
        public DeterministicRandom Fork(ulong streamId)
        {
            ulong mixed = _s0 ^ (streamId * 0x9E3779B97F4A7C15UL);
            return new DeterministicRandom(mixed);
        }

        public ulong NextUInt64()
        {
            ulong result = Rotl(_s1 * 5UL, 7) * 9UL;
            ulong t = _s1 << 17;

            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = Rotl(_s3, 45);

            DrawCount++;
            return result;
        }

        /// <summary>[0, maxExclusive) 범위의 정수. <b>편향 없음.</b></summary>
        /// <remarks>
        /// 단순 나머지 연산(% max)은 범위가 2의 거듭제곱이 아닐 때 작은 값에 미세하게 치우친다.
        /// 상점 추첨을 수만 번 돌려 채택률(M3b/M3c)을 재는 설계라
        /// 그 편향이 지표에 그대로 실린다. 기각 표본법으로 없앤다.
        /// </remarks>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "1 이상이어야 한다.");

            ulong range = (ulong)maxExclusive;
            ulong limit = ulong.MaxValue - (ulong.MaxValue % range) - 1UL;

            ulong x;
            do
            {
                x = NextUInt64();
            } while (x > limit);

            return (int)(x % range);
        }

        /// <summary>[minInclusive, maxExclusive) 범위의 정수.</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "min 보다 커야 한다.");

            return minInclusive + NextInt(maxExclusive - minInclusive);
        }

        /// <summary>천분율 확률 판정. 확률을 float 로 다루지 않기 위한 것이다.</summary>
        /// <param name="permille">0~1000</param>
        public bool Chance(int permille) => NextInt(1000) < permille;

        /// <summary>
        /// Fisher-Yates 셔플. <b>리스트 순회 순서에 의존하지 않는 유일한 섞기 방법이다.</b>
        /// </summary>
        public void Shuffle<T>(T[] items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            for (int i = items.Length - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }

        /// <summary>
        /// 현재 내부 상태의 지문. 결정론 회귀 테스트가 이 값을 비교한다.
        /// </summary>
        /// <remarks>
        /// 값 자체에 의미는 없다. "같은 시드로 같은 조작을 하면 같은 값이 나온다"만 보장하면 된다.
        /// </remarks>
        public ulong StateHash()
        {
            ulong h = 1469598103934665603UL;
            foreach (ulong v in new[] { _s0, _s1, _s2, _s3, (ulong)DrawCount })
            {
                h ^= v;
                h *= 1099511628211UL;
            }
            return h;
        }

        private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

        private static ulong SplitMix64(ref ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            ulong r = z;
            r = (r ^ (r >> 30)) * 0xBF58476D1CE4E5B9UL;
            r = (r ^ (r >> 27)) * 0x94D049BB133111EBUL;
            return r ^ (r >> 31);
        }
    }

    /// <summary>
    /// 난수 스트림 용도 식별자. <see cref="DeterministicRandom.Fork"/> 에 넘긴다.
    /// </summary>
    /// <remarks>
    /// 숫자를 직접 쓰지 않고 상수로 두는 이유 — 두 용도가 같은 값을 쓰면
    /// 스트림이 겹치는데, 그건 컴파일러가 못 잡고 지표가 조용히 오염된다.
    /// </remarks>
    public static class RngStream
    {
        public const ulong Combat = 1;
        public const ulong Shop = 2;
        public const ulong Encounter = 3;
        public const ulong Reward = 4;
    }
}
