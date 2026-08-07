// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System.Collections.Generic;
using DomoNinja.Core.Domain;

namespace DomoNinja.Core.Events
{
    /// <summary>
    /// 손으로 적은 가짜 전투 로그. <b>전투 코어(P2)가 없어도 View 를 짤 수 있게 하는 용도다.</b>
    /// </summary>
    /// <remarks>
    /// ★ 이건 시뮬레이터가 아니다. 전투 규칙을 흉내 내지 않는다.
    /// 흉내 냈다면 그 순간 P2 의 규칙이 두 군데 존재하게 되고, 팀원은 틀린 쪽을 기준으로 연출을 맞추게 된다.
    /// 여기 있는 건 <b>이벤트가 어떤 모양으로·어떤 순서로 오는가</b> 뿐이다. 수치에는 의미가 없다.
    ///
    /// <see cref="EventKind"/> 전 항목이 최소 1회씩 나온다 — 연출 분기를 미리 다 만들어 볼 수 있게 한 것이다.
    /// P2 완성 후 실제 로그로 갈아끼우면 이 파일은 지운다. (19 §5 "H1 지연" 대응)
    /// </remarks>
    public static class DummyBattleLog
    {
        private static int Key(int x, int y) => new Coord(x, y).OrderKey;

        /// <summary>재생 테스트용 전투 1회. 호출할 때마다 같은 내용을 돌려준다.</summary>
        public static BattleLog Create()
        {
            var units = new List<UnitSpec>
            {
                new UnitSpec(0, 0, "C1", 320, Key(2, 2)), // 근접 탱커
                new UnitSpec(1, 0, "C3", 180, Key(1, 1)), // 원거리
                new UnitSpec(2, 0, "C5", 210, Key(1, 4)), // 지원
                // 적 TypeId 는 encounters.json 의 type 을 그대로 쓴다 — 아군처럼 C+숫자가 아니다
                new UnitSpec(3, 1, "kappa", 260, Key(5, 2)),
                new UnitSpec(4, 1, "slime", 140, Key(6, 4)),
            };

            var e = new List<GameEvent>();

            // 액티브를 든 유닛을 전투 시작에 한 번 알린다 (Value = 0). 실제 발동은 아래 Value = 1 이다.
            e.Add(new GameEvent(EventKind.SkillCast, 0, 0, -1, 0));
            e.Add(new GameEvent(EventKind.SkillCast, 0, 2, -1, 0));

            // 접근 — 20틱 = 1초
            e.Add(new GameEvent(EventKind.Move, 10, 0, -1, Key(3, 2), Key(2, 2)));
            e.Add(new GameEvent(EventKind.Move, 10, 3, -1, Key(4, 2), Key(5, 2)));

            // 원거리는 사거리 안이라 이동하지 않고 먼저 때린다 (08 §5.2 정지 조건)
            e.Add(new GameEvent(EventKind.Attack, 12, 1, 3, 0));
            e.Add(new GameEvent(EventKind.Damage, 12, 1, 3, 34, 226));

            // 지원 유닛의 보호막 — Aux 는 적용 후 총량이다
            e.Add(new GameEvent(EventKind.Attack, 14, 2, 0, 1));
            e.Add(new GameEvent(EventKind.Shield, 14, 2, 0, 80, 80));
            e.Add(new GameEvent(EventKind.StatusApply, 14, 2, 0, (int)StatusKind.Shield, 14 + 200));

            // 보호막이 먼저 깎이고 HP 는 그대로 (08 §5 보호막 → HP 순)
            e.Add(new GameEvent(EventKind.Attack, 20, 3, 0, 0));
            e.Add(new GameEvent(EventKind.Shield, 20, 3, 0, -45, 35));
            e.Add(new GameEvent(EventKind.Damage, 20, 3, 0, 0, 320));

            // 보호막을 넘긴 피해 — 넘친 만큼만 HP 로 간다
            e.Add(new GameEvent(EventKind.Attack, 40, 3, 0, 0));
            e.Add(new GameEvent(EventKind.Shield, 40, 3, 0, -35, 0));
            e.Add(new GameEvent(EventKind.StatusExpire, 40, 3, 0, (int)StatusKind.Shield));
            e.Add(new GameEvent(EventKind.Damage, 40, 3, 0, 10, 310));

            // 첫 피격 무효 (C3-A 그림자) — 상태가 소모되고 피해가 0 이다.
            // ★ Damage 가 아니라 Dodge 로 나간다(v1.1). Aux 는 "안 깎인" 현재 HP 라
            //   View 가 숫자를 띄우지 않고 회피 연출만 내면 된다.
            e.Add(new GameEvent(EventKind.Attack, 44, 3, 1, 0));
            e.Add(new GameEvent(EventKind.StatusExpire, 44, -1, 1, (int)StatusKind.Invulnerable));
            e.Add(new GameEvent(EventKind.Dodge, 44, 3, 1, 0, 210));

            // 액티브가 실제로 터졌다 (Value = 1). 화면은 이때 스킬 이름을 띄운다.
            e.Add(new GameEvent(EventKind.SkillCast, 46, 0, -1, 1));

            // 상태이상 두 종
            e.Add(new GameEvent(EventKind.Attack, 46, 4, 0, 1));
            e.Add(new GameEvent(EventKind.StatusApply, 46, 4, 0, (int)StatusKind.Slow, 46 + 60));
            e.Add(new GameEvent(EventKind.StatusApply, 46, 4, 1, (int)StatusKind.Root, 46 + 40));

            e.Add(new GameEvent(EventKind.Heal, 60, 2, 0, 55, 365));
            e.Add(new GameEvent(EventKind.StatusExpire, 86, -1, 1, (int)StatusKind.Root));
            e.Add(new GameEvent(EventKind.StatusExpire, 106, -1, 0, (int)StatusKind.Slow));

            // 적 하나 처치
            e.Add(new GameEvent(EventKind.Attack, 120, 0, 4, 0));
            e.Add(new GameEvent(EventKind.Damage, 120, 0, 4, 140, 0));
            e.Add(new GameEvent(EventKind.Death, 120, 0, 4, 0));

            // 45초(900틱) 초과 — 서든데스 (08 §5.3). 아군에게만 걸린다
            e.Add(new GameEvent(EventKind.SuddenDeath, 900, -1, -1, 0));
            e.Add(new GameEvent(EventKind.Damage, 920, -1, 1, 2, 178));
            e.Add(new GameEvent(EventKind.Damage, 940, -1, 1, 5, 173));

            e.Add(new GameEvent(EventKind.Attack, 960, 1, 3, 1));
            e.Add(new GameEvent(EventKind.Damage, 960, 1, 3, 226, 0));
            e.Add(new GameEvent(EventKind.Death, 960, 1, 3, 0));

            e.Add(new GameEvent(EventKind.RoundEnd, 960, -1, -1, 1));

            return new BattleLog(stage: 1, round: 3, seed: 20260802UL, units: units, events: e);
        }
    }
}
