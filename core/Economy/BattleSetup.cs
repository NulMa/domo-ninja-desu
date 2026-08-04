// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System;
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
        /// 표준 배치의 앞열. 여기서부터 <b>사거리 짧은 순으로</b> 뒤로 물린다 (`08` §6.1).
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>시뮬 전용 규칙이다</b> (`D-46`). 실제 게임에서는 플레이어가 매 라운드 배치한다.
        /// 그래서 시뮬이 내는 `M4` 는 <b>"표준 배치 하의" 값</b>이고, 그 사실을 지표에 표기한다.
        /// 배치를 탐색 축에 넣으면 24칸에 3명 = 12,144 배치가 곱해져 전수 탐색이 붕괴한다.
        ///
        /// ★ 처음엔 근접/원거리 2단계로 갈랐는데 스펙은 <b>"사거리 짧은 순"</b> 이라고 적어뒀다.
        /// 사거리 3·4·5 가 같은 열에 서면 그 셋의 차이가 배치에 반영되지 않는다.
        /// `M4` 를 *"표준 배치 하의 정확값"* 이라고 말하려면 <b>그 표준이 스펙과 같아야 한다.</b>
        /// </remarks>
        private const int FrontColumn = 3;

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
        /// <param name="allyPlacement">
        /// 플레이어가 고른 아군 좌표 (캐릭터 id → 좌표). <c>null</c> 이면 <b>표준 배치</b>로 떨어진다.
        /// </param>
        /// <remarks>
        /// ★ <b>표준 배치를 지우지 않고 폴백으로 남긴다.</b> <c>sim</c> 은 4,320 빌드를
        /// 사람 없이 돌려야 하고, `M4` 를 *"표준 배치 하의 정확값"* 이라고 말하려면
        /// <b>그 표준이 계속 존재해야 한다</b> (`08` §6.1).
        /// </remarks>
        public static List<Unit> Build(GameData data, RunState run, VariantDef variant, MetaProgress meta,
                                       IReadOnlyDictionary<string, Coord>? allyPlacement = null)
        {
            var units = new List<Unit>();

            int slot = 0;
            for (int i = 0; i < run.Deployed.Count; i++)
            {
                var entry = run.Deployed[i];

                // 죽은 캐릭터는 런 종료까지 돌아오지 않는다 (A6 — 부활 없음).
                if (!entry.IsAlive) continue;

                units.Add(BuildAlly(data, run, entry, meta, slot));
                slot++;
            }

            // ★ 자리는 유닛을 다 만든 뒤에 정한다 — 사거리를 알아야 앞뒤가 갈리는데,
            //   사거리는 스킬·보조를 다 반영한 뒤에야 확정된다(C4-A 저격은 5 → 7).
            //   슬롯 인덱스는 건드리지 않는다. 위치만 바뀌지 순회 순서는 로스터 순서 그대로다.
            if (allyPlacement != null) ApplyPlayerPlacement(units, allyPlacement);
            else PlaceAllies(units);

            foreach (var placement in variant.Units)
            {
                if (!data.EnemyTypes.TryGetValue(placement.Type, out var type)) continue;
                units.Add(BuildEnemy(type, slot++, placement.At));
            }

            return units;
        }

        /// <summary>
        /// 플레이어가 고른 좌표를 그대로 놓는다 (`D-53` 매 라운드 재배치 · `D-75`).
        /// </summary>
        /// <exception cref="ArgumentException">
        /// 빠진 유닛 · 진영 밖 · 겹침 중 하나라도 있으면.
        /// </exception>
        /// <remarks>
        /// <para>
        /// ★ <b>View 를 신뢰하지 않고 여기서 검증한다.</b> 이 프로젝트가 데이터 로더(`R01`~`R24`)와
        /// 최적화기 덮어쓰기에서 지켜온 것과 같은 규칙이다 — <b>조용히 보정하면 잘못된 배치가
        /// 정상값처럼 전투에 실리고, 그건 결과 숫자로는 구분되지 않는다.</b>
        /// UI 버그를 UI 가 아니라 밸런스 리포트에서 발견하게 되는 게 최악이다.
        /// </para>
        /// <para>
        /// ★ <b>전원을 요구한다. 일부만 받지 않는다.</b>
        /// <see cref="PlaceAllies"/> 는 <b>전체를 사거리순으로 정렬해</b> 열을 배정하므로,
        /// 빈 자리만 표준배치로 메우면 <b>표준도 플레이어 의도도 아닌 제3의 배치</b>가 되고
        /// 열이 겹칠 수도 있다. *"일부만 놓았다"* 는 상태는 정의할 수 없다 —
        /// 플레이어가 손을 안 댔어도 유닛은 어딘가에 서 있으므로 <b>UI 가 전원 좌표를 안다.</b>
        /// </para>
        /// </remarks>
        private static void ApplyPlayerPlacement(List<Unit> units,
                                                 IReadOnlyDictionary<string, Coord> placement)
        {
            var taken = new Dictionary<int, string>();

            foreach (var u in units)
            {
                if (u.Team != Team.Ally) continue;

                if (!placement.TryGetValue(u.TypeId, out var at))
                    throw new ArgumentException(
                        $"배치 좌표가 없는 아군이 있다: {u.TypeId}. " +
                        "전원 좌표를 넘겨야 한다 — 일부만 놓은 상태는 정의되지 않는다 (`D-75`).");

                if (!at.IsAllyZone)
                    throw new ArgumentException(
                        $"{u.TypeId} 의 좌표 {at} 가 아군 진영 밖이다 " +
                        $"(x 는 0~{Coord.AllyMaxX}, y 는 0~{Coord.BoardHeight - 1}).");

                if (taken.TryGetValue(at.OrderKey, out string? other))
                    throw new ArgumentException(
                        $"{u.TypeId} 와 {other} 가 같은 칸 {at} 에 배치됐다. " +
                        "겹치면 나중 유닛이 보드에 안 올라가고 전투가 조용히 한 명 적은 채로 돈다.");

                taken[at.OrderKey] = u.TypeId;
                u.At = at;
            }
        }

        /// <summary>
        /// 표준 배치 — <b>사거리 짧은 순으로 앞열부터</b>. 동률은 슬롯 인덱스로 끊는다.
        /// </summary>
        /// <remarks>
        /// 동률 처리가 없으면 같은 사거리 둘의 앞뒤가 정렬 구현에 따라 갈린다.
        /// <c>List.Sort</c> 는 불안정 정렬이라 <b>같은 입력에서도 순서가 뒤집힐 수 있다</b> —
        /// 여기서 결정론이 깨지면 그 위의 밸런스 수치가 전부 의미를 잃는다.
        /// 그래서 비교자가 사거리와 id 를 둘 다 본다.
        /// </remarks>
        private static void PlaceAllies(List<Unit> allies)
        {
            var order = new List<Unit>(allies);
            order.Sort((a, b) =>
            {
                int byRange = a.Range.CompareTo(b.Range);
                return byRange != 0 ? byRange : a.Id.CompareTo(b.Id);
            });

            for (int i = 0; i < order.Count; i++)
            {
                // 앞열(x=3)부터 뒤로 물린다. 진영이 4열이라 3명이면 3·2·1 을 쓴다.
                int column = FrontColumn - i;
                if (column < 0) column = 0;

                order[i].At = new Coord(column, RowOrder[i % RowOrder.Length]);
            }
        }

        private static Unit BuildAlly(GameData data, RunState run, RosterEntry entry, MetaProgress meta, int slot)
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

            // 스킬이 만든 증감분에 메타와 아이템을 더한 뒤 한 번만 적용한다.
            int maxHp = ApplyExtras(stats.Hp, data, run, entry, meta, StatKey.Hp);
            // 자리는 PlaceAllies 가 나중에 정한다. 여기서는 유닛만 만든다.

            var unit = new Unit(
                slot, Team.Ally, character.Id,
                maxHp,
                ApplyExtras(stats.Attack, data, run, entry, meta, StatKey.Attack),
                ApplyExtras(stats.AttackInterval, data, run, entry, meta, StatKey.AttackInterval),
                stats.Range,
                ApplyExtras(stats.MoveInterval, data, run, entry, meta, StatKey.MoveInterval),
                Coord.Origin)
            {
                DamageTakenDeltaPermille = stats.DamageTakenDeltaPermille,
                Loadout = Loadout.Build(active, supports),

                // 조건부 강화만 스탯에 접히지 않고 유닛을 따라 전투로 들어간다 —
                // 세 조건이 전부 전투 중에 뒤집혀서 시작 시점에 값을 정할 수 없다.
                ConditionalBoosts = ItemEffects.CollectConditional(data, run, entry),
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

        /// <summary>
        /// 메타 강화와 아이템을 <b>같은 통에 더한 뒤 한 번만</b> 적용한다.
        /// </summary>
        /// <remarks>
        /// 축이 셋(스킬·메타·아이템)인데 절삭 지점은 하나여야 한다.
        /// 축마다 따로 곱하면 절삭이 세 번 일어나고 <b>적용 순서에 따라 결과가 달라진다</b> —
        /// 합연산 규칙(`_schema` §8)이 막으려던 바로 그것이다.
        /// 스킬 몫은 <see cref="SkillResolver.BuildStats"/> 가 이미 접어 넣은 값으로 들어온다.
        /// </remarks>
        private static int ApplyExtras(int value, GameData data, RunState run, RosterEntry entry,
                                       MetaProgress meta, StatKey stat)
        {
            var sum = new ModifierSum();
            sum.AddDeltaPermille(meta.DeltaPermilleFor(stat));
            sum.AddDeltaPermille(ItemEffects.DeltaPermilleFor(data, run, entry, stat));

            int result = sum.ApplyTo(value);

            // 간격이 0 이 되면 매 틱 행동하게 된다. 메타 강화가 -12%·-15% 라 지금 값으로는
            // 도달하지 않지만, 최적화기가 값을 올리면 도달할 수 있는 자리다.
            if ((stat == StatKey.AttackInterval || stat == StatKey.MoveInterval) && result < 1) result = 1;

            return result;
        }
    }
}
