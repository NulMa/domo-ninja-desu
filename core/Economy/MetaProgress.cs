// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System.Collections.Generic;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using DomoNinja.Core.Skills;

namespace DomoNinja.Core.Economy
{
    /// <summary>
    /// 런 사이에 누적되는 <b>영구</b> 강화 진행도 (`meta.json` · `08` §4.5).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ 여기 재화는 <see cref="Domain.RunState.Currency"/> 와 <b>다른 화폐다.</b>
    /// 런 재화는 런이 끝나면 소멸하고 이건 남는다. 둘을 섞으면
    /// "런 안에서 잘하기"와 "런을 반복하기"가 같은 자원을 두고 경쟁해 밸런스 해석이 불가능해진다.
    /// </para>
    /// <para>
    /// ★ <b>캐릭터를 지정하지 않는다.</b> 로스터 6명 전원에게 똑같이 적용된다 —
    /// 특정 캐릭터만 강화되면 `M3a`(출전률 20~80%)가 메타 진행도에 끌려다니게 되고,
    /// 그러면 캐릭터 밸런스와 메타 밸런스를 분리해서 읽을 수 없다.
    /// </para>
    /// <para>
    /// 저장 대상은 <b>이것과 언락 스테이지뿐이다</b>(`D-56` 재정의).
    /// 런 진행 상태는 저장하지 않는다 — 정수 배열 몇 개인 이쪽과 달리
    /// 런 상태는 시드 스트림 위치까지 직렬화해야 하고, 틀리면 재현 불가능한 버그가 된다.
    /// </para>
    /// </remarks>
    public sealed class MetaProgress
    {
        private readonly Dictionary<string, int> _levels = new Dictionary<string, int>();
        private readonly MetaDef _def;

        /// <summary>영구 재화. <b>런 재화가 아니다.</b></summary>
        public int Currency { get; set; }

        /// <summary>열린 스테이지. 초기값은 `S1` 하나다 (`D-68`).</summary>
        public List<string> UnlockedStages { get; } = new List<string> { "S1" };

        public MetaProgress(MetaDef def)
        {
            _def = def;
        }

        public int LevelOf(string upgradeId) =>
            _levels.TryGetValue(upgradeId, out int lv) ? lv : 0;

        /// <summary>레벨을 직접 지정한다. 시뮬의 고정 측정점(`meta0`·`meta50`·`metaMax`)이 쓴다.</summary>
        public void SetLevel(string upgradeId, int level)
        {
            foreach (var u in _def.Upgrades)
            {
                if (u.Id != upgradeId) continue;

                int clamped = level < 0 ? 0 : (level > u.MaxLevel ? u.MaxLevel : level);
                _levels[upgradeId] = clamped;
                return;
            }
        }

        /// <summary>모든 강화를 같은 비율로 채운다. `_simulationPolicy.fixedPoints` 의 `allLevelsRatio`.</summary>
        public void SetAllLevelsRatio(double ratio)
        {
            foreach (var u in _def.Upgrades)
            {
                // 정수 절삭. 0.5 에서 maxLevel 5 면 2 다 — 반올림하면 측정점이 "절반"보다 세진다.
                SetLevel(u.Id, (int)(u.MaxLevel * ratio));
            }
        }

        /// <summary>다음 레벨 비용. 만렙이면 <c>null</c>.</summary>
        public int? NextCost(string upgradeId)
        {
            foreach (var u in _def.Upgrades)
            {
                if (u.Id != upgradeId) continue;

                int lv = LevelOf(upgradeId);
                return lv >= u.MaxLevel ? (int?)null : u.Costs[lv];
            }
            return null;
        }

        /// <summary>살 수 있으면 사고 <c>true</c>. 재화가 모자라거나 만렙이면 아무것도 하지 않는다.</summary>
        public bool TryUpgrade(string upgradeId)
        {
            int? cost = NextCost(upgradeId);
            if (cost == null || Currency < cost.Value) return false;

            Currency -= cost.Value;
            SetLevel(upgradeId, LevelOf(upgradeId) + 1);
            return true;
        }

        /// <summary>
        /// 스탯 하나에 걸리는 증감분(천분율). <b>합연산에 참여한다</b> (`_schema` §8 · §10.5).
        /// </summary>
        /// <remarks>
        /// 런 내부 아이템·스킬 버프와 전부 더한 뒤 마지막에 한 번만 적용한다.
        /// 메타만 따로 곱하면 메타 진행도가 오를수록 런 내부 빌드의 효과가 같이 커져,
        /// 두 축을 분리해 읽으려던 설계가 무너진다.
        /// </remarks>
        public int DeltaPermilleFor(StatKey stat)
        {
            var sum = new ModifierSum();

            foreach (var u in _def.Upgrades)
            {
                int level = LevelOf(u.Id);
                if (level <= 0) continue;
                if (StatKeys.Parse(u.Stat) != stat) continue;

                // ⚠️ valuePerLevel 은 배율이 아니라 증감분 자체다 — 0.04 는 1.04 가 아니라 +4% 다.
                //    배율로 읽고 1000 을 빼면 +4% 가 -88% 가 된다(테스트가 이 실수를 잡았다).
                //    레벨만큼 더한다. 곱하지 않는다 — 3레벨은 1.04³(12.5%)가 아니라 12% 다.
                sum.AddDeltaPermille(Permille.FromMultiplier(u.ValuePerLevel * level));
            }

            return sum.DeltaPermille;
        }

        /// <summary>라운드 승리 회복량에 더할 천분율. `M-HEAL` 은 배율이 아니라 <b>가산</b>이다.</summary>
        public int RoundEndHealBonusPermille => FlatBonus("roundEndHealPermille");

        /// <summary>라운드 승리 재화에 더할 값. `M-GOLD`.</summary>
        public int CurrencyOnWinBonus => FlatBonus("currencyOnWin");

        /// <remarks>
        /// 스탯 강화와 달리 이 둘은 <c>valuePerLevel</c> 이 비율이 아니라 <b>절대값</b>이다
        /// (`+50‰`, `+1`). 같은 함수로 다루면 50 이 5% 로 읽힌다.
        /// </remarks>
        private int FlatBonus(string statName)
        {
            int total = 0;
            foreach (var u in _def.Upgrades)
            {
                if (u.Stat != statName) continue;
                total += (int)(u.ValuePerLevel * LevelOf(u.Id));
            }
            return total;
        }

        /// <summary>런 결과로 얻는 영구 재화. <b>패배해도 도달 라운드만큼은 받는다.</b></summary>
        /// <remarks>
        /// 실패한 런도 다음 런을 위한 진전이 되는 게 이 장르의 핵심이다.
        /// 0 을 주면 연패했을 때 진행이 멈추고, 그러면 `M5`(1런 3~5분)가 의미를 잃는다.
        /// </remarks>
        public int EarnedFrom(int roundsCleared, bool runCleared) =>
            roundsCleared * _def.EarnPerRoundCleared + (runCleared ? _def.EarnOnRunClear : 0);

        public void UnlockStage(string stageId)
        {
            if (!UnlockedStages.Contains(stageId)) UnlockedStages.Add(stageId);
        }

        public bool IsUnlocked(string stageId) => UnlockedStages.Contains(stageId);
    }
}
