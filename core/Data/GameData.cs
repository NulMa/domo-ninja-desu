// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System.Collections.Generic;

namespace DomoNinja.Core.Data
{
    /// <summary>
    /// 검증을 통과한 게임 데이터 전체. <b>이 객체가 존재한다는 것 자체가 "계약을 만족한다"는 뜻이다.</b>
    /// </summary>
    /// <remarks>
    /// 생성자를 <see cref="GameDataLoader"/> 만 부를 수 있게 두지는 않았지만,
    /// 정상 경로는 로더 하나뿐이다. 검증되지 않은 데이터로 전투를 돌리면
    /// 그 런의 결과는 재현도 해석도 되지 않는다.
    /// </remarks>
    public sealed class GameData
    {
        public IReadOnlyList<CharacterDef> Characters { get; }

        /// <summary>액티브 스킬 12개 (캐릭터당 2).</summary>
        public IReadOnlyList<SkillDef> Skills { get; }

        /// <summary>보조 스킬 18개 (캐릭터당 3). 선택한 메인을 강화한다 (A3).</summary>
        public IReadOnlyList<SkillDef> SupportSkills { get; }

        public IReadOnlyDictionary<string, EnemyTypeDef> EnemyTypes { get; }
        public IReadOnlyList<RoundDef> Rounds { get; }
        public EconomyDef Economy { get; }
        public MetaDef Meta { get; }

        private readonly Dictionary<string, CharacterDef> _charById;
        private readonly Dictionary<string, SkillDef> _skillById;

        public GameData(
            IReadOnlyList<CharacterDef> characters,
            IReadOnlyList<SkillDef> skills,
            IReadOnlyList<SkillDef> supportSkills,
            IReadOnlyDictionary<string, EnemyTypeDef> enemyTypes,
            IReadOnlyList<RoundDef> rounds,
            EconomyDef economy,
            MetaDef meta)
        {
            Characters = characters;
            Skills = skills;
            SupportSkills = supportSkills;
            EnemyTypes = enemyTypes;
            Rounds = rounds;
            Economy = economy;
            Meta = meta;

            _charById = new Dictionary<string, CharacterDef>();
            foreach (var c in characters) _charById[c.Id] = c;

            _skillById = new Dictionary<string, SkillDef>();
            foreach (var s in skills) _skillById[s.Id] = s;
            foreach (var s in supportSkills) _skillById[s.Id] = s;
        }

        /// <remarks>
        /// ⚠️ 조회에만 쓴다. <b>Dictionary 를 순회하지 말 것</b> — 순회 순서는 구현이 바뀌면 같이 바뀌고,
        /// 그 순서에 의존하는 순간 결정론이 깨진다 (`_schema` §7). 순서가 필요하면
        /// <see cref="Characters"/> · <see cref="Skills"/> 리스트를 쓴다.
        /// </remarks>
        public CharacterDef? FindCharacter(string id) =>
            _charById.TryGetValue(id, out var c) ? c : null;

        /// <inheritdoc cref="FindCharacter"/>
        public SkillDef? FindSkill(string id) =>
            _skillById.TryGetValue(id, out var s) ? s : null;
    }
}
