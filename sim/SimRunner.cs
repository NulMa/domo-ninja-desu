#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using DomoNinja.Core.Economy;
using DomoNinja.Core.Events;
using DomoNinja.Core.Rng;
using Newtonsoft.Json.Linq;

namespace DomoNinja.Sim
{
    /// <summary>`params.json` 으로 들어오는 실행 조건.</summary>
    /// <remarks>
    /// 기본값은 <b>가볍게</b> 잡았다. 인자 없이 돌렸을 때 4,320 빌드가 통째로 도는 건
    /// 손이 미끄러진 사람에게 벌을 주는 설계다. 전수 탐색은 <c>buildLimit: 0</c> 으로 명시해야 돈다.
    /// </remarks>
    public sealed class SimParams
    {
        public int Seeds { get; set; } = 5;
        public ulong SeedStart { get; set; } = 1;
        public string Stage { get; set; } = "S1";

        /// <summary>`meta.json._simulationPolicy.fixedPoints` 의 id — `meta0`·`meta50`·`metaMax`.</summary>
        public string Meta { get; set; } = "meta0";

        /// <summary>돌릴 빌드 수. <b>0 이면 전부</b>(4,320).</summary>
        public int BuildLimit { get; set; } = 50;

        public bool Parallel { get; set; } = true;

        /// <summary>빌드별 상세를 결과에 담을지. 4,320개면 파일이 커진다.</summary>
        public bool IncludeBuilds { get; set; } = true;

        /// <summary>
        /// 최적화기가 시도할 파라미터 값. <b>`/data` 파일은 건드리지 않는다</b> (`ParamOverrides`).
        /// </summary>
        public ParamOverrides.Set? Overrides { get; set; }

        public static SimParams FromJson(string json)
        {
            var o = JObject.Parse(json);
            var p = new SimParams();

            if (o["seeds"] != null) p.Seeds = (int)o["seeds"]!;
            if (o["seedStart"] != null) p.SeedStart = (ulong)o["seedStart"]!;
            if (o["stage"] != null) p.Stage = (string)o["stage"]!;
            if (o["meta"] != null) p.Meta = (string)o["meta"]!;
            if (o["buildLimit"] != null) p.BuildLimit = (int)o["buildLimit"]!;
            if (o["parallel"] != null) p.Parallel = (bool)o["parallel"]!;
            if (o["includeBuilds"] != null) p.IncludeBuilds = (bool)o["includeBuilds"]!;

            p.Overrides = ParamOverrides.FromJson(o["overrides"] as JObject);

            return p;
        }

        public JObject ToJson()
        {
            var result = new JObject
            {
                ["seeds"] = Seeds,
                ["seedStart"] = SeedStart,
                ["stage"] = Stage,
                ["meta"] = Meta,
                ["buildLimit"] = BuildLimit,
                ["parallel"] = Parallel,

                // ★ 값 자체와 지문을 둘 다 남긴다. 지문만 있으면 대조는 되지만 읽을 수가 없고,
                //   값만 있으면 [BAL] 커밋 본문에서 한 줄로 가리킬 수단이 없다 (`D-55`).
                ["overridesHash"] = ParamOverrides.Hash(Overrides),
            };

            if (Overrides != null) result["overrides"] = ParamOverrides.ToJson(Overrides);

            return result;
        }
    }

    /// <summary>빌드 하나의 집계.</summary>
    public sealed class BuildResult
    {
        public string Id = "";
        public int Runs;
        public int Cleared;
        public int RoundsWonTotal;
        public int TicksTotal;
        public long UnitTicksTotal;

        /// <summary>클리어율. `M1` 의 빌드별 값이다.</summary>
        public double ClearRate => Runs == 0 ? 0 : (double)Cleared / Runs;

        public double AvgRoundsWon => Runs == 0 ? 0 : (double)RoundsWonTotal / Runs;
    }

    /// <summary>
    /// 빌드 × 시드를 돌려 집계한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>로그를 끄고 돌린다</b>(<see cref="NullEventSink"/>). 그게 처리량의 전제다(`19` §2.5).
    /// 한 런에 이벤트가 수만 건 쌓이는데 4,320 빌드 × 시드 30 이면 그대로 메모리와 시간이 된다.
    /// </para>
    /// <para>
    /// ★ <b>병렬은 빌드 단위다.</b> 런은 서로 독립이고 각자 자기 시드만 본다.
    /// 결과는 인덱스 자리에 써넣으므로 <b>완료 순서가 결과 순서를 바꾸지 않는다</b> —
    /// 병렬이 결과를 흔들면 재현 가능한 리포트가 아니게 된다.
    /// </para>
    /// </remarks>
    public static class SimRunner
    {
        public sealed class Report
        {
            public int Runs;
            public int Cleared;
            public long TicksTotal;
            public long UnitTicksTotal;
            public double ElapsedMs;
            public BuildResult[] Builds = Array.Empty<BuildResult>();

            /// <summary>지표 계산의 원자료. 런 하나가 표본 하나다.</summary>
            public List<Metrics.Sample> Samples = new List<Metrics.Sample>();

            public double ClearRate => Runs == 0 ? 0 : (double)Cleared / Runs;

            /// <summary>유닛-틱당 마이크로초. <b>5µs 를 넘으면 최적화 대상이다</b> (`17` §7.1).</summary>
            public double MicrosPerUnitTick =>
                UnitTicksTotal == 0 ? 0 : ElapsedMs * 1000.0 / UnitTicksTotal;
        }

        public static Report Run(GameData data, SimParams p)
        {
            var config = CombatConfig.From(data.Economy, TickRate(data));

            var builds = BuildSpace.Enumerate(data);
            if (p.BuildLimit > 0) builds = builds.Take(p.BuildLimit);
            var buildList = builds.ToList();

            var results = new BuildResult[buildList.Count];

            // ★ 표본을 빌드별 통에 따로 담았다가 나중에 순서대로 합친다.
            //   공유 리스트에 병렬로 넣으면 순서가 실행마다 달라지고, 그러면
            //   같은 입력에서 리포트가 미묘하게 달라진다 — 재현 가능한 리포트가 아니게 된다.
            var buckets = new List<Metrics.Sample>[buildList.Count];
            var watch = Stopwatch.StartNew();

            if (p.Parallel)
            {
                Parallel.For(0, buildList.Count, i =>
                {
                    buckets[i] = new List<Metrics.Sample>(p.Seeds);
                    results[i] = RunBuild(data, config, buildList[i], p, buckets[i]);
                });
            }
            else
            {
                for (int i = 0; i < buildList.Count; i++)
                {
                    buckets[i] = new List<Metrics.Sample>(p.Seeds);
                    results[i] = RunBuild(data, config, buildList[i], p, buckets[i]);
                }
            }

            watch.Stop();

            var report = new Report
            {
                ElapsedMs = watch.Elapsed.TotalMilliseconds,
                Builds = p.IncludeBuilds ? results : Array.Empty<BuildResult>(),
            };

            foreach (var bucket in buckets)
            {
                if (bucket != null) report.Samples.AddRange(bucket);
            }

            foreach (var r in results)
            {
                report.Runs += r.Runs;
                report.Cleared += r.Cleared;
                report.TicksTotal += r.TicksTotal;
                report.UnitTicksTotal += r.UnitTicksTotal;
            }

            return report;
        }

        private static BuildResult RunBuild(GameData data, CombatConfig config, BuildTarget build, SimParams p,
                                            List<Metrics.Sample> samples)
        {
            var result = new BuildResult { Id = build.Id };
            var engine = new RunEngine(data, config);

            for (int s = 0; s < p.Seeds; s++)
            {
                ulong seed = p.SeedStart + (ulong)s;

                // ★ 메타 진행도는 시드마다 새로 만든다. 공유하면 한 런의 구매가 다음 런에 남고,
                //   그러면 "같은 시드 같은 결과"가 성립하지 않는다.
                var meta = MetaAt(data, p.Meta);
                var run = engine.StartRun(p.Stage, build.CharacterIds, meta);

                var summary = engine.PlayRun(run, meta, new DeterministicRandom(seed),
                                             NullEventSink.Instance, collectLogs: false, build: build);

                result.Runs++;
                if (summary.Cleared) result.Cleared++;
                result.RoundsWonTotal += summary.RoundsWon;
                result.TicksTotal += summary.TotalTicks;
                result.UnitTicksTotal += summary.TotalUnitTicks;

                samples.Add(new Metrics.Sample { Build = build, Summary = summary });
            }

            return result;
        }

        /// <summary>`meta.json._simulationPolicy.fixedPoints` 의 측정점을 만든다.</summary>
        private static MetaProgress MetaAt(GameData data, string pointId)
        {
            var meta = new MetaProgress(data.Meta);

            var policy = data.Meta.Raw["_simulationPolicy"] as JObject;
            if (!(policy?["fixedPoints"] is JArray points)) return meta;

            foreach (var point in points)
            {
                if ((string?)point["id"] != pointId) continue;

                if (point["allLevelsRatio"] != null)
                    meta.SetAllLevelsRatio((double)point["allLevelsRatio"]!);

                foreach (var stage in point["stages"] as JArray ?? new JArray())
                    meta.UnlockStage((string)stage!);

                return meta;
            }

            return meta;
        }

        private static int TickRate(GameData data)
        {
            // tickRate 는 characters.json 에 있다(`CombatConfig.From` 주석 참조).
            // sim 은 그 파일을 이미 읽었으므로 economy 쪽에 없다고 20 을 박지 않는다.
            var raw = data.Economy.Raw["_tickRate"];
            return (int?)raw ?? 20;
        }

        public static JObject ToJson(Report report, SimParams p, GameData data)
        {
            var metrics = Metrics.Compute(data, report.Samples, TickRate(data));
            var builds = new JArray();
            foreach (var b in report.Builds)
            {
                builds.Add(new JObject
                {
                    ["id"] = b.Id,
                    ["runs"] = b.Runs,
                    ["clearRate"] = Math.Round(b.ClearRate, 4),
                    ["avgRoundsWon"] = Math.Round(b.AvgRoundsWon, 3),
                });
            }

            return new JObject
            {
                // ★ provenance 는 필수다 (`_schema` §11). 어떤 조건에서 나온 숫자인지 없으면
                //   리포트끼리 비교할 수 없고, 밸런스 판정이 근거를 잃는다.
                ["provenance"] = new JObject
                {
                    ["generatedAt"] = DateTime.UtcNow.ToString("o"),
                    ["params"] = p.ToJson(),
                    ["stageHasEncounters"] = data.HasEncounterSetFor(p.Stage),
                    ["buildSpaceTotal"] = BuildSpace.Enumerate(data).Count(),
                },
                ["throughput"] = new JObject
                {
                    ["runs"] = report.Runs,
                    ["elapsedMs"] = Math.Round(report.ElapsedMs, 1),
                    ["unitTicks"] = report.UnitTicksTotal,
                    ["microsPerUnitTick"] = Math.Round(report.MicrosPerUnitTick, 4),
                    ["budgetMicros"] = 5.0,
                    ["withinBudget"] = report.MicrosPerUnitTick <= 5.0,
                },
                ["aggregate"] = new JObject
                {
                    ["clearRate"] = Math.Round(report.ClearRate, 4),
                    ["cleared"] = report.Cleared,
                    ["ticksTotal"] = report.TicksTotal,
                },
                ["metrics"] = metrics,
                ["objective"] = Objective.Compute(metrics, p.Meta),
                ["builds"] = builds,
            };
        }
    }
}
