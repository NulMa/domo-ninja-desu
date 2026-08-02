// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System.Collections.Generic;
using DomoNinja.Core.Data;

namespace DomoNinja.Core.Economy
{
    /// <summary>
    /// 시뮬이 목표로 삼는 빌드 하나. <b>봇은 이것만 산다.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 로스터 3명 + 각자의 액티브 1택 + 각자의 보조 2택. 아이템은 들어가지 않는다 —
    /// 빌드 공간(`08` §6.1)의 축이 아니고, 넣으면 전수 탐색이 무너진다.
    /// </para>
    /// <para>
    /// ★ <b>봇이 사람의 실력을 흉내 내지 않는 게 요점이다.</b>
    /// 목표를 고정해두면 측정 대상이 <b>빌드 자체의 성립 가능성</b>으로 깨끗하게 분리된다.
    /// 봇이 상황을 보고 판단하면 `M1`~`M7` 이 재는 게 게임이 아니라 <b>봇의 판단력</b>이 된다.
    /// </para>
    /// </remarks>
    public sealed class BuildTarget
    {
        /// <summary>출전 3명. <b>이 순서가 슬롯 인덱스가 된다.</b></summary>
        public IReadOnlyList<string> CharacterIds { get; }

        /// <summary>캐릭터 id → 활성화할 액티브 스킬 id.</summary>
        public IReadOnlyDictionary<string, string> ActiveByCharacter { get; }

        /// <summary>캐릭터 id → 살 보조 스킬 2개.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportsByCharacter { get; }

        public BuildTarget(
            IReadOnlyList<string> characterIds,
            IReadOnlyDictionary<string, string> activeByCharacter,
            IReadOnlyDictionary<string, IReadOnlyList<string>> supportsByCharacter)
        {
            CharacterIds = characterIds;
            ActiveByCharacter = activeByCharacter;
            SupportsByCharacter = supportsByCharacter;
        }

        /// <summary>리포트에 찍을 짧은 이름. <b>같은 빌드는 항상 같은 문자열이 된다.</b></summary>
        public string Id
        {
            get
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < CharacterIds.Count; i++)
                {
                    if (i > 0) sb.Append('/');
                    string c = CharacterIds[i];
                    sb.Append(ActiveByCharacter[c]);

                    var supports = SupportsByCharacter[c];
                    for (int k = 0; k < supports.Count; k++)
                        sb.Append('+').Append(Suffix(supports[k]));
                }
                return sb.ToString();
            }
        }

        /// <summary>`C1-P2` → `P2`. 캐릭터는 앞의 액티브 id 로 이미 드러난다.</summary>
        private static string Suffix(string skillId)
        {
            int dash = skillId.LastIndexOf('-');
            return dash >= 0 ? skillId.Substring(dash + 1) : skillId;
        }

        public override string ToString() => Id;
    }

    /// <summary>
    /// 빌드 공간을 전부 열거한다. <b>4,320개</b> (`08` §6.1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <code>
    /// 로스터 6 중 3      C(6,3) = 20
    /// 각자 액티브 2택     2³     = 8
    /// 각자 보조 3 중 2    C(3,2)³ = 27
    ///                     ─────────────
    ///                     4,320
    /// </code>
    /// </para>
    /// <para>
    /// ★ <b>보조를 "2개 다 산다"로 고정한 게 이 숫자를 지킨 결정이다.</b>
    /// 0·1개도 허용하면 캐릭터당 7가지(1+3+3) → 7³ = 343 → 총 <b>54,880</b> 이 되어 전수 탐색이 무너진다.
    /// 0·1개만 사는 건 <b>재화가 모자란 상황이지 빌드 선택이 아니고</b>, 그건 `M6`(저축 전략)이 따로 잰다.
    /// </para>
    /// <para>
    /// ⚠️ <b>보조 풀이 캐릭터당 4 이상이 되면 이 방어가 무너진다</b> — C(4,2)=6 → 6³=216 → 34,560.
    /// 검증 규칙 `R04` 가 풀 3 고정을 지키고 있고, 그게 여기를 지키는 것이기도 하다.
    /// </para>
    /// </remarks>
    public static class BuildSpace
    {
        /// <summary>모든 빌드. <b>순서가 결정적이다</b> — 같은 데이터면 항상 같은 순서로 나온다.</summary>
        public static IEnumerable<BuildTarget> Enumerate(GameData data)
        {
            var roster = data.Characters;
            int deploy = data.Economy.DeployCount;

            foreach (var trio in Combinations(roster.Count, deploy))
            {
                var characterIds = new string[deploy];
                for (int i = 0; i < deploy; i++) characterIds[i] = roster[trio[i]].Id;

                // 각 캐릭터의 (액티브 2택 × 보조 2택 3가지) = 6가지를 곱한다.
                var options = new List<(string active, IReadOnlyList<string> supports)>[deploy];
                for (int i = 0; i < deploy; i++) options[i] = OptionsFor(data, characterIds[i]);

                foreach (var pick in CartesianIndices(options))
                {
                    var actives = new Dictionary<string, string>(deploy);
                    var supports = new Dictionary<string, IReadOnlyList<string>>(deploy);

                    for (int i = 0; i < deploy; i++)
                    {
                        var option = options[i][pick[i]];
                        actives[characterIds[i]] = option.active;
                        supports[characterIds[i]] = option.supports;
                    }

                    yield return new BuildTarget(characterIds, actives, supports);
                }
            }
        }

        /// <summary>한 캐릭터가 가질 수 있는 (액티브, 보조 2개) 조합. 2 × 3 = 6가지.</summary>
        private static List<(string, IReadOnlyList<string>)> OptionsFor(GameData data, string characterId)
        {
            var character = data.FindCharacter(characterId)!;

            var pool = new List<string>();
            foreach (var s in data.SupportSkills)
                if (s.CharacterId == characterId) pool.Add(s.Id);

            var result = new List<(string, IReadOnlyList<string>)>();

            foreach (string active in character.SkillIds)
            {
                foreach (var pair in Combinations(pool.Count, 2))
                    result.Add((active, new[] { pool[pair[0]], pool[pair[1]] }));
            }

            return result;
        }

        /// <summary><paramref name="n"/> 중 <paramref name="k"/> 를 고르는 인덱스 조합. 사전순.</summary>
        private static IEnumerable<int[]> Combinations(int n, int k)
        {
            if (k <= 0 || k > n) yield break;

            var index = new int[k];
            for (int i = 0; i < k; i++) index[i] = i;

            while (true)
            {
                yield return (int[])index.Clone();

                int pos = k - 1;
                while (pos >= 0 && index[pos] == n - k + pos) pos--;
                if (pos < 0) yield break;

                index[pos]++;
                for (int i = pos + 1; i < k; i++) index[i] = index[i - 1] + 1;
            }
        }

        /// <summary>자리별 선택지 개수의 곱집합을 인덱스로 훑는다.</summary>
        private static IEnumerable<int[]> CartesianIndices<T>(IReadOnlyList<List<T>> options)
        {
            var pick = new int[options.Count];

            while (true)
            {
                yield return (int[])pick.Clone();

                int pos = options.Count - 1;
                while (pos >= 0)
                {
                    pick[pos]++;
                    if (pick[pos] < options[pos].Count) break;
                    pick[pos] = 0;
                    pos--;
                }

                if (pos < 0) yield break;
            }
        }
    }
}
