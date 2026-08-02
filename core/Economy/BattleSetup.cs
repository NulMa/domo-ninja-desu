// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System.Collections.Generic;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Skills;

namespace DomoNinja.Core.Economy
{
    /// <summary>
    /// 런 상태와 관문 구성을 <b>전투에 올릴 유닛 목록</b>으로 바꾼다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 슬롯 인덱스(<see cref="Unit.Id"/>)를 여기서 매긴다. <b>아군이 먼저, 적이 나중이다.</b>
    /// 이 순서가 매 틱 순회 순서이자 모든 동률 판정의 최종 타이브레이커라
    /// (`_schema` §7), 여기서 흔들리면 같은 시드가 다른 결과를 낸다.
    /// </para>
    /// <para>
    /// 스탯 계산 순서도 고정이다 — <b>스킬 → 메타를 전부 더한 뒤 한 번만 적용</b>한다.
    /// 메타를 따로 곱하면 메타 진행도가 오를수록 런 내부 빌드의 효과까지 같이 커져,
    /// 두 축을 분리해 읽으려던 설계(`08` §4.5)가 무너진다.
    /// </para>
    /// </remarks>
    public static class BattleSetup
    {
        /// <summary>
        /// 표준 배치의 열. 근접은 앞, 원거리는 뒤.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>시뮬 전용 규칙이다</b> (`D-46`). 실제 게임에서는 플레이어가 매 라운드 배치한다.
        /// 그래서 시뮬이 내는 `M4` 는 <b>"표준 배치 하의" 값</b>이고, 그 사실을 지표에 표기한다.
        /// 배치를 탐색 축에 넣으면 빌드 공간이 24칸 조합만큼 곱해져 전수 탐색이 불가능해진다.
        /// </remarks>
        private const int MeleeColumn = 3;
        private const int RangedColumn = 1;

        /// <summary>
        /// 배치 행 순서. <b>가운데부터 바깥으로</b>.
        /// </summary>
        /// <remarks>
        /// 적도 가운데 열에 몰려 있어(`encounters.json`) 가운데부터 채우면 교전이 빨리 붙는다.
        /// 위에서부터 채우면 y=0 에 선 유닛이 대각선으로 한참 걸어와 전투가 길어지고,
        /// 그건 `M5`(1런 3~5분)에 그대로 실린다.
        /// </remarks>
        private static readonly int[] RowOrder = { 2, 3, 1, 4, 0, 5 };

        /// <summary>한 라운드에 올릴 유닛 전부. <b>슬롯 인덱스 오름차순으로 돌려준다.</b></summary>
        public static List<Unit> Build(GameData data, RunState run, VariantDef variant, MetaProgress meta)
        {
            var units = new List<Unit>();

            int slot = 0;
            for (int i = 0; i < run.Deployed.Count; i++)
            {
                var entry = run.Deployed[i];

                // 죽은 캐릭터는 런 종료까지 돌아오지 않는다 (A6 — 부활 없음).
                if (!entry.IsAlive) continue;

                units.Add(BuildAlly(data, entry, meta, slot, RowOrder[slot % RowOrder.Length]));
                slot++;
            }

            foreach (var placement in variant.Units)
            {
                if (!data.EnemyTypes.TryGetValue(placement.Type, out var type)) continue;
                units.Add(BuildEnemy(type, slot++, placement.At));
            }

            return units;
        }

        private static Unit BuildAlly(GameData data, RosterEntry entry, MetaProgress meta, int slot, int row)
        {
            var character = data.FindCharacter(entry.CharacterId)!;
            var active = entry.ActiveSkillId == null ? null : data.FindSkill(entry.ActiveSkillId);

            var supports = new List<SkillDef>();
            foreach (string id in entry.SupportSkillIds)
            {
                var s = data.FindSkill(id);
                if (s != null) supports.Add(s);
            }

            var stats = SkillResolver.BuildStats(character, active, supports);

            // 스킬이 만든 증감분에 메타를 더한 뒤 한 번만 적용한다.
            int maxHp = ApplyMeta(stats.Hp, meta, StatKey.Hp);
            int column = stats.Range <= 1 ? MeleeColumn : RangedColumn;

            var unit = new Unit(
                slot, Team.Ally, character.Id,
                maxHp,
                ApplyMeta(stats.Attack, meta, StatKey.Attack),
                ApplyMeta(stats.AttackInterval, meta, StatKey.AttackInterval),
                stats.Range,
                ApplyMeta(stats.MoveInterval, meta, StatKey.MoveInterval),
                new Coord(column, row))
            {
                DamageTakenDeltaPermille = stats.DamageTakenDeltaPermille,
                Loadout = Loadout.Build(active, supports),
            };

            // ★ 현재 HP 는 라운드를 넘어 누적된다 (A-6). 최대 체력이 메타로 늘었다고
            //   지금 체력까지 따라 오르지는 않는다 — 그러면 강화가 회복 수단이 된다.
            unit.Hp = entry.Hp < maxHp ? entry.Hp : maxHp;
            entry.MaxHp = maxHp;

            return unit;
        }

        private static Unit BuildEnemy(EnemyTypeDef type, int slot, Coord at) =>
            new Unit(slot, Team.Enemy, type.Type, type.Hp, type.Attack, type.AttackInterval,
                     type.Range, type.MoveInterval ?? 1, at, type.Immobile);

        private static int ApplyMeta(int value, MetaProgress meta, StatKey stat)
        {
            var sum = new ModifierSum();
            sum.AddDeltaPermille(meta.DeltaPermilleFor(stat));

            int result = sum.ApplyTo(value);

            // 간격이 0 이 되면 매 틱 행동하게 된다. 메타 강화가 -12%·-15% 라 지금 값으로는
            // 도달하지 않지만, 최적화기가 값을 올리면 도달할 수 있는 자리다.
            if ((stat == StatKey.AttackInterval || stat == StatKey.MoveInterval) && result < 1) result = 1;

            return result;
        }
    }
}
