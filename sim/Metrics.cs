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
        /// 지배 빌드 감시. <b>목표: 최상위 빌드 35% 미만.</b>
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>"점유율"의 정의가 스펙에 없다.</b> 봇이 모든 빌드를 똑같이 한 번씩 돌기 때문에
        /// 등장 빈도로 재면 어느 빌드든 1/4320 이라 지표가 성립하지 않는다.
        ///
        /// 여기서는 <b>"클리어한 빌드 중 상위 몇 %가 클리어의 절반을 가져가는가"</b> 로 읽었다 —
        /// 한 빌드가 압도적이면 그 값이 작아진다. 함께 최고 클리어율도 같이 낸다.
        ///
        /// <b>이 해석은 확정이 아니다.</b> `M4` 는 `D-30` 채택의 결정적 근거였던 지표라
        /// 정의가 흔들리면 그 근거도 흔들린다 — 사람이 한 번 정하고 스펙에 적어야 한다.
        /// </remarks>
        private static JObject M4(IReadOnlyList<Sample> samples)
        {
            var byBuild = samples
                .GroupBy(s => s.Build.Id)
                .Select(g => new
                {
                    Id = g.Key,
                    ClearRate = g.Count(x => x.Summary.Cleared) / (double)g.Count(),
                })
                .OrderByDescending(x => x.ClearRate)
                .ThenBy(x => x.Id, StringComparer.Ordinal)
                .ToList();

            double totalClear = byBuild.Sum(b => b.ClearRate);
            double half = totalClear / 2.0;

            int needed = 0;
            double acc = 0;
            foreach (var b in byBuild)
            {
                acc += b.ClearRate;
                needed++;
                if (acc >= half) break;
            }

            return new JObject
            {
                ["_definitionPending"] = "점유율 정의가 스펙에 없다. 08 §6.2 에 사람이 확정해야 한다.",
                ["builds"] = byBuild.Count,
                ["topClearRate"] = Round(byBuild.Count == 0 ? 0 : byBuild[0].ClearRate),
                ["topBuildId"] = byBuild.Count == 0 ? "" : byBuild[0].Id,
                ["buildsForHalfOfClears"] = needed,
                ["concentration"] = Round(byBuild.Count == 0 ? 0 : needed / (double)byBuild.Count),
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
