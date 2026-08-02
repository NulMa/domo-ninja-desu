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

        /// <summary>스테이지 1 의 관문. <see cref="RoundsFor"/> 의 기본값과 같다.</summary>
        public IReadOnlyList<RoundDef> Rounds { get; }

        /// <summary>
        /// 관문 세트 이름(`economy.stages.list[].encounterSet`) → 라운드 목록.
        /// </summary>
        /// <remarks>
        /// ★ <b>적 타입 테이블(<see cref="EnemyTypes"/>)은 스테이지가 공유한다.</b>
        /// 스테이지마다 적 스탯을 따로 두면 같은 이름의 적이 스테이지별로 다른 값을 갖게 되고,
        /// 밸런스 리포트에서 *"슬라임이 약한가"* 를 물을 수 없게 된다.
        /// 난이도는 <b>개체 조합과 수량으로만</b> 올린다 — 팀원 저작 노트와 같은 판단이다.
        /// </remarks>
        public IReadOnlyDictionary<string, IReadOnlyList<RoundDef>> EncounterSets { get; }

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
            MetaDef meta,
            IReadOnlyDictionary<string, IReadOnlyList<RoundDef>>? encounterSets = null)
        {
            Characters = characters;
            Skills = skills;
            SupportSkills = supportSkills;
            EnemyTypes = enemyTypes;
            Rounds = rounds;
            Economy = economy;
            Meta = meta;

            var sets = new Dictionary<string, IReadOnlyList<RoundDef>>();
            if (encounterSets != null)
                foreach (var kv in encounterSets) sets[kv.Key] = kv.Value;

            // 스테이지 1 은 encounters.json 의 rounds 다. 세트 이름이 따로 안 왔으면 여기서 채운다.
            if (!sets.ContainsKey(DefaultEncounterSet)) sets[DefaultEncounterSet] = rounds;
            EncounterSets = sets;

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

        /// <summary>`encounters.json` 이 담는 기본 세트 이름.</summary>
        public const string DefaultEncounterSet = "stage1";

        /// <summary>
        /// 스테이지 id(`S1`·`S2`)의 관문. <b>없는 스테이지는 스테이지 1 로 떨어진다.</b>
        /// </summary>
        /// <remarks>
        /// 예외를 던지지 않는 이유 — 스테이지 2 저작이 아직 안 들어왔을 수 있고,
        /// 그때 시뮬 전체가 멈추면 <b>아트·저작 진행이 밸런스 루프를 막는다.</b>
        /// 대신 <see cref="HasEncounterSetFor"/> 로 물어볼 수 있게 열어둔다.
        /// </remarks>
        public IReadOnlyList<RoundDef> RoundsFor(string stageId)
        {
            string setName = EncounterSetName(stageId);
            return EncounterSets.TryGetValue(setName, out var rounds) ? rounds : Rounds;
        }

        /// <summary>그 스테이지의 관문이 실제로 들어와 있는가.</summary>
        public bool HasEncounterSetFor(string stageId) =>
            EncounterSets.ContainsKey(EncounterSetName(stageId));

        /// <summary>`economy.stages.list` 에서 스테이지 id 에 대응하는 세트 이름을 찾는다.</summary>
        private string EncounterSetName(string stageId)
        {
            var stages = Economy.Raw["stages"] as Newtonsoft.Json.Linq.JObject;
            if (stages?["list"] is Newtonsoft.Json.Linq.JArray list)
            {
                foreach (var s in list)
                {
                    if ((string?)s["id"] != stageId) continue;
                    return (string?)s["encounterSet"] ?? DefaultEncounterSet;
                }
            }
            return DefaultEncounterSet;
        }
    }
}
