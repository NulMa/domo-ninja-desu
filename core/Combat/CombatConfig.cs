// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using DomoNinja.Core.Data;
using Newtonsoft.Json.Linq;

namespace DomoNinja.Core.Combat
{
    /// <summary>
    /// 전투 파라미터. <b>전부 `economy.json` 에서 온다 — 코드에 상수를 박지 않는다.</b>
    /// </summary>
    /// <remarks>
    /// <see cref="EconomyDef"/> 를 통째로 들고 다니지 않고 이 struct 로 뽑는 이유 —
    /// 틱 루프가 매 틱 참조하는 값들이라 <see cref="JObject"/> 조회(문자열 해시)를 두면
    /// 그 비용이 <b>유닛 × 틱 × 런</b> 만큼 곱해진다. 로드 시 한 번만 풀어서 정수로 들고 간다.
    /// </remarks>
    public readonly struct CombatConfig
    {
        /// <summary>1초당 틱. 20 (`A-2`).</summary>
        public readonly int TickRate;

        /// <summary>이 틱을 넘기면 서든데스에 들어간다. 초기값 900 = 45초.</summary>
        public readonly int TimeoutTicks;

        /// <summary><c>true</c> 면 서든데스가 아군에게만 걸린다 (`A1` 확정).</summary>
        public readonly bool SuddenDeathAllyOnly;

        /// <summary>서든데스 피해 적용 주기. 매 초(20틱).</summary>
        public readonly int ApplyEveryTicks;

        /// <summary>이 틱에 걸쳐 시작값에서 상한까지 선형 증가한다. 600틱 = 30초.</summary>
        public readonly int RampTicks;

        /// <summary>고정 피해 시작값 (최대 체력의 천분율).</summary>
        public readonly int FixedDamageStartPermille;

        /// <summary>고정 피해 상한 (최대 체력의 천분율).</summary>
        public readonly int FixedDamageMaxPermille;

        /// <summary>회복량 감소 시작값 (천분율).</summary>
        public readonly int HealReductionStartPermille;

        /// <summary>회복량 감소 상한 (천분율). 1000 이면 회복이 완전히 막힌다.</summary>
        public readonly int HealReductionMaxPermille;

        public CombatConfig(int tickRate, int timeoutTicks, bool suddenDeathAllyOnly,
                            int applyEveryTicks, int rampTicks,
                            int fixedDamageStartPermille, int fixedDamageMaxPermille,
                            int healReductionStartPermille, int healReductionMaxPermille)
        {
            TickRate = tickRate;
            TimeoutTicks = timeoutTicks;
            SuddenDeathAllyOnly = suddenDeathAllyOnly;
            ApplyEveryTicks = applyEveryTicks;
            RampTicks = rampTicks;
            FixedDamageStartPermille = fixedDamageStartPermille;
            FixedDamageMaxPermille = fixedDamageMaxPermille;
            HealReductionStartPermille = healReductionStartPermille;
            HealReductionMaxPermille = healReductionMaxPermille;
        }

        /// <summary>
        /// 검증을 통과한 데이터에서 뽑는다.
        /// </summary>
        /// <remarks>
        /// <c>tickRate</c> 만 `characters.json` 에 있다 — `economy.json` 에 있어야 자연스럽지만
        /// 데이터를 옮기면 그건 스키마 변경이고 사람이 결정할 일이다(`_schema` 머리말).
        /// 값을 옮기는 대신 <b>어디서 오는지를 여기 적어둔다.</b>
        /// </remarks>
        public static CombatConfig From(EconomyDef economy, int tickRate)
        {
            var combat = economy.Raw["combat"] as JObject ?? new JObject();
            var sd = combat["suddenDeath"] as JObject ?? new JObject();
            var fixedDmg = sd["fixedDamagePermille"] as JObject ?? new JObject();
            var healRed = sd["healReductionPermille"] as JObject ?? new JObject();

            return new CombatConfig(
                tickRate,
                economy.TimeoutTicks,
                (string?)sd["appliesTo"] != "both",
                (int?)sd["applyEveryTicks"] ?? tickRate,
                (int?)sd["rampTicks"] ?? 600,
                (int?)fixedDmg["start"] ?? 0,
                (int?)fixedDmg["max"] ?? 0,
                (int?)healRed["start"] ?? 0,
                (int?)healRed["max"] ?? 0);
        }

        /// <summary>
        /// 서든데스 램프. <b>천분율 정수 나눗셈이다.</b>
        /// </summary>
        /// <remarks>
        /// <c>value(t) = start + (max - start) * min(t, rampTicks) / rampTicks</c>
        /// (`economy.combat.suddenDeath._formula`)
        ///
        /// 실수를 쓰지 않는 이유는 여기가 <b>매 초 호출되는 자리</b>라서다.
        /// 부동소수점이면 몇백 틱 뒤 누산 결과가 플랫폼마다 갈릴 수 있다.
        /// </remarks>
        /// <param name="elapsed">서든데스 발동 후 경과 틱.</param>
        public int RampPermille(int elapsed, int startPermille, int maxPermille)
        {
            if (elapsed < 0) elapsed = 0;
            int t = elapsed < RampTicks ? elapsed : RampTicks;
            return startPermille + (maxPermille - startPermille) * t / RampTicks;
        }
    }
}
