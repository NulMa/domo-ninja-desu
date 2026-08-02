#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using DomoNinja.Core.Economy;
using DomoNinja.Core.Events;
using DomoNinja.Core.Rng;

namespace DomoNinja.Sim
{
    /// <summary>
    /// 런 하나를 <b>사람이 읽을 수 있게</b> 콘솔에 풀어놓는다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 화면이 없는 동안 <b>"지금 어디까지 됐나"를 확인할 수 있는 유일한 창</b>이다.
    /// Unity View 는 `P5`(팀원 작업분)라 그때까지 게임은 눈에 보이지 않는데,
    /// 그 상태로 며칠을 가면 <b>돌아가는 것과 돌아간다고 믿는 것</b>이 구분되지 않는다.
    /// </para>
    /// <para>
    /// 이벤트 로그를 그대로 읽는다 — View 가 나중에 받을 것과 <b>같은 자료</b>다.
    /// 여기서 말이 되면 View 도 말이 된다. 별도 출력 경로를 만들면 그 보장이 사라진다.
    /// </para>
    /// </remarks>
    public static class Replay
    {
        public static int Run(GameData data, string stage, ulong seed, int buildIndex, int? onlyRound)
        {
            var config = CombatConfig.From(data.Economy, 20);
            var build = BuildSpace.Enumerate(data).Skip(buildIndex).FirstOrDefault();

            if (build == null)
            {
                Console.Error.WriteLine($"빌드 {buildIndex} 가 없다 (전체 {BuildSpace.Enumerate(data).Count()}개)");
                return 2;
            }

            var meta = new MetaProgress(data.Meta);
            var engine = new RunEngine(data, config);
            var run = engine.StartRun(stage, build.CharacterIds, meta);

            Console.WriteLine($"빌드 #{buildIndex}  {build.Id}");
            Console.WriteLine($"스테이지 {stage} · 시드 {seed} · 메타 없음(meta0)");
            Console.WriteLine(new string('─', 72));

            var summary = engine.PlayRun(run, meta, new DeterministicRandom(seed),
                                         NullEventSink.Instance, collectLogs: true, build: build);

            foreach (var outcome in summary.Rounds)
            {
                if (onlyRound != null && outcome.Round != onlyRound.Value) continue;
                PrintRound(data, outcome);
            }

            Console.WriteLine(new string('─', 72));
            Console.WriteLine($"결과: {(summary.Cleared ? "클리어" : "실패")} · " +
                              $"{summary.RoundsWon}/{summary.RoundsReached}승 · " +
                              $"생명 {summary.LivesLeft} · " +
                              $"{summary.TotalTicks / 20.0:F1}초");

            foreach (var entry in run.Deployed)
            {
                string skills = entry.ActiveSkillId ?? "(없음)";
                if (entry.SupportSkillIds.Count > 0)
                    skills += " + " + string.Join(" + ", entry.SupportSkillIds);

                Console.WriteLine($"  {entry.CharacterId} {entry.Hp,4}/{entry.MaxHp,-4} {skills}");
            }

            return 0;
        }

        private static void PrintRound(GameData data, RoundOutcome outcome)
        {
            var log = outcome.Log;
            if (log == null) return;

            Console.WriteLine();
            Console.WriteLine($"■ 라운드 {outcome.Round} ({outcome.VariantId})  " +
                              $"{(outcome.Won ? "승" : "패")} · {outcome.Ticks / 20.0:F1}초 · +{outcome.CurrencyGained}");

            var names = new Dictionary<int, string>();
            foreach (var u in log.Units)
            {
                names[u.UnitId] = $"{u.TypeId}#{u.UnitId}";
                Console.WriteLine($"    {(u.Team == 0 ? "아군" : "적  ")} {names[u.UnitId],-14} " +
                                  $"hp {u.MaxHp,-4} @{Where(u.StartCoordKey)}");
            }

            string Name(int id) => id < 0 ? "—" : (names.TryGetValue(id, out var n) ? n : $"#{id}");

            int lastSecond = -1;
            foreach (var e in log.Events)
            {
                // 초 단위로 묶는다. 20틱이 1초라 틱을 그대로 찍으면 사람이 못 읽는다.
                int second = e.Tick / 20;
                if (second != lastSecond)
                {
                    Console.WriteLine($"    [{second,3}초]");
                    lastSecond = second;
                }

                string line = Describe(e, Name);
                if (line.Length > 0) Console.WriteLine($"          {line}");
            }
        }

        private static string Describe(in GameEvent e, Func<int, string> name)
        {
            switch (e.Kind)
            {
                case EventKind.Move:
                    return $"{name(e.ActorId)} 이동 {Where(e.Aux)} → {Where(e.Value)}";

                case EventKind.Attack:
                    return $"{name(e.ActorId)} → {name(e.TargetId)} 공격";

                case EventKind.Damage:
                    return e.Value == 0
                        ? $"    {name(e.TargetId)} 무효 (hp {e.Aux})"
                        : $"    {name(e.TargetId)} -{e.Value} (hp {e.Aux})";

                case EventKind.Shield:
                    return $"    {name(e.TargetId)} 보호막 {e.Value:+#;-#;0} → {e.Aux}";

                case EventKind.Heal:
                    return $"    {name(e.TargetId)} +{e.Value} 회복 (hp {e.Aux})";

                case EventKind.Death:
                    return $"    ☠ {name(e.TargetId)} 사망";

                case EventKind.StatusApply:
                    return $"    {name(e.TargetId)} [{(StatusKind)e.Value}]";

                case EventKind.StatusExpire:
                    return $"    {name(e.TargetId)} [{(StatusKind)e.Value}] 해제";

                case EventKind.SuddenDeath:
                    return "★ 서든데스";

                case EventKind.RoundEnd:
                    return e.Value == 1 ? "▶ 승리" : "▶ 패배";

                default:
                    return "";
            }
        }

        /// <summary>좌표키를 (x,y) 로 되돌린다. 8×6 보드다.</summary>
        private static string Where(int orderKey) => $"({orderKey % 8},{orderKey / 8})";
    }
}
