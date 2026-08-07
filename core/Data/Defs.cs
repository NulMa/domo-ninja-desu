// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System.Collections.Generic;
using DomoNinja.Core.Domain;
using Newtonsoft.Json.Linq;

namespace DomoNinja.Core.Data
{
    /// <summary>로스터 캐릭터 1명. `characters.json`</summary>
    public sealed class CharacterDef
    {
        public string Id { get; }
        public string Sprite { get; }
        public string Name { get; }
        public int Hp { get; }
        public int Attack { get; }
        public int AttackInterval { get; }
        public int Range { get; }
        public int MoveInterval { get; }

        /// <summary>액티브 스킬 id 2개. 배타 선택의 대상이다 (08 §2.2).</summary>
        public IReadOnlyList<string> SkillIds { get; }

        /// <summary>캐릭터 소개 문구(성격·배경). 표시 전용 — 전투 규칙에 관여하지 않는다. 없을 수 있다.</summary>
        public string? Flavor { get; }

        public CharacterDef(string id, string sprite, string name, int hp, int attack,
                            int attackInterval, int range, int moveInterval, IReadOnlyList<string> skillIds,
                            string? flavor = null)
        {
            Id = id; Sprite = sprite; Name = name;
            Hp = hp; Attack = attack; AttackInterval = attackInterval;
            Range = range; MoveInterval = moveInterval; SkillIds = skillIds;
            Flavor = flavor;
        }

        public override string ToString() => $"{Id}({Name})";
    }

    /// <summary>적 타입 1종. `encounters.json.enemyTypes`. <b>적은 스킬을 갖지 않는다</b> (D-39).</summary>
    public sealed class EnemyTypeDef
    {
        public string Type { get; }
        public string Sprite { get; }
        public int Hp { get; }
        public int Attack { get; }
        public int AttackInterval { get; }
        public int Range { get; }

        /// <summary>고정포대는 이 값이 없다. <see cref="Immobile"/> 이 true 면 이동 판정 자체를 건너뛴다.</summary>
        public int? MoveInterval { get; }

        /// <summary>고정포대형. <b>적 전용이다</b> (A5).</summary>
        public bool Immobile { get; }

        public bool IsBoss { get; }

        public EnemyTypeDef(string type, string sprite, int hp, int attack, int attackInterval,
                            int range, int? moveInterval, bool immobile, bool isBoss)
        {
            Type = type; Sprite = sprite; Hp = hp; Attack = attack;
            AttackInterval = attackInterval; Range = range;
            MoveInterval = moveInterval; Immobile = immobile; IsBoss = isBoss;
        }

        public override string ToString() => Type;
    }

    /// <summary>스킬 1개. 액티브(12)와 보조(18)가 같은 타입을 쓴다.</summary>
    /// <remarks>
    /// ★ <see cref="Effects"/> 를 <see cref="JArray"/> 로 둔 이유 —
    /// 효과 템플릿을 타입으로 펴면 P2(전투 코어)의 해석기를 여기서 미리 짜는 셈이 된다.
    /// P1 의 책임은 "구조가 계약대로인지 확인하는 것"까지고, 해석은 P2 가 한다.
    /// 검증은 이 배열을 순회하면서 하므로 타입이 없어도 부족하지 않다.
    /// </remarks>
    public sealed class SkillDef
    {
        public string Id { get; }
        public string CharacterId { get; }
        public string Name { get; }

        /// <summary>액티브만 갖는다. 퓨어딜러/브루저/탱커/힐러/디버퍼/버퍼.</summary>
        public string? Role { get; }

        /// <summary>보조만 갖는다. 공격/생존/팀 (§10 3자리 문법).</summary>
        public string? Slot { get; }

        public string? TextGain { get; }
        public string? TextCost { get; }

        /// <summary>
        /// 아이콘 경로. <c>Skill/{캐릭터폴더}/{Main|Support}/{id}_{이름}</c> — 접두사·확장자 없음.
        /// </summary>
        /// <remarks>
        /// <see cref="CharacterDef.Sprite"/> 와 같은 규칙이다. 아직 없을 수 있어서 nullable 이다.
        ///
        /// ★ 경로가 <see cref="Id"/> 와 <see cref="Name"/> 을 <b>중복해서 담는다.</b>
        /// 그래서 이름을 바꾸면 파일도 같이 바꿔야 하고, 안 바꾸면 <b>아이콘만 조용히 안 뜬다</b> —
        /// 컴파일도 로드도 안 걸린다. 검증 규칙 <c>R22</c> 가 그 어긋남을 로드 시점으로 당긴다.
        ///
        /// 파생 가능한데도 필드로 두는 이유 — 아이콘을 공유하거나 이름과 다른 파일명을 써야 하는
        /// 예외가 아트 쪽에서 나올 수 있다. <b>명시하되 검증하는</b> 쪽이 계산해서 없애는 쪽보다 낫다.
        /// </remarks>
        public string? Icon { get; }

        public JArray Effects { get; }

        /// <summary>⚠️ <b>표시에 쓰지 않는다.</b> `_schema` §5 — 런타임 태그는 effects 에서 파생한다. 이 값은 검증용 기대치다.</summary>
        public IReadOnlyList<string> DeclaredTags { get; }

        /// <summary>캐릭터가 그 스킬을 두고 하는 한마디. 표시 전용 — 전투 규칙에 관여하지 않는다.
        /// 지금은 메인 12개만 갖는다(보조 18개는 없음) — 없을 수 있다.</summary>
        public string? Flavor { get; }

        public SkillDef(string id, string characterId, string name, string? role, string? slot,
                        string? textGain, string? textCost, JArray effects, IReadOnlyList<string> declaredTags,
                        string? icon = null, string? flavor = null)
        {
            Id = id; CharacterId = characterId; Name = name; Role = role; Slot = slot;
            TextGain = textGain; TextCost = textCost; Effects = effects; DeclaredTags = declaredTags;
            Icon = icon; Flavor = flavor;
        }

        /// <summary>보조 스킬인가. <see cref="Slot"/> 을 갖는 쪽이 보조다.</summary>
        public bool IsSupport => Slot != null;

        public override string ToString() => $"{Id}({Name})";
    }

    /// <summary>관문 1구성. 라운드 하나가 변형 1~2개를 갖는다.</summary>
    public sealed class VariantDef
    {
        public string Id { get; }
        public IReadOnlyList<EnemyPlacement> Units { get; }

        public VariantDef(string id, IReadOnlyList<EnemyPlacement> units)
        {
            Id = id; Units = units;
        }

        public override string ToString() => $"{Id}({Units.Count}체)";
    }

    /// <summary>적 1체의 <b>시작 위치</b>. 좌표는 시작점일 뿐 고정 위치가 아니다 (A5).</summary>
    public readonly struct EnemyPlacement
    {
        public readonly string Type;
        public readonly Coord At;

        public EnemyPlacement(string type, Coord at)
        {
            Type = type; At = at;
        }

        public override string ToString() => $"{Type}@{At}";
    }

    /// <summary>라운드 1개 (1~8).</summary>
    public sealed class RoundDef
    {
        public int Round { get; }

        /// <summary>이 라운드가 시험하는 딜레마 축. 밸런스 리포트의 분류 축이 된다.</summary>
        public string AxisTested { get; }

        public IReadOnlyList<VariantDef> Variants { get; }

        public RoundDef(int round, string axisTested, IReadOnlyList<VariantDef> variants)
        {
            Round = round; AxisTested = axisTested; Variants = variants;
        }

        public override string ToString() => $"R{Round}({Variants.Count}변형)";
    }

    /// <summary>
    /// `economy.json` 중 <b>구조에 해당하는 값들.</b> 런 전용이다 — 영구 강화는 <see cref="MetaDef"/>.
    /// </summary>
    /// <remarks>
    /// 전부 타입으로 펴지 않고 <see cref="Raw"/> 를 같이 둔 이유 —
    /// 이 파일은 최적화기가 값을 바꿔 쓰는 표면이고(`_schema` 머리말), 항목이 계속 는다.
    /// 필드를 하나 늘릴 때마다 여기를 고쳐야 하면 "코드에 상수를 박지 않는다"가 형태만 남는다.
    /// → <b>전투 루프가 매 틱 읽는 값만 타입으로 꺼내고</b>, 나머지는 Raw 로 조회한다.
    ///   JObject 조회는 문자열 해시라 틱 루프에 두면 비싸지만, 로드 시 1회는 무시할 수 있다.
    /// </remarks>
    public sealed class EconomyDef
    {
        public int TotalRounds { get; }
        public int Lives { get; }
        public int RosterPoolSize { get; }
        public int DeployCount { get; }

        /// <summary>진영당 열 수. 전체 보드 가로는 이 값의 2배다.</summary>
        public int BoardCols { get; }
        public int BoardRows { get; }

        public int TimeoutTicks { get; }
        public int CurrencyOnWin { get; }
        public int CurrencyOnLose { get; }
        public int ShopSkillSlots { get; }
        public int ShopItemSlots { get; }
        public int RerollCost { get; }
        public int SupportPoolPerCharacter { get; }
        public int SupportMaxSelectPerCharacter { get; }

        public JObject Raw { get; }

        public EconomyDef(int totalRounds, int lives, int rosterPoolSize, int deployCount,
                          int boardCols, int boardRows, int timeoutTicks,
                          int currencyOnWin, int currencyOnLose,
                          int shopSkillSlots, int shopItemSlots, int rerollCost,
                          int supportPoolPerCharacter, int supportMaxSelectPerCharacter, JObject raw)
        {
            TotalRounds = totalRounds; Lives = lives;
            RosterPoolSize = rosterPoolSize; DeployCount = deployCount;
            BoardCols = boardCols; BoardRows = boardRows;
            TimeoutTicks = timeoutTicks;
            CurrencyOnWin = currencyOnWin; CurrencyOnLose = currencyOnLose;
            ShopSkillSlots = shopSkillSlots; ShopItemSlots = shopItemSlots; RerollCost = rerollCost;
            SupportPoolPerCharacter = supportPoolPerCharacter;
            SupportMaxSelectPerCharacter = supportMaxSelectPerCharacter;
            Raw = raw;
        }
    }

    /// <summary>메타 강화 1종. `meta.json.upgrades`</summary>
    public sealed class MetaUpgradeDef
    {
        public string Id { get; }
        public string Name { get; }
        public string Stat { get; }
        public int MaxLevel { get; }
        public double ValuePerLevel { get; }
        public IReadOnlyList<int> Costs { get; }

        public MetaUpgradeDef(string id, string name, string stat, int maxLevel,
                              double valuePerLevel, IReadOnlyList<int> costs)
        {
            Id = id; Name = name; Stat = stat;
            MaxLevel = maxLevel; ValuePerLevel = valuePerLevel; Costs = costs;
        }

        public override string ToString() => $"{Id} x{MaxLevel}";
    }

    /// <summary>
    /// `meta.json` — <b>런 사이 영구 강화.</b>
    /// </summary>
    /// <remarks>
    /// ⚠️ <see cref="EconomyDef"/> 와 <b>화폐가 다르다.</b> 런 재화는 런 종료 시 소멸하고 이건 영구다.
    /// 둘을 섞으면 "런 안에서 잘하기"와 "런을 반복하기"가 같은 자원을 두고 경쟁해
    /// 밸런스 해석이 불가능해진다 — 타입을 나눠 둔 것이 그 계약이다.
    /// </remarks>
    public sealed class MetaDef
    {
        public int EarnPerRoundCleared { get; }
        public int EarnOnRunClear { get; }
        public IReadOnlyList<MetaUpgradeDef> Upgrades { get; }
        public JObject Raw { get; }

        public MetaDef(int earnPerRoundCleared, int earnOnRunClear,
                       IReadOnlyList<MetaUpgradeDef> upgrades, JObject raw)
        {
            EarnPerRoundCleared = earnPerRoundCleared;
            EarnOnRunClear = earnOnRunClear;
            Upgrades = upgrades;
            Raw = raw;
        }
    }
}
