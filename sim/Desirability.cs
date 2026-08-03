#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DomoNinja.Sim
{
    /// <summary>
    /// 지표 하나의 만족도 <c>d ∈ [0,1]</c> (Derringer–Suich).
    /// </summary>
    /// <remarks>
    /// <para>
    /// `M1`~`M7` 은 전부 <b>"이 구간에 들어와라"</b> 형태다 — 최대화도 최소화도 아니다.
    /// 그래서 목표 구간은 1, 허용 한계 밖은 0, 그 사이는 선형 보간이다.
    /// </para>
    /// <para>
    /// ★ <b>허용 한계(<see cref="Lower"/>·<see cref="Upper"/>)가 목표보다 넓은 게 요점이다.</b>
    /// 목표 밖이면 곧장 0 으로 떨어뜨리면 <b>최적화기가 기울기를 못 본다</b> —
    /// 어느 방향으로 가야 나아지는지 알 수 없어 탐색이 눈먼 상태가 된다.
    /// </para>
    /// </remarks>
    public readonly struct Desirability
    {
        public readonly string Name;

        /// <summary>이 아래는 만족도 0.</summary>
        public readonly double Lower;

        /// <summary>목표 구간 하한. 여기부터 만족도 1.</summary>
        public readonly double TargetLow;

        /// <summary>목표 구간 상한.</summary>
        public readonly double TargetHigh;

        /// <summary>이 위는 만족도 0.</summary>
        public readonly double Upper;

        public Desirability(string name, double lower, double targetLow, double targetHigh, double upper)
        {
            Name = name;
            Lower = lower; TargetLow = targetLow; TargetHigh = targetHigh; Upper = upper;
        }

        public double Score(double value)
        {
            if (value <= Lower || value >= Upper) return 0;
            if (value >= TargetLow && value <= TargetHigh) return 1;

            return value < TargetLow
                ? (value - Lower) / (TargetLow - Lower)
                : (Upper - value) / (Upper - TargetHigh);
        }
    }

    /// <summary>
    /// `M1`~`M7` 을 하나의 점수 <c>D</c> 로 접는다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <code>D = (d₁ · d₂ · … · dₙ)^(1/n)</code>
    /// </para>
    /// <para>
    /// ★ <b>기하평균이어야 한다. 하나라도 0 이면 전체가 0 이다.</b>
    /// 산술평균이면 *"`M4` 를 포기하고 나머지로 벌충하는"* 해가 최적으로 뽑히는데,
    /// <b>최적화기는 그런 구멍을 반드시 찾아낸다.</b>
    /// 이건 새 규칙이 아니라 `08` §6.2 의 *"하나라도 벗어나면 패치 대상"* 을 수식으로 옮긴 것이다 —
    /// 그래서 디렉팅 층의 의도가 목적함수에 그대로 보존된다.
    /// </para>
    /// <para>
    /// ⚠️ <b>가중치를 붙이지 않았다.</b> 균등에서 시작해 *"왜 이 지표에 더 줬는가"* 를
    /// 나중에 근거와 함께 올리는 쪽이, 처음부터 손으로 튜닝한 가중치보다 문서에서 강하다.
    /// </para>
    /// <para>
    /// ✅ <b>`M4` 가 D+4 에 들어왔다</b> (`D-71`). 상위 5% 빌드의 클리어 점유율로 정의가 확정됐고,
    /// <b>`M1` 과 분리되도록</b> 고른 정의다 — 난이도를 고치는 방향이 `M4` 를 깎지 않는다.
    /// </para>
    /// <para>
    /// ⚠️ <b>`M6` 은 예선 지표에서 뺀다</b> (`D-72` — 확정, 보류가 아니다).
    /// `M6` 은 *"저축 전략이 작동하는가"* 를 재는데 <b>봇은 저축을 안 하도록 의도적으로 설계했다</b>
    /// (`08` §6.1 — 그래야 측정 대상이 "빌드 자체의 성립 가능성" 으로 분리된다).
    /// 지표와 봇이 서로를 무효화하고 있어, <b>못 재는 지표를 목적함수에 넣으면
    /// 최적화기가 봇 정책의 그림자를 쫓는다.</b> 값은 계속 리포트에 내되 점수에서만 뺀다 —
    /// <b>무엇을 못 재는지 아는 것이 기록으로 남아야 한다.</b>
    /// </para>
    /// </remarks>
    public static class Objective
    {
        /// <summary>
        /// `M1` 은 메타 측정점마다 목표가 다르다 (`08` §6.2).
        /// </summary>
        /// <remarks>
        /// 허용 한계는 목표 폭만큼 양옆으로 벌렸다. 목표 밖에서도 기울기가 남아야
        /// 최적화기가 <b>어느 쪽으로 가야 나아지는지</b> 알 수 있다.
        /// </remarks>
        private static Desirability M1For(string metaPoint)
        {
            switch (metaPoint)
            {
                case "meta50": return new Desirability("M1", 0.25, 0.40, 0.55, 0.70);
                case "metaMax": return new Desirability("M1", 0.40, 0.55, 0.70, 0.85);
                default: return new Desirability("M1", 0.10, 0.25, 0.40, 0.55);
            }
        }

        // 각 20~80%. 밖으로 나가도 기울기가 남게 0~100% 를 한계로 둔다.
        private static readonly Desirability Share = new Desirability("share", 0.0, 0.20, 0.80, 1.0);

        // 보조는 15~70%.
        private static readonly Desirability SupportShare = new Desirability("supportShare", 0.0, 0.15, 0.70, 1.0);

        /// <summary>
        /// `M4` — 상위 5% 빌드의 클리어 점유율. <b>35% 미만이면 만족</b> (`08` §6.2 · `D-71`).
        /// </summary>
        /// <remarks>
        /// 하한을 두지 않는다. 균등 분포(5%)보다 <b>더 고른 건 나쁠 게 없다</b> —
        /// 하한을 두면 최적화기가 <b>일부러 지배 빌드를 만들어</b> 점수를 채우려 든다.
        /// 위쪽 한계 60% 는 "상위 5% 가 클리어의 6할" 이면 사실상 단일 해라는 뜻이다.
        /// </remarks>
        private static readonly Desirability TopShare = new Desirability("M4", -1.0, 0.0, 0.35, 0.60);

        // 1런 3~5분. 아래로는 0분, 위로는 8분을 한계로.
        private static readonly Desirability RunMinutes = new Desirability("M5", 0.0, 3.0, 5.0, 8.0);

        // 타임아웃 5% 미만. 0 이 이상적이라 하한을 두지 않는다.
        private static readonly Desirability Timeout = new Desirability("M7", -1.0, 0.0, 0.05, 0.15);

        /// <summary>
        /// `M2` 는 값이 아니라 <b>모양</b>을 본다 — 단조 증가하고 급등이 없어야 한다.
        /// </summary>
        /// <remarks>
        /// 구간 사이 최대 점프를 만족도로 바꾼다. 0.25(25%p) 이하면 만족, 0.6 을 넘으면 0.
        /// <b>급등 지점이 곧 난이도 곡선의 턱</b>이고, 플레이어에게는 "갑자기 어려워졌다"로 느껴진다.
        /// </remarks>
        private static readonly Desirability CurveJump = new Desirability("M2", -1.0, 0.0, 0.25, 0.60);

        public sealed class Term
        {
            public string Name = "";
            public double Value;
            public double Score;
            public string? Note;
        }

        public static JObject Compute(JObject metrics, string metaPoint)
        {
            var terms = new List<Term>();

            // M1 — 클리어율
            double m1 = (double?)metrics["M1"]?["clearRate"] ?? 0;
            var m1Spec = M1For(metaPoint);
            terms.Add(new Term { Name = "M1", Value = m1, Score = m1Spec.Score(m1) });

            // M2 — 구간 사이 최대 점프
            double jump = MaxJump(metrics["M2"] as JArray);
            terms.Add(new Term { Name = "M2", Value = jump, Score = CurveJump.Score(jump),
                                 Note = "구간 사이 최대 패배율 점프" });

            // M3a — 캐릭터 출전률. 가장 나쁜 캐릭터가 점수를 정한다.
            AddWorst(terms, "M3a", metrics["M3a"] as JObject, Share, "가장 치우친 캐릭터");

            // M3b — 액티브 2택. 캐릭터마다 두 값이 있고 그중 최악을 본다.
            var m3bValues = new List<KeyValuePair<string, double>>();
            foreach (var character in (metrics["M3b"] as JObject) ?? new JObject())
            {
                foreach (var skill in (character.Value as JObject) ?? new JObject())
                    m3bValues.Add(new KeyValuePair<string, double>(skill.Key, (double?)skill.Value ?? 0));
            }
            AddWorst(terms, "M3b", m3bValues, Share, "가장 치우친 2택");

            // M3c — 보조 채택률
            AddWorst(terms, "M3c", metrics["M3c"] as JObject, SupportShare, "가장 치우친 보조");

            // M4 — 상위 5% 빌드의 클리어 점유율 (D-71)
            double share = (double?)metrics["M4"]?["topShare"] ?? 0;
            terms.Add(new Term { Name = "M4", Value = share, Score = TopShare.Score(share),
                                 Note = "상위 5% 빌드가 가져가는 클리어 몫 (균등이면 5%)" });

            // M5 — 1런 시간 (전투 + 조작 추정, `D-74`)
            double minutes = (double?)metrics["M5"]?["runMinutesAvg"] ?? 0;
            terms.Add(new Term { Name = "M5", Value = minutes, Score = RunMinutes.Score(minutes),
                                 Note = "전투 + 라운드당 조작 추정 — 상수는 D+6 실측으로 교체" });

            // M7 — 타임아웃
            double timeout = (double?)metrics["M7"]?["timeoutRate"] ?? 0;
            terms.Add(new Term { Name = "M7", Value = timeout, Score = Timeout.Score(timeout) });

            double d = GeometricMean(terms.Select(t => t.Score));

            var breakdown = new JArray();
            foreach (var t in terms)
            {
                var item = new JObject
                {
                    ["metric"] = t.Name,
                    ["value"] = Math.Round(t.Value, 4),
                    ["d"] = Math.Round(t.Score, 4),
                };
                if (t.Note != null) item["note"] = t.Note;
                breakdown.Add(item);
            }

            return new JObject
            {
                ["D"] = Math.Round(d, 4),
                ["metaPoint"] = metaPoint,
                ["terms"] = breakdown,
                ["excluded"] = new JArray("M6"),
                ["_excludedWhy"] =
                    "M6 은 이 봇으로 측정할 수 없다 — 저축 전략을 재는 지표인데 봇은 저축을 " +
                    "안 하도록 의도적으로 설계돼 있다(08 §6.1). 지표와 봇이 서로를 무효화한다. " +
                    "예선 지표에서 뺀다(D-72 확정). 값은 리포트에 계속 내되 점수에서만 뺀다.",
                ["verdict"] = d >= 1.0 ? "pass" : "fail",
                ["violations"] = new JArray(terms.Where(t => t.Score < 1.0).Select(t => (object)t.Name).ToArray()),
            };
        }

        /// <summary>
        /// 기하평균. <b>하나라도 0 이면 0 이다</b> — 그게 이 함수를 고른 이유다.
        /// </summary>
        /// <remarks>
        /// 곱을 그대로 쌓지 않고 로그로 더하는 이유 — 항이 7개면 <c>0.5⁷ ≈ 0.008</c> 이라
        /// 배정밀도에서도 자릿수가 빠르게 줄어든다. 0 은 로그를 취할 수 없으므로 먼저 걸러낸다.
        /// </remarks>
        public static double GeometricMean(IEnumerable<double> scores)
        {
            var list = scores.ToList();
            if (list.Count == 0) return 0;
            if (list.Any(s => s <= 0)) return 0;

            double sum = list.Sum(Math.Log);
            return Math.Exp(sum / list.Count);
        }

        private static double MaxJump(JArray? buckets)
        {
            if (buckets == null || buckets.Count < 2) return 0;

            double max = 0;
            for (int i = 1; i < buckets.Count; i++)
            {
                double prev = (double?)buckets[i - 1]["lossRate"] ?? 0;
                double cur = (double?)buckets[i]["lossRate"] ?? 0;

                // 방향 무관하게 급변을 본다. 단조성이 깨진 것도 급변이다.
                double diff = Math.Abs(prev - cur);
                if (diff > max) max = diff;
            }

            return max;
        }

        /// <summary>
        /// 여러 값 중 <b>가장 나쁜 것</b>이 그 지표의 점수를 정한다.
        /// </summary>
        /// <remarks>
        /// 평균을 쓰면 사장된 캐릭터 하나를 나머지 다섯이 가려버린다.
        /// `M3a` 가 잡으려는 게 바로 그 하나다 — <b>평균은 그걸 못 본다.</b>
        /// </remarks>
        private static void AddWorst(List<Term> terms, string name, JObject? values,
                                     Desirability spec, string note)
        {
            var pairs = new List<KeyValuePair<string, double>>();
            foreach (var kv in values ?? new JObject())
                pairs.Add(new KeyValuePair<string, double>(kv.Key, (double?)kv.Value ?? 0));

            AddWorst(terms, name, pairs, spec, note);
        }

        private static void AddWorst(List<Term> terms, string name,
                                     List<KeyValuePair<string, double>> values,
                                     Desirability spec, string note)
        {
            if (values.Count == 0)
            {
                terms.Add(new Term { Name = name, Value = 0, Score = 0, Note = "표본 없음" });
                return;
            }

            var worst = values
                .OrderBy(kv => spec.Score(kv.Value))
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .First();

            terms.Add(new Term
            {
                Name = name,
                Value = worst.Value,
                Score = spec.Score(worst.Value),
                Note = $"{note}: {worst.Key}",
            });
        }
    }
}
