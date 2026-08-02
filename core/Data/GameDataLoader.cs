// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System;
using System.Collections.Generic;
using DomoNinja.Core.Domain;
using Newtonsoft.Json.Linq;

namespace DomoNinja.Core.Data
{
    /// <summary>
    /// `/data` 의 JSON 5개를 읽어 <see cref="GameData"/> 로 만든다. <b>검증에 실패하면 던진다.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>타입 자동 역직렬화(<c>ToObject&lt;T&gt;</c>)를 쓰지 않는다</b> (13 결정).
    /// 자동 역직렬화는 JSON 에 없는 필드를 <b>조용히 기본값으로 채운다.</b>
    /// `hp` 가 빠지면 0 이 되고, 0 인 유닛은 전투 첫 틱에 죽는다 —
    /// 그러면 "데이터가 빠졌다"가 아니라 "밸런스가 이상하다"로 보인다.
    /// 최적화기가 값을 써넣는 파일들이라 그런 사고가 실제로 날 수 있는 구조다.
    /// → <see cref="JObject"/> 로 읽고 필드를 <b>하나씩 명시적으로 꺼내며</b>, 없으면 오류로 남긴다.
    /// </para>
    /// <para>
    /// ★ <b>파일 경로가 아니라 문자열을 받는다.</b>
    /// WebGL 에는 파일 시스템이 없다 — Unity 는 TextAsset/UnityWebRequest 로 읽어 문자열을 넘긴다.
    /// core 가 <c>File.ReadAllText</c> 를 부르면 dotnet(sim·tests)에서만 되고 브라우저에서 깨진다.
    /// 그 차이는 WebGL 빌드를 실제로 돌리기 전까지 드러나지 않는다.
    /// </para>
    /// </remarks>
    public static class GameDataLoader
    {
        // 템플릿 5종. `_schema` §3 — "새 템플릿을 늘리지 않는다"
        private static readonly string[] Templates =
            { "stat_mult", "aoe", "conditional", "status", "targeting" };

        // conditional 안에 중첩되는 원자 동작 5종 (`_schema` §3)
        private static readonly string[] AtomicActions =
            { "extra_attack", "counter", "recast", "self_damage", "heal" };

        private static readonly string[] TriggerTypes =
            { "on_kill", "on_damaged", "on_dodge", "on_hit", "hp_below", "every_n_tick" };

        private static readonly string[] StatusKinds =
            { "weaken", "dot_ramping", "invulnerable_first_hit", "regen", "slow", "root", "shield", "taunt" };

        private static readonly string[] TargetingPriorities =
            { "nearest", "lowest_hp", "farthest" };

        private static readonly string[] Roles =
            { "퓨어딜러", "브루저", "탱커", "힐러", "디버퍼", "버퍼" };

        private static readonly string[] SupportSlots = { "공격", "생존", "팀" };

        /// <exception cref="DataValidationException">계약 위반이 하나라도 있으면.</exception>
        public static GameData Load(
            string charactersJson,
            string skillsJson,
            string encountersJson,
            string economyJson,
            string metaJson)
        {
            var errors = new List<ValidationError>();

            JObject characters = Parse(charactersJson, "characters.json", errors);
            JObject skills = Parse(skillsJson, "skills.json", errors);
            JObject encounters = Parse(encountersJson, "encounters.json", errors);
            JObject economy = Parse(economyJson, "economy.json", errors);
            JObject meta = Parse(metaJson, "meta.json", errors);

            // 파싱 자체가 깨졌으면 그 위의 규칙 검사는 의미 없는 오류를 쏟아낸다.
            if (errors.Count > 0) throw new DataValidationException(errors);

            var economyDef = ReadEconomy(economy, errors);
            var characterList = ReadCharacters(characters, errors);
            var (activeSkills, supportSkills) = ReadSkills(skills, errors);
            var enemyTypes = ReadEnemyTypes(encounters, errors);
            var rounds = ReadRounds(encounters, errors);
            var metaDef = ReadMeta(meta, errors);

            Validate(errors, characterList, activeSkills, supportSkills, enemyTypes, rounds, economyDef, characters);

            if (errors.Count > 0) throw new DataValidationException(errors);

            return new GameData(characterList, activeSkills, supportSkills, enemyTypes, rounds, economyDef, metaDef);
        }

        // ────────────────────────────── 읽기

        private static JObject Parse(string json, string where, List<ValidationError> errors)
        {
            try
            {
                return JObject.Parse(json);
            }
            catch (Exception ex)
            {
                errors.Add(new ValidationError("PARSE", where, ex.Message));
                return new JObject();
            }
        }

        private static List<CharacterDef> ReadCharacters(JObject root, List<ValidationError> errors)
        {
            var list = new List<CharacterDef>();
            var arr = root["characters"] as JArray;
            if (arr == null)
            {
                errors.Add(new ValidationError("PARSE", "characters.json", "`characters` 배열이 없다"));
                return list;
            }

            foreach (var t in arr)
            {
                if (!(t is JObject o)) continue;
                string id = Str(o, "id", "characters.json", errors) ?? "?";
                string where = $"characters.json {id}";

                var skillIds = new List<string>();
                if (o["skills"] is JArray sa)
                {
                    foreach (var s in sa) skillIds.Add(s.ToString());
                }
                else
                {
                    errors.Add(new ValidationError("PARSE", where, "`skills` 배열이 없다"));
                }

                list.Add(new CharacterDef(
                    id,
                    Str(o, "sprite", where, errors) ?? "",
                    Str(o, "name", where, errors) ?? "",
                    Int(o, "hp", where, errors),
                    Int(o, "attack", where, errors),
                    Int(o, "attackInterval", where, errors),
                    Int(o, "range", where, errors),
                    Int(o, "moveInterval", where, errors),
                    skillIds));
            }

            return list;
        }

        private static (List<SkillDef> active, List<SkillDef> support) ReadSkills(
            JObject root, List<ValidationError> errors)
        {
            return (ReadSkillArray(root, "skills", isSupport: false, errors),
                    ReadSkillArray(root, "supportSkills", isSupport: true, errors));
        }

        private static List<SkillDef> ReadSkillArray(
            JObject root, string key, bool isSupport, List<ValidationError> errors)
        {
            var list = new List<SkillDef>();
            var arr = root[key] as JArray;
            if (arr == null)
            {
                errors.Add(new ValidationError("PARSE", "skills.json", $"`{key}` 배열이 없다"));
                return list;
            }

            foreach (var t in arr)
            {
                if (!(t is JObject o)) continue;
                string id = Str(o, "id", "skills.json", errors) ?? "?";
                string where = $"skills.json {id}";

                var tags = new List<string>();
                if (o["tags"] is JArray ta)
                {
                    foreach (var x in ta) tags.Add(x.ToString());
                }

                var text = o["text"] as JObject;

                list.Add(new SkillDef(
                    id,
                    Str(o, "character", where, errors) ?? "",
                    Str(o, "name", where, errors) ?? "",
                    isSupport ? null : (string?)o["role"],
                    isSupport ? (string?)o["slot"] : null,
                    (string?)text?["gain"],
                    (string?)text?["cost"],
                    o["effects"] as JArray ?? new JArray(),
                    tags,
                    (string?)o["icon"]));
            }

            return list;
        }

        private static Dictionary<string, EnemyTypeDef> ReadEnemyTypes(JObject root, List<ValidationError> errors)
        {
            var dict = new Dictionary<string, EnemyTypeDef>();
            var types = root["enemyTypes"] as JObject;
            if (types == null)
            {
                errors.Add(new ValidationError("PARSE", "encounters.json", "`enemyTypes` 가 없다"));
                return dict;
            }

            foreach (var kv in types)
            {
                // `_note` `_movementNote` 처럼 밑줄로 시작하는 키는 문서용 주석이다.
                if (kv.Key.StartsWith("_", StringComparison.Ordinal)) continue;
                if (!(kv.Value is JObject o)) continue;

                string where = $"encounters.json {kv.Key}";
                int? moveInterval = o["moveInterval"] != null ? Int(o, "moveInterval", where, errors) : (int?)null;

                dict[kv.Key] = new EnemyTypeDef(
                    kv.Key,
                    Str(o, "sprite", where, errors) ?? "",
                    Int(o, "hp", where, errors),
                    Int(o, "attack", where, errors),
                    Int(o, "attackInterval", where, errors),
                    Int(o, "range", where, errors),
                    moveInterval,
                    (bool?)o["immobile"] ?? false,
                    (bool?)o["isBoss"] ?? false);
            }

            return dict;
        }

        private static List<RoundDef> ReadRounds(JObject root, List<ValidationError> errors)
        {
            var list = new List<RoundDef>();
            var arr = root["rounds"] as JArray;
            if (arr == null)
            {
                errors.Add(new ValidationError("PARSE", "encounters.json", "`rounds` 배열이 없다"));
                return list;
            }

            foreach (var t in arr)
            {
                if (!(t is JObject o)) continue;
                int round = Int(o, "round", "encounters.json", errors);
                var variants = new List<VariantDef>();

                if (o["variants"] is JArray va)
                {
                    foreach (var vt in va)
                    {
                        if (!(vt is JObject vo)) continue;
                        string vid = Str(vo, "id", $"encounters.json R{round}", errors) ?? "?";
                        var units = new List<EnemyPlacement>();

                        if (vo["units"] is JArray ua)
                        {
                            foreach (var ut in ua)
                            {
                                if (!(ut is JObject uo)) continue;
                                units.Add(new EnemyPlacement(
                                    (string?)uo["type"] ?? "?",
                                    new Coord(Int(uo, "x", $"encounters.json {vid}", errors),
                                              Int(uo, "y", $"encounters.json {vid}", errors))));
                            }
                        }

                        variants.Add(new VariantDef(vid, units));
                    }
                }

                list.Add(new RoundDef(round, (string?)o["axisTested"] ?? "", variants));
            }

            return list;
        }

        private static EconomyDef ReadEconomy(JObject root, List<ValidationError> errors)
        {
            const string W = "economy.json";
            var run = root["run"] as JObject ?? new JObject();
            var roster = root["roster"] as JObject ?? new JObject();
            var perSide = (root["board"] as JObject)?["perSide"] as JObject ?? new JObject();
            var combat = root["combat"] as JObject ?? new JObject();
            var currency = root["currency"] as JObject ?? new JObject();
            var shop = root["shop"] as JObject ?? new JObject();
            var slots = shop["slots"] as JObject ?? new JObject();
            var support = shop["supportSkill"] as JObject ?? new JObject();

            return new EconomyDef(
                Int(run, "totalRounds", W, errors),
                Int(run, "lives", W, errors),
                Int(roster, "poolSize", W, errors),
                Int(roster, "deployCount", W, errors),
                Int(perSide, "cols", W, errors),
                Int(perSide, "rows", W, errors),
                Int(combat, "timeoutTicks", W, errors),
                Int(currency, "onWin", W, errors),
                Int(currency, "onLose", W, errors),
                Int(slots, "skill", W, errors),
                Int(slots, "item", W, errors),
                Int(shop, "rerollCost", W, errors),
                Int(support, "poolPerCharacter", W, errors),
                Int(support, "maxSelectPerCharacter", W, errors),
                root);
        }

        private static MetaDef ReadMeta(JObject root, List<ValidationError> errors)
        {
            const string W = "meta.json";
            var currency = root["currency"] as JObject ?? new JObject();
            var upgrades = new List<MetaUpgradeDef>();

            if (root["upgrades"] is JArray arr)
            {
                foreach (var t in arr)
                {
                    if (!(t is JObject o)) continue;
                    string id = Str(o, "id", W, errors) ?? "?";
                    var costs = new List<int>();
                    if (o["costs"] is JArray ca)
                    {
                        foreach (var c in ca) costs.Add((int?)c ?? 0);
                    }

                    upgrades.Add(new MetaUpgradeDef(
                        id,
                        Str(o, "name", $"{W} {id}", errors) ?? "",
                        Str(o, "stat", $"{W} {id}", errors) ?? "",
                        Int(o, "maxLevel", $"{W} {id}", errors),
                        (double?)o["valuePerLevel"] ?? 0d,
                        costs));
                }
            }
            else
            {
                errors.Add(new ValidationError("PARSE", W, "`upgrades` 배열이 없다"));
            }

            return new MetaDef(
                Int(currency, "earnPerRoundCleared", W, errors),
                Int(currency, "earnOnRunClear", W, errors),
                upgrades,
                root);
        }

        private static string? Str(JObject o, string key, string where, List<ValidationError> errors)
        {
            var v = o[key];
            if (v == null || v.Type == JTokenType.Null)
            {
                errors.Add(new ValidationError("PARSE", where, $"`{key}` 가 없다"));
                return null;
            }
            return v.ToString();
        }

        private static int Int(JObject o, string key, string where, List<ValidationError> errors)
        {
            var v = o[key];
            if (v == null || v.Type == JTokenType.Null)
            {
                errors.Add(new ValidationError("PARSE", where, $"`{key}` 가 없다"));
                return 0;
            }
            return (int)v;
        }

        // ────────────────────────────── 검증 (`_schema` §6)

        private static void Validate(
            List<ValidationError> e,
            List<CharacterDef> characters,
            List<SkillDef> active,
            List<SkillDef> support,
            Dictionary<string, EnemyTypeDef> enemyTypes,
            List<RoundDef> rounds,
            EconomyDef economy,
            JObject charactersRaw)
        {
            // R01 — 캐릭터 수 == economy.roster.poolSize
            if (characters.Count != economy.RosterPoolSize)
                e.Add(new ValidationError("R01", "characters.json",
                    $"캐릭터 {characters.Count}명인데 roster.poolSize 는 {economy.RosterPoolSize}"));

            foreach (var c in characters)
            {
                // R02 — 모든 캐릭터가 정확히 2개의 액티브 스킬
                int owned = CountBy(active, s => s.CharacterId == c.Id);
                if (owned != 2)
                    e.Add(new ValidationError("R02", $"characters.json {c.Id}", $"액티브 스킬이 {owned}개 (2개여야 한다)"));

                // R04 — 캐릭터당 보조 정확히 poolPerCharacter(3)개
                var mine = new List<SkillDef>();
                foreach (var s in support) if (s.CharacterId == c.Id) mine.Add(s);

                if (mine.Count != economy.SupportPoolPerCharacter)
                    e.Add(new ValidationError("R04", $"skills.json {c.Id}",
                        $"보조 스킬이 {mine.Count}개 (poolPerCharacter={economy.SupportPoolPerCharacter})"));

                // R05 — 보조 3종이 slot 을 공격·생존·팀 하나씩 (§10 3자리 문법)
                foreach (string slot in SupportSlots)
                {
                    int n = CountBy(mine, s => s.Slot == slot);
                    if (n != 1)
                        e.Add(new ValidationError("R05", $"skills.json {c.Id}",
                            $"`{slot}` 자리 보조가 {n}개 (1개여야 한다)"));
                }
            }

            // R03 — 모든 스킬에 text.gain 과 text.cost 가 둘 다 (M3b: 한쪽이 그냥 좋은 스킬 금지)
            foreach (var s in Concat(active, support))
            {
                if (string.IsNullOrEmpty(s.TextGain) || string.IsNullOrEmpty(s.TextCost))
                    e.Add(new ValidationError("R03", $"skills.json {s.Id}",
                        "text.gain 과 text.cost 가 둘 다 있어야 한다 — 얻는 것만 있는 스킬은 배타 선택을 가짜로 만든다"));
            }

            foreach (var s in active)
            {
                // R06 — 액티브에 role 이 있고 목록 안에 있다
                if (s.Role == null)
                    e.Add(new ValidationError("R06", $"skills.json {s.Id}", "`role` 이 없다"));
                else if (Array.IndexOf(Roles, s.Role) < 0)
                    e.Add(new ValidationError("R06", $"skills.json {s.Id}", $"알 수 없는 role `{s.Role}`"));

                // R09 — 액티브에 upgrades 가 남아 있으면 v0.6 마이그레이션 누락이다
                foreach (var t in s.Effects)
                {
                    if (t is JObject eo && eo["upgrades"] != null)
                        e.Add(new ValidationError("R09", $"skills.json {s.Id}",
                            "`upgrades` 가 남아 있다 — v0.6 에서 폐기됐다(보조 스킬로 통합)"));
                }
            }

            // R07 · R08 · R10 · R11 — 효과 트리를 훑는다
            foreach (var s in Concat(active, support))
                foreach (var t in s.Effects)
                    ValidateEffect(t, s.Id, topLevel: true, e);

            // R12 — rounds 가 1~totalRounds 를 빠짐없이 덮는다
            var seen = new HashSet<int>();
            foreach (var r in rounds) seen.Add(r.Round);
            for (int i = 1; i <= economy.TotalRounds; i++)
            {
                if (!seen.Contains(i))
                    e.Add(new ValidationError("R12", "encounters.json", $"라운드 {i} 구성이 없다"));
            }

            int minEnemyX = economy.BoardCols;
            int maxEnemyX = economy.BoardCols * 2 - 1;
            int maxY = economy.BoardRows - 1;
            int sideCells = economy.BoardCols * economy.BoardRows;

            foreach (var r in rounds)
            {
                foreach (var v in r.Variants)
                {
                    var used = new HashSet<int>();
                    foreach (var u in v.Units)
                    {
                        // R13 — 모든 type 이 enemyTypes 에 존재한다
                        if (!enemyTypes.ContainsKey(u.Type))
                            e.Add(new ValidationError("R13", $"encounters.json {v.Id}",
                                $"`{u.Type}` 이 enemyTypes 에 없다"));

                        // R14 — 적 좌표가 적 진영 안이다
                        if (u.At.X < minEnemyX || u.At.X > maxEnemyX || u.At.Y < 0 || u.At.Y > maxY)
                            e.Add(new ValidationError("R14", $"encounters.json {v.Id}",
                                $"{u.Type}{u.At} 가 적 진영(x {minEnemyX}~{maxEnemyX}, y 0~{maxY}) 밖이다"));

                        // R15 — 한 variant 안에서 좌표가 중복되지 않는다
                        if (!used.Add(u.At.OrderKey))
                            e.Add(new ValidationError("R15", $"encounters.json {v.Id}",
                                $"{u.At} 에 두 체 이상이 겹친다"));
                    }

                    // R16 — 유닛 수가 자기 진영 칸 수를 넘지 않는다
                    if (v.Units.Count > sideCells)
                        e.Add(new ValidationError("R16", $"encounters.json {v.Id}",
                            $"{v.Units.Count}체 > 진영 칸 수 {sideCells}"));
                }
            }

            // R17 — supportSkills 수 == 캐릭터 수 × poolPerCharacter
            int expected = characters.Count * economy.SupportPoolPerCharacter;
            if (support.Count != expected)
                e.Add(new ValidationError("R17", "skills.json",
                    $"보조 스킬 {support.Count}개 (기대값 {characters.Count}×{economy.SupportPoolPerCharacter}={expected})"));

            // R18 — 보조 스킬의 character 가 characters.json 에 존재한다
            foreach (var s in support)
            {
                bool found = false;
                foreach (var c in characters) if (c.Id == s.CharacterId) { found = true; break; }
                if (!found)
                    e.Add(new ValidationError("R18", $"skills.json {s.Id}",
                        $"`character: {s.CharacterId}` 가 characters.json 에 없다"));
            }

            // R19 — moveInterval >= 1. 0 이면 무한 이동이 된다. immobile 인 적만 예외
            foreach (var c in characters)
            {
                if (c.MoveInterval < 1)
                    e.Add(new ValidationError("R19", $"characters.json {c.Id}",
                        $"moveInterval={c.MoveInterval} — 1 이상이어야 한다"));
            }
            foreach (var kv in enemyTypes)
            {
                var t = kv.Value;
                if (t.Immobile) continue;
                if (t.MoveInterval == null)
                    e.Add(new ValidationError("R19", $"encounters.json {t.Type}",
                        "moveInterval 이 없다 — immobile 이 아닌 적은 반드시 갖는다"));
                else if (t.MoveInterval.Value < 1)
                    e.Add(new ValidationError("R19", $"encounters.json {t.Type}",
                        $"moveInterval={t.MoveInterval} — 1 이상이어야 한다"));
            }

            // R20 — immobile 은 적 전용이다 (A5 — 고정포대는 플레이어 빌드 공간에 없다)
            if (charactersRaw["characters"] is JArray ca)
            {
                foreach (var t in ca)
                {
                    if (t is JObject o && (bool?)o["immobile"] == true)
                        e.Add(new ValidationError("R20", $"characters.json {(string?)o["id"]}",
                            "`immobile` 은 enemyTypes 에만 쓴다 — 고정포대는 적 전용이다(A5)"));
                }
            }

            // R21(추가) — `_schema` §6 에 없는 우리 규칙이다.
            // Coord 가 보드 크기를 const 로 들고 있어서(정수 연산·배열 인덱싱에 필요) economy.json 과
            // 어긋날 수 있다. 어긋나면 "적 좌표가 진영 밖"이 아니라 배치·이동이 조용히 틀린다.
            if (economy.BoardCols * 2 != Coord.BoardWidth || economy.BoardRows != Coord.BoardHeight)
                e.Add(new ValidationError("R21", "economy.json",
                    $"보드 크기가 코드 상수와 다르다 — json {economy.BoardCols * 2}x{economy.BoardRows} / Coord {Coord.BoardWidth}x{Coord.BoardHeight}"));

            ValidateIcons(e, characters, active, support);
        }

        /// <summary>
        /// `R22`(추가) — <c>icon</c> 이 있으면 경로 규약과 일치해야 한다.
        /// </summary>
        /// <remarks>
        /// ★ <b>없는 것은 통과시킨다.</b> 아이콘은 아트 작업과 함께 들어오므로 아직 비어 있는 게 정상이다.
        /// 여기서 필수로 걸면 아직 그림이 없는 스킬 때문에 로드 자체가 막히고,
        /// 그러면 아이콘이 다 나올 때까지 시뮬을 못 돌린다.
        ///
        /// 형식이 아니라 <b>값 자체를 대조</b>하는 이유 — 경로가 <c>id</c> 와 <c>name</c> 을 중복해서 담기 때문에
        /// 이름을 바꾸고 파일명을 안 바꾸면 <b>아이콘만 조용히 안 뜬다.</b>
        /// 정규식으로 모양만 보면 그 어긋남을 못 잡는다. 기대 경로를 직접 만들어 비교한다.
        /// </remarks>
        private static void ValidateIcons(List<ValidationError> e, List<CharacterDef> characters,
                                          List<SkillDef> active, List<SkillDef> support)
        {
            foreach (var skill in Concat(active, support))
            {
                if (skill.Icon == null) continue;

                CharacterDef? owner = null;
                foreach (var c in characters) if (c.Id == skill.CharacterId) { owner = c; break; }
                if (owner == null) continue;   // R18 이 이미 잡는다

                // characters.json 의 "Actor/Character/Samurai" 에서 마지막 조각만 쓴다.
                string folder = owner.Sprite;
                int slash = folder.LastIndexOf('/');
                if (slash >= 0) folder = folder.Substring(slash + 1);

                string expected = $"Skill/{folder}/{(skill.IsSupport ? "Support" : "Main")}/{skill.Id}_{skill.Name}";

                if (skill.Icon != expected)
                    e.Add(new ValidationError("R22", $"skills.json {skill.Id}",
                        $"icon 이 규약과 다르다 — 기대 `{expected}` / 실제 `{skill.Icon}`"));
            }
        }

        /// <summary>효과 하나를 검사한다. `conditional` 안에 중첩된 효과까지 재귀로 내려간다.</summary>
        private static void ValidateEffect(JToken token, string skillId, bool topLevel, List<ValidationError> e)
        {
            if (!(token is JObject o)) return;
            string where = $"skills.json {skillId}";
            string? template = (string?)o["template"];

            // R10 — template 이 5종(중첩이면 + 원자 동작 5종) 안에 있다
            if (template == null)
            {
                e.Add(new ValidationError("R10", where, "`template` 이 없다"));
            }
            else
            {
                bool ok = Array.IndexOf(Templates, template) >= 0
                          || (!topLevel && Array.IndexOf(AtomicActions, template) >= 0);
                if (!ok)
                    e.Add(new ValidationError("R10", where,
                        $"알 수 없는 template `{template}` — 템플릿은 5종에서 늘리지 않는다"));
            }

            // R11 — trigger.type / status.kind / targeting.priority 가 목록 안에 있다
            if (o["trigger"] is JObject trig)
            {
                string? tt = (string?)trig["type"];
                if (tt == null || Array.IndexOf(TriggerTypes, tt) < 0)
                    e.Add(new ValidationError("R11", where, $"알 수 없는 trigger.type `{tt}`"));
            }

            if (template == "status")
            {
                string? kind = (string?)o["kind"];
                if (kind == null || Array.IndexOf(StatusKinds, kind) < 0)
                    e.Add(new ValidationError("R11", where, $"알 수 없는 status.kind `{kind}`"));

                // R07 — overflowToHp 가 있으면 maxPermille 도 있어야 한다
                //       상한이 없으면 "초과분"이 정의되지 않는다
                if (kind == "shield" && o["overflowToHp"] != null && o["maxPermille"] == null)
                    e.Add(new ValidationError("R07", where,
                        "shield 에 overflowToHp 가 있는데 maxPermille 이 없다 — 상한 없이 초과분을 정의할 수 없다"));
            }

            if (template == "targeting" && o["priority"] != null)
            {
                string? p = (string?)o["priority"];
                if (p == null || Array.IndexOf(TargetingPriorities, p) < 0)
                    e.Add(new ValidationError("R11", where, $"알 수 없는 targeting.priority `{p}`"));
            }

            // R08 — heal 은 permille 과 fromDamagePermille 중 정확히 하나만 갖는다
            if (template == "heal")
            {
                bool a = o["permille"] != null;
                bool b = o["fromDamagePermille"] != null;
                if (a == b)
                    e.Add(new ValidationError("R08", where,
                        a ? "heal 에 permille 과 fromDamagePermille 이 둘 다 있다"
                          : "heal 에 permille 도 fromDamagePermille 도 없다"));
            }

            if (o["effect"] != null) ValidateEffect(o["effect"]!, skillId, topLevel: false, e);
        }

        // netstandard2.1 + LINQ 없이. 전투 코어가 LINQ 할당을 피하는 것과 같은 결이다.
        private static int CountBy(List<SkillDef> list, Func<SkillDef, bool> pred)
        {
            int n = 0;
            foreach (var s in list) if (pred(s)) n++;
            return n;
        }

        private static IEnumerable<SkillDef> Concat(List<SkillDef> a, List<SkillDef> b)
        {
            foreach (var s in a) yield return s;
            foreach (var s in b) yield return s;
        }
    }
}
