using System;
using System.IO;
using System.Linq;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Rng;

namespace DomoNinja.Sim
{
    /// <summary>
    /// 헤드리스 시뮬레이터 진입점.
    /// </summary>
    /// <remarks>
    /// P0 시점에는 <c>--selftest</c> 만 실제로 동작한다.
    /// 런 루프는 P3 에서 붙는다(19 §3). 그때까지 이 바이너리의 역할은
    /// <b>"CI 에서 dotnet 으로 실행된다"는 게이트 2 를 지금 세워두는 것</b>이다.
    /// 파이프라인을 코드보다 먼저 세우는 이유는, 나중에 세우면 그때는
    /// 코드가 안 되는 건지 파이프라인이 안 되는 건지 구분이 안 되기 때문이다.
    /// </remarks>
    public static class Program
    {
        public static int Main(string[] args)
        {
            // Windows 콘솔 기본 코드페이지(cp949)에서 한국어 출력이 깨진다.
            // CI 로그는 사람이 읽는 것이고, 깨진 로그는 안 읽는 로그가 된다.
            // 리다이렉트된 스트림에서는 실패할 수 있으므로 조용히 넘어간다.
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
            catch (System.IO.IOException) { }

            if (args.Length == 0 || args[0] is "-h" or "--help")
            {
                PrintUsage();
                return args.Length == 0 ? 1 : 0;
            }

            return args[0] switch
            {
                "--selftest" => SelfTest(),
                "--version" => Version(),
                "--run" => RunSim(args),
                "--replay" => RunReplay(args),
                _ => Unknown(args[0]),
            };
        }

        /// <summary>런 하나를 사람이 읽게 풀어놓는다. <b>화면이 없는 동안의 유일한 창이다.</b></summary>
        private static int RunReplay(string[] args)
        {
            GameData data;
            try
            {
                data = LoadData(ArgAfter(args, "--data") ?? FindDataDir());
            }
            catch (DataValidationException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 3;
            }

            ulong seed = ulong.TryParse(ArgAfter(args, "--seed"), out ulong s) ? s : 1UL;
            int build = int.TryParse(ArgAfter(args, "--build"), out int b) ? b : 0;
            int? round = int.TryParse(ArgAfter(args, "--round"), out int r) ? r : (int?)null;
            string stage = ArgAfter(args, "--stage") ?? "S1";

            return Replay.Run(data, stage, seed, build, round);
        }

        /// <remarks>
        /// core 는 파일을 직접 열지 않는다 — WebGL 에 파일 시스템이 없어서다.
        /// 읽는 방법은 실행 환경이 정하고, 여기서는 디스크다.
        /// </remarks>
        private static GameData LoadData(string dataDir, ParamOverrides.Set? overrides = null,
                                         BalanceReport.DataHasher? hasher = null)
        {
            Func<string, string?> read = name =>
            {
                string path = Path.Combine(dataDir, name);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            };

            // 순서가 중요하다 — 덮어쓰기를 **적용한 뒤**를 해싱해야 지문이 지문 노릇을 한다.
            // 앞뒤가 바뀌면 최적화기가 돌린 모든 실행이 같은 해시를 갖는다.
            read = ParamOverrides.Wrap(read, overrides);
            if (hasher != null) read = hasher.Wrap(read);

            return GameDataFiles.Load(read);
        }

        /// <summary>
        /// `params.json` → `metrics.json`.
        /// </summary>
        /// <remarks>
        /// ★ <b>이 인터페이스가 `/tune` 을 Python 으로 분리할 수 있게 한 근거다</b> (`D-49-a`).
        /// 외부 최적화 루프는 이 바이너리를 호출만 하면 되므로 어떤 언어로 짜도 된다.
        /// 파일로 주고받는 이유도 같다 — 프로세스 경계를 파일로 그으면
        /// 최적화기가 죽어도 <b>마지막 결과가 디스크에 남는다.</b>
        /// </remarks>
        private static int RunSim(string[] args)
        {
            string? paramsPath = ArgAfter(args, "--run");
            string outPath = ArgAfter(args, "--out") ?? "metrics.json";
            string dataDir = ArgAfter(args, "--data") ?? FindDataDir();

            var p = paramsPath == null || !File.Exists(paramsPath)
                ? new SimParams()
                : SimParams.FromJson(File.ReadAllText(paramsPath));

            if (paramsPath != null && !File.Exists(paramsPath))
                Console.Error.WriteLine($"경고: {paramsPath} 가 없어 기본값으로 돈다");

            string? balancePath = ArgAfter(args, "--balance");
            var hasher = new BalanceReport.DataHasher();

            GameData data;
            try
            {
                data = LoadData(dataDir, p.Overrides, hasher);
            }
            catch (DataValidationException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 3;
            }
            catch (ArgumentException ex)
            {
                // 경로가 안 맞은 경우. 조용히 넘기면 최적화기가 헛돈다 — ParamOverrides 주석 참조.
                Console.Error.WriteLine(ex.Message);
                return 4;
            }

            Console.WriteLine($"데이터: {dataDir}");
            if (p.Overrides != null)
            {
                int count = p.Overrides.Sum(f => f.Value.Count);
                Console.WriteLine($"덮어쓰기: {count}개 · 지문 {ParamOverrides.Hash(p.Overrides)}");
            }
            Console.WriteLine($"빌드 {(p.BuildLimit > 0 ? p.BuildLimit.ToString() : "전부")} × 시드 {p.Seeds} · {p.Stage} · {p.Meta}");

            var report = SimRunner.Run(data, p);
            var json = SimRunner.ToJson(report, p, data);
            File.WriteAllText(outPath, json.ToString());

            if (balancePath != null)
            {
                var balance = BalanceReport.From(json, p, hasher.Hash(), GitCommit(dataDir));
                File.WriteAllText(balancePath, balance.ToString());
            }

            Console.WriteLine();
            Console.WriteLine($"  런 {report.Runs:N0}회 · {report.ElapsedMs:N0}ms");
            Console.WriteLine($"  클리어율 {report.ClearRate:P1}");
            Console.WriteLine($"  유닛-틱당 {report.MicrosPerUnitTick:F3}µs " +
                              $"({(report.MicrosPerUnitTick <= 5.0 ? "예산 내" : "예산 초과")})");
            Console.WriteLine($"  → {outPath}");
            if (balancePath != null) Console.WriteLine($"  → {balancePath} (스냅샷)");

            return 0;
        }

        /// <summary>
        /// 리포트에 박을 커밋 해시. <b>어느 코드로 낸 숫자인지가 없으면 재현이 성립하지 않는다</b> (`D-55`).
        /// </summary>
        /// <remarks>
        /// ★ <c>git</c> 을 <b>프로세스로 부르지 않는다.</b> <c>.git/HEAD</c> 를 읽는다 —
        /// <c>core</c>·<c>sim</c> 이 외부 프로세스를 띄우지 못하게 <c>balance.yml</c> 가드가 막고 있고,
        /// 그 가드는 *"게임은 자기 힘으로 돈다"* 를 지키려고 둔 것이다. 리포트 한 줄 때문에 뚫지 않는다.
        /// </remarks>
        private static string GitCommit(string dataDir)
        {
            try
            {
                var dir = new DirectoryInfo(dataDir);
                while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
                    dir = dir.Parent;
                if (dir == null) return "unknown";

                string gitDir = Path.Combine(dir.FullName, ".git");
                string head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();

                if (!head.StartsWith("ref:", StringComparison.Ordinal))
                    return head.Length >= 7 ? head.Substring(0, 7) : head;

                string refPath = Path.Combine(gitDir, head.Substring(4).Trim().Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(refPath))
                {
                    string sha = File.ReadAllText(refPath).Trim();
                    return sha.Length >= 7 ? sha.Substring(0, 7) : sha;
                }

                // 참조가 packed-refs 에 접혀 있는 경우. 갓 클론한 저장소가 이 상태다.
                string packed = Path.Combine(gitDir, "packed-refs");
                if (File.Exists(packed))
                {
                    string want = head.Substring(4).Trim();
                    foreach (string line in File.ReadAllLines(packed))
                    {
                        if (line.Length < 42 || line[0] == '#' || line[0] == '^') continue;
                        if (line.EndsWith(" " + want, StringComparison.Ordinal))
                            return line.Substring(0, 7);
                    }
                }

                return "unknown";
            }
            catch
            {
                // 해시를 못 읽는 게 시뮬을 멈출 이유는 아니다. 다만 "모른다" 를 그대로 적는다 —
                // 빈 문자열이면 리포트를 읽는 쪽이 "안 넣었나" 와 구분할 수 없다.
                return "unknown";
            }
        }

        /// <remarks>
        /// 다음 토큰이 또 다른 플래그면 값이 없는 것으로 본다.
        /// <c>--run --out x.json</c> 에서 <c>--out</c> 을 params 경로로 읽으면
        /// "파일이 없다"는 경고만 뜨고 조용히 기본값으로 돈다 — 실제로 겪었다.
        /// </remarks>
        private static string? ArgAfter(string[] args, string flag)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != flag) continue;

                string next = args[i + 1];
                return next.StartsWith("--", StringComparison.Ordinal) ? null : next;
            }
            return null;
        }

        /// <summary>저장소의 `/data` 를 거슬러 올라가며 찾는다. 출력 경로가 재배치돼 깊이가 고정이 아니다.</summary>
        private static string FindDataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "data");
                if (File.Exists(Path.Combine(candidate, "economy.json"))) return candidate;
                dir = dir.Parent;
            }
            return "data";
        }

        private static void PrintUsage()
        {
            Console.WriteLine("DomoNinja.Sim — 헤드리스 시뮬레이터");
            Console.WriteLine();
            Console.WriteLine("  --run [params.json] [--out metrics.json] [--data DIR]   시뮬 실행");
            Console.WriteLine("  --replay [--seed N] [--build N] [--round N] [--stage S1]   런 1회를 읽기 좋게 출력");
            Console.WriteLine("  --selftest    결정론 자체 점검 (게이트 2)");
            Console.WriteLine("  --version     버전 출력");
            Console.WriteLine();
            Console.WriteLine("  기본값: 빌드 50 x 시드 5. 전수 탐색은 params 에 buildLimit: 0.");
        }

        private static int Version()
        {
            Console.WriteLine("DomoNinja.Sim 0.1.0 (P0 스캐폴딩)");
            return 0;
        }

        private static int Unknown(string arg)
        {
            Console.Error.WriteLine($"알 수 없는 인자: {arg}");
            PrintUsage();
            return 2;
        }

        /// <summary>
        /// 결정론 자체 점검. <b>CI 가 매 푸시마다 이걸 돌린다.</b>
        /// </summary>
        /// <remarks>
        /// 테스트 프로젝트에도 같은 검사가 있지만 여기 한 번 더 두는 이유가 있다.
        /// core.tests 는 dotnet test 가 돌리고, 이건 실제 배포되는 것과 같은
        /// 실행 파일이 돌린다. 빌드 설정(최적화·트리밍·IL2CPP 유사 조건)이
        /// 결정론을 깨는 경우가 실제로 있어서, 산출물 자체로도 확인해야 한다.
        /// </remarks>
        private static int SelfTest()
        {
            const ulong seed = 20260802UL;
            bool ok = true;

            ok &= Check("같은 시드 -> 같은 상태 해시", () =>
            {
                var a = new DeterministicRandom(seed);
                var b = new DeterministicRandom(seed);
                for (int i = 0; i < 100_000; i++) { a.NextUInt64(); b.NextUInt64(); }
                return a.StateHash() == b.StateHash();
            });

            ok &= Check("스트림 분리", () =>
            {
                var root = new DeterministicRandom(seed);
                var combat = root.Fork(RngStream.Combat);
                var shop = root.Fork(RngStream.Shop);
                return combat.NextUInt64() != shop.NextUInt64();
            });

            ok &= Check("정수 제곱거리", () =>
                new Coord(0, 0).SqrDistanceTo(new Coord(3, 4)) == 25);

            ok &= Check("좌표 동률 키 유일성", () =>
            {
                var seen = new System.Collections.Generic.HashSet<int>();
                for (int y = 0; y < Coord.BoardHeight; y++)
                for (int x = 0; x < Coord.BoardWidth; x++)
                    if (!seen.Add(new Coord(x, y).OrderKey)) return false;
                return true;
            });

            Console.WriteLine();
            Console.WriteLine(ok ? "결정론 자체 점검 통과" : "결정론 자체 점검 실패");
            return ok ? 0 : 1;
        }

        private static bool Check(string name, Func<bool> body)
        {
            bool result;
            try
            {
                result = body();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  FAIL  {name} — 예외: {ex.Message}");
                return false;
            }

            Console.WriteLine($"  {(result ? "ok  " : "FAIL")}  {name}");
            return result;
        }
    }
}
