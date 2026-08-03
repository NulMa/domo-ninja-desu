#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using DomoNinja.Core.Data;
using DomoNinja.Core.Economy;
using Newtonsoft.Json.Linq;

namespace DomoNinja.Sim
{
    /// <summary>
    /// 런 결과를 <b>`M1`~`M7` 지표</b>로 접는다 (`08` §6.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 지표는 <b>"게임이 재미있나"가 아니라 "설계가 의도대로 작동하나"를 잰다.</b>
    /// `M3b` 가 대표적이다 — 액티브 2택 중 한쪽이 95% 선택되면 그건 선택지가 아니라
    /// <b>함정 선택지</b>고, 배타 선택이라는 이 게임의 정체성이 가짜가 된다.
    /// </para>
    /// <para>
    /// ⚠️ <b>`M3a`·`M3b`·`M3c` 는 클리어한 런으로 가중한다.</b>
    /// 봇이 4,320 빌드를 똑같이 한 번씩 돌기 때문에, 단순 등장 횟수를 세면
    /// 모든 캐릭터가 정확히 같은 값이 나와 <b>지표가 아무것도 말하지 않는다.</b>
    /// "어떤 빌드가 실제로 통했나"를 봐야 사장 캐릭터가 드러난다.
    /// </para>
    /// </remarks>
    public static class Metrics
    {
        /// <summary>`M2` 의 HP 구간. 천분율 하한.</summary>
        private static readonly int[] HpBuckets = { 0, 200, 400, 600, 800 };

        /// <summary>
        /// `M4` 가 "상위" 로 보는 빌드 비율 (`D-71`). <b>사람이 고른 값이다.</b>
        /// </summary>
        /// <remarks>
        /// 5% 면 4,320 중 216 빌드다. 균등 분포에서 이들이 가져가는 클리어 몫이 정확히 5% 이므로
        /// 스펙의 <b>"35% 미만"</b> 은 <b>제 몫의 7배까지 허용</b>한다는 뜻이 된다.
        /// 더 좁히면(1%) 표본이 얇아 시드 잡음에 흔들리고, 넓히면(20%) 독식을 못 잡는다.
        /// </remarks>
        private const double TopFraction = 0.05;

        public sealed class Sample
        {
            public BuildTarget Build = null!;
            public RunSummary Summary = null!;
        }

        public static JObject Compute(GameData data, IReadOnlyList<Sample> samples, int tickRate)
        {
            if (samples.Count == 0) return new JObject();

            return new JObject
            {
                ["M1"] = M1(samples),
                ["M2"] = M2(samples),
                ["M3a"] = M3a(data, samples),
                ["M3b"] = M3b(data, samples),
                ["M3c"] = M3c(data, samples),
                ["M4"] = M4(samples),
                ["M5"] = M5(samples, tickRate),
                ["M6"] = M6(samples),
                ["M7"] = M7(samples),
            };
        }

        /// <summary>런 클리어율. 목표는 메타 측정점마다 다르다 — 판정은 호출부가 한다.</summary>
        private static JObject M1(IReadOnlyList<Sample> samples) => new JObject
        {
            ["clearRate"] = Round(samples.Count(s => s.Summary.Cleared) / (double)samples.Count),
            ["runs"] = samples.Count,
        };

        /// <summary>
        /// 진입 HP% 구간별 라운드 패배율. <b>단조 증가해야 하고 급등이 없어야 한다.</b>
        /// </summary>
        /// <remarks>
        /// 급등이 있으면 그 지점이 <b>난이도 곡선의 턱</b>이다 —
        /// 체력이 조금 깎인 순간 갑자기 못 이기게 되는 구간이고, 플레이어에게는
        /// "갑자기 어려워졌다"로 느껴진다.
        /// </remarks>
        private static JArray M2(IReadOnlyList<Sample> samples)
        {
            var total = new int[HpBuckets.Length];
            var lost = new int[HpBuckets.Length];

            foreach (var s in samples)
            {
                foreach (var round in s.Summary.Rounds)
                {
                    int b = BucketOf(round.EntryHpPermille);
                    total[b]++;
                    if (!round.Won) lost[b]++;
                }
            }

            var result = new JArray();
            for (int i = 0; i < HpBuckets.Length; i++)
            {
                int upper = i + 1 < HpBuckets.Length ? HpBuckets[i + 1] : 1000;
                result.Add(new JObject
                {
                    ["hpFrom"] = HpBuckets[i],
                    ["hpTo"] = upper,
                    ["rounds"] = total[i],
                    ["lossRate"] = total[i] == 0 ? 0d : Round(lost[i] / (double)total[i]),
                });
            }

            return result;
        }

        private static int BucketOf(int permille)
        {
            for (int i = HpBuckets.Length - 1; i >= 0; i--)
                if (permille >= HpBuckets[i]) return i;
            return 0;
        }

        /// <summary>캐릭터별 출전률 — <b>클리어한 런 기준</b>. 목표 각 20~80%.</summary>
        private static JObject M3a(GameData data, IReadOnlyList<Sample> samples)
        {
            var cleared = samples.Where(s => s.Summary.Cleared).ToList();
            var result = new JObject();

            foreach (var character in data.Characters)
            {
                double rate = cleared.Count == 0
                    ? 0d
                    : cleared.Count(s => s.Build.CharacterIds.Contains(character.Id)) / (double)cleared.Count;

                result[character.Id] = Round(rate);
            }

            return result;
        }

        /// <summary>
        /// 액티브 2택 중 선택 비율 — <b>그 캐릭터가 나온 클리어 런 안에서</b>. 목표 각 20~80%.
        /// </summary>
        /// <remarks>
        /// 분모를 전체 클리어 런으로 잡으면 출전률(`M3a`)이 섞여 들어온다.
        /// 물어보려는 건 <b>"그 캐릭터를 썼을 때 어느 쪽을 골랐나"</b> 다.
        /// </remarks>
        private static JObject M3b(GameData data, IReadOnlyList<Sample> samples)
        {
            var cleared = samples.Where(s => s.Summary.Cleared).ToList();
            var result = new JObject();

            foreach (var character in data.Characters)
            {
                var used = cleared.Where(s => s.Build.CharacterIds.Contains(character.Id)).ToList();
                var per = new JObject();

                foreach (string skillId in character.SkillIds)
                {
                    double rate = used.Count == 0
                        ? 0d
                        : used.Count(s => s.Build.ActiveByCharacter[character.Id] == skillId) / (double)used.Count;

                    per[skillId] = Round(rate);
                }

                result[character.Id] = per;
            }

            return result;
        }

        /// <summary>보조 스킬 채택률 — 그 캐릭터가 나온 클리어 런 안에서. 목표 각 15~70%.</summary>
        private static JObject M3c(GameData data, IReadOnlyList<Sample> samples)
        {
            var cleared = samples.Where(s => s.Summary.Cleared).ToList();
            var result = new JObject();

            foreach (var support in data.SupportSkills)
            {
                var used = cleared
                    .Where(s => s.Build.CharacterIds.Contains(support.CharacterId))
                    .ToList();

                double rate = used.Count == 0
                    ? 0d
                    : used.Count(s => s.Build.SupportsByCharacter[support.CharacterId].Contains(support.Id))
                      / (double)used.Count;

                result[support.Id] = Round(rate);
            }

            return result;
        }

        /// <summary>
        /// 상위 <see cref="TopFraction"/> 빌드가 <b>전체 클리어 중 차지하는 비율</b>. 목표 35% 미만 (`D-71`).
        /// </summary>
        /// <remarks>
        /// <para>
        /// 봇이 4,320 빌드를 <b>똑같이 한 번씩</b> 돌기 때문에 등장 빈도로 재면 어느 빌드든 1/4320 이다.
        /// 그래서 "점유율" 을 <b>클리어의 몫</b>으로 읽는다 — 균등하면 상위 5% 가 클리어의 5% 를 가져가고,
        /// 한 줌이 독식하면 그 값이 올라간다. <b>35% 는 제 몫의 7배</b>다.
        /// </para>
        /// <para>
        /// ★ <b><c>M1</c> 과 분리된다는 게 이 정의를 고른 이유다.</b> D+3 구현은
        /// *"클리어의 절반을 가져가는 빌드 비율"* 이었는데, 분모가 전체 빌드 수라
        /// <b>게임이 어려워져 클리어 가능한 빌드가 줄면 그 값이 같이 떨어진다.</b>
        /// 최적화기가 <c>M1</c> 을 고치는 방향이 곧 <c>M4</c> 를 깎는 방향이 되어
        /// <b>기하평균 안에서 두 지표가 서로 싸운다.</b> 비율로 재면 클리어 총량이 줄어도
        /// 분포가 균등한 한 값이 유지된다.
        /// </para>
        /// <para>
        /// ⚠️ <b><see cref="TopFraction"/> 5% 는 사람이 고른 값이다</b> (`D-71`).
        /// 스펙의 *"35% 미만"* 이라는 문장은 그대로 살렸다 — 임계값을 새로 정하지 않아도 되는 쪽을 택했다.
        /// </para>
        /// </remarks>
        private static JObject M4(IReadOnlyList<Sample> samples)
        {
            var byBuild = samples
                .GroupBy(s => s.Build.Id)
                .Select(g => new
                {
                    Id = g.Key,
                    Clears = g.Count(x => x.Summary.Cleared),
                })
                .OrderByDescending(x => x.Clears)
                // 동률을 id 로 끊는다. 안 끊으면 상위 N 의 경계에서 어느 빌드가 들어가는지가
                // 정렬 구현에 달리고, 같은 입력이 다른 M4 를 내게 된다.
                .ThenBy(x => x.Id, StringComparer.Ordinal)
                .ToList();

            long totalClears = byBuild.Sum(b => (long)b.Clears);

            // 반올림이 아니라 올림이다 — 빌드가 적을 때 상위 0개가 되면 M4 가 항상 0 이 된다.
            int topCount = (int)Math.Ceiling(byBuild.Count * TopFraction);
            if (topCount < 1) topCount = 1;
            if (topCount > byBuild.Count) topCount = byBuild.Count;

            long topClears = 0;
            for (int i = 0; i < topCount; i++) topClears += byBuild[i].Clears;

            double share = totalClears == 0 ? 0 : topClears / (double)totalClears;

            return new JObject
            {
                ["_definition"] = $"상위 {TopFraction:P0} 빌드가 전체 클리어 중 차지하는 비율 (D-71). "
                                + $"균등하면 {TopFraction:P0}, 목표는 35% 미만.",
                ["builds"] = byBuild.Count,
                ["topCount"] = topCount,
                ["topShare"] = Round(share),
                ["topBuildId"] = byBuild.Count == 0 ? "" : byBuild[0].Id,

                // 참고값. 목표 판정에는 안 쓰지만 "한 빌드가 100% 인가" 는 눈으로 봐야 한다.
                ["topBuildClearRate"] = Round(samples.Count == 0 || byBuild.Count == 0
                    ? 0
                    : byBuild[0].Clears / (double)samples.Count(s => s.Build.Id == byBuild[0].Id)),
            };
        }

        /// <summary>
        /// 1런 소요 시간. 목표 3~5분.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>전투 틱만 센다.</b> 실제 플레이에는 배치·상점 조작 시간이 더 붙는데
        /// 그건 사람의 속도라 시뮬이 알 수 없다. <b>하한으로 읽어야 한다</b> —
        /// 여기서 3분이 안 나오면 실제로도 안 나온다.
        /// </remarks>
        private static JObject M5(IReadOnlyList<Sample> samples, int tickRate)
        {
            var minutes = samples.Select(s => s.Summary.TotalTicks / (double)tickRate / 60.0).ToList();

            return new JObject
            {
                ["_note"] = "전투 틱만. 배치·상점 조작 시간은 포함되지 않는다 — 하한으로 읽는다.",
                ["combatMinutesAvg"] = Round(minutes.Average(), 3),
                ["combatMinutesMax"] = Round(minutes.Max(), 3),
            };
        }

        /// <summary>첫 스킬 활성화 라운드 분포. <b>한 라운드에 몰리지 않아야 한다.</b></summary>
        private static JObject M6(IReadOnlyList<Sample> samples)
        {
            var histogram = new JObject();
            for (int round = 0; round <= 8; round++)
            {
                int count = samples.Count(s => s.Summary.FirstActivationRound == round);
                if (count > 0) histogram[round == 0 ? "never" : round.ToString()] = count;
            }

            var bought = samples.Where(s => s.Summary.FirstActivationRound > 0).ToList();

            return new JObject
            {
                // ★ 이 값은 게임의 성질이 아니라 봇 정책의 그림자다. 리포트를 읽는 사람이
                //   숫자만 보고 "2라운드에 몰린다" 로 읽지 않도록 값 옆에 붙여둔다.
                ["_notMeasurable"] =
                    "M6 은 저축 전략(08 §4.3)이 작동하는가를 재는데, 봇은 저축을 안 하도록 " +
                    "의도적으로 설계돼 있다(08 §6.1). 이 분포는 봇 정책의 귀결이지 게임의 성질이 아니다. " +
                    "예선 지표·목적함수에서 제외한다(D-72 확정).",
                ["histogram"] = histogram,
                ["neverBought"] = samples.Count - bought.Count,
                ["avgRound"] = bought.Count == 0 ? 0d : Round(bought.Average(s => s.Summary.FirstActivationRound), 3),
            };
        }

        /// <summary>타임아웃(서든데스) 도달률. <b>목표 5% 미만.</b></summary>
        private static JObject M7(IReadOnlyList<Sample> samples)
        {
            int rounds = samples.Sum(s => s.Summary.Rounds.Count);
            int timedOut = samples.Sum(s => s.Summary.Rounds.Count(r => r.TimedOut));

            return new JObject
            {
                ["rounds"] = rounds,
                ["timeoutRate"] = rounds == 0 ? 0d : Round(timedOut / (double)rounds),
            };
        }

        private static double Round(double value, int digits = 4) => Math.Round(value, digits);
    }
}
