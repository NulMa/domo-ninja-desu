using System;
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
                _ => Unknown(args[0]),
            };
        }

        private static void PrintUsage()
        {
            Console.WriteLine("DomoNinja.Sim — 헤드리스 시뮬레이터");
            Console.WriteLine();
            Console.WriteLine("  --selftest    결정론 자체 점검 (게이트 2)");
            Console.WriteLine("  --version     버전 출력");
            Console.WriteLine();
            Console.WriteLine("  params.json -> metrics.json 실행은 P3 에서 붙는다.");
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
