using System;
using System.Collections.Generic;
using System.Linq;
using DomoNinja.Core.Events;
using NUnit.Framework;

namespace DomoNinja.Core.Tests
{
    /// <summary>
    /// 이벤트 로그 포맷(H1)의 계약 검사.
    /// </summary>
    /// <remarks>
    /// ★ 이 파일은 코드가 맞는지가 아니라 <b>약속을 어겼는지</b>를 본다.
    /// 팀원은 core 내부를 모르는 채로 이 포맷 하나만 보고 Unity View 를 짠다(19 §4.1).
    /// 그래서 enum 번호를 하나 밀거나 RoundEnd 를 빼먹는 변경은 컴파일도 통과하고
    /// 우리 쪽 테스트도 통과하면서 <b>상대 화면에서만 조용히 틀린다.</b>
    /// 그런 변경을 CI 에서 실패로 만드는 것이 목적이다.
    /// </remarks>
    [TestFixture]
    public class EventLogContractTests
    {
        [Test]
        public void 이벤트_종류_번호는_고정이다()
        {
            // 값을 하드코딩해두는 것이 요점이다. 항목을 중간에 끼워 넣으면 여기서 깨진다.
            Assert.That((int)EventKind.None, Is.EqualTo(0));
            Assert.That((int)EventKind.Move, Is.EqualTo(1));
            Assert.That((int)EventKind.Attack, Is.EqualTo(2));
            Assert.That((int)EventKind.Damage, Is.EqualTo(3));
            Assert.That((int)EventKind.Shield, Is.EqualTo(4));
            Assert.That((int)EventKind.Heal, Is.EqualTo(5));
            Assert.That((int)EventKind.Death, Is.EqualTo(6));
            Assert.That((int)EventKind.RoundEnd, Is.EqualTo(7));
            Assert.That((int)EventKind.StatusApply, Is.EqualTo(8));
            Assert.That((int)EventKind.StatusExpire, Is.EqualTo(9));
            Assert.That((int)EventKind.SuddenDeath, Is.EqualTo(10));

            // ★ v1 동결(2026-08-02) 이후 처음 늘어난 항목 (D+4 포맷 리뷰).
            //   위 1~10 이 그대로라는 것이 이 테스트의 요점이다 — 동결이 지키려던 건
            //   "항목을 늘리지 않는다" 가 아니라 "기존 번호를 안 민다" 다.
            Assert.That((int)EventKind.Dodge, Is.EqualTo(11));
        }

        [Test]
        public void 상태이상_번호는_고정이다()
        {
            Assert.That((int)StatusKind.Weaken, Is.EqualTo(1));
            Assert.That((int)StatusKind.DotRamping, Is.EqualTo(2));
            Assert.That((int)StatusKind.Invulnerable, Is.EqualTo(3));
            Assert.That((int)StatusKind.Regen, Is.EqualTo(4));
            Assert.That((int)StatusKind.Slow, Is.EqualTo(5));
            Assert.That((int)StatusKind.Root, Is.EqualTo(6));
            Assert.That((int)StatusKind.Shield, Is.EqualTo(7));
            Assert.That((int)StatusKind.Taunt, Is.EqualTo(8));

            // 08 §5 가 상태이상을 8종으로 못박았다. 9번째가 생겼다면 스펙 개정이 먼저다.
            Assert.That(Enum.GetValues(typeof(StatusKind)).Length, Is.EqualTo(9)); // None 포함
        }

        [Test]
        public void 이벤트는_참조_타입을_담지_않는다()
        {
            // sim 은 이 struct 를 수만 런 × 수만 건 만든다. 필드에 참조가 하나라도 들어오면
            // 틱마다 GC 가 붙고 17 §7 의 처리량 전제가 무너진다.
            Assert.That(typeof(GameEvent).IsValueType, Is.True);

            var refFields = typeof(GameEvent)
                .GetFields()
                .Where(f => !f.FieldType.IsValueType)
                .Select(f => f.Name)
                .ToArray();

            Assert.That(refFields, Is.Empty, "GameEvent 에 참조 타입 필드가 들어왔다: " + string.Join(", ", refFields));
        }

        [Test]
        public void 더미_로그는_모든_이벤트_종류를_한_번씩은_낸다()
        {
            var log = DummyBattleLog.Create();
            var seen = new HashSet<EventKind>(log.Events.Select(x => x.Kind));

            foreach (EventKind kind in Enum.GetValues(typeof(EventKind)))
            {
                if (kind == EventKind.None) continue;

                // 빠진 종류가 있으면 팀원이 그 연출 분기를 만들어 볼 방법이 없다.
                Assert.That(seen, Does.Contain(kind), $"더미 로그에 {kind} 가 없다");
            }
        }

        [Test]
        public void 로그의_틱은_되돌아가지_않는다()
        {
            var log = DummyBattleLog.Create();

            for (int i = 1; i < log.Events.Count; i++)
            {
                Assert.That(
                    log.Events[i].Tick, Is.GreaterThanOrEqualTo(log.Events[i - 1].Tick),
                    $"{i} 번째에서 틱이 거꾸로 갔다");
            }
        }

        [Test]
        public void 마지막_이벤트는_항상_라운드_종료다()
        {
            var log = DummyBattleLog.Create();

            // View 가 재생 종료를 판정하는 유일한 신호다. 없으면 화면이 그대로 멈춘다.
            Assert.That(log.Events[log.Events.Count - 1].Kind, Is.EqualTo(EventKind.RoundEnd));
        }

        [Test]
        public void 이벤트가_가리키는_유닛은_전부_헤더에_있다()
        {
            var log = DummyBattleLog.Create();
            var ids = new HashSet<int>(log.Units.Select(u => u.UnitId));

            foreach (var ev in log.Events)
            {
                // -1 은 "주체 없음"(서든데스·라운드 종료)이라 허용한다.
                if (ev.ActorId != -1)
                    Assert.That(ids, Does.Contain(ev.ActorId), $"{ev} 의 ActorId 가 헤더에 없다");
                if (ev.TargetId != -1)
                    Assert.That(ids, Does.Contain(ev.TargetId), $"{ev} 의 TargetId 가 헤더에 없다");
            }
        }

        [Test]
        public void 헤더의_유닛_번호는_중복되지_않는다()
        {
            var log = DummyBattleLog.Create();

            Assert.That(log.Units.Select(u => u.UnitId).Distinct().Count(), Is.EqualTo(log.Units.Count));
        }

        [Test]
        public void 포맷_버전이_박혀_있다()
        {
            // 팀원이 버전 불일치를 즉시 알 수 있어야 한다. 계약이 바뀌면 이 숫자를 올린다.
            Assert.That(DummyBattleLog.Create().Version, Is.EqualTo(BattleLog.FormatVersion));
        }
    }
}
