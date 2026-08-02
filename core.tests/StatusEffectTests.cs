using System.Collections.Generic;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Events;
using NUnit.Framework;

namespace DomoNinja.Core.Tests
{
    /// <summary>상태이상 8종 컨테이너 (`_schema` §3).</summary>
    [TestFixture]
    public class StatusEffectTests
    {
        [Test]
        public void 같은_종류를_다시_걸면_누적이_아니라_갱신이다()
        {
            // ★ 누적하게 두면 둔화 배율이 곱해지며 발산한다 (1.35² = 1.82).
            //    합연산 규칙(§8)과도 어긋난다. 세기를 올리는 건 스킬 강화의 몫이다.
            var set = new StatusSet();
            set.Apply(new StatusEffect(StatusKind.Slow, expireTick: 60, valueA: 1350));
            set.Apply(new StatusEffect(StatusKind.Slow, expireTick: 120, valueA: 1350));

            Assert.That(set.Count, Is.EqualTo(1));
            set.TryGet(StatusKind.Slow, out var slow);
            Assert.That(slow.ExpireTick, Is.EqualTo(120), "지속시간은 새로 시작한다");
            Assert.That(set.MoveIntervalDeltaPermille, Is.EqualTo(350), "배율이 겹쳐 커지지 않는다");
        }

        [Test]
        public void 다른_종류는_같이_걸린다()
        {
            var set = new StatusSet();
            set.Apply(new StatusEffect(StatusKind.Slow, 60, 1350));
            set.Apply(new StatusEffect(StatusKind.Root, 40));
            set.Apply(new StatusEffect(StatusKind.Weaken, 200, 850, 1150));

            Assert.That(set.Count, Is.EqualTo(3));
            Assert.That(set.Has(StatusKind.Root), Is.True);
        }

        [Test]
        public void Root_는_이동만_막고_공격은_막지_않는다()
        {
            // slow(느려짐)와 root(못 움직임)는 다르고, 둘 다 공격은 가능하다 (_schema §3).
            var set = new StatusSet();
            Assert.That(set.CanMove, Is.True);

            set.Apply(new StatusEffect(StatusKind.Root, 40));
            Assert.That(set.CanMove, Is.False);
            Assert.That(set.AttackDeltaPermille, Is.EqualTo(0), "root 는 공격력에 영향을 주지 않는다");
        }

        [Test]
        public void Weaken_은_공격력과_받는_피해를_동시에_바꾼다()
        {
            // C6-A 주술: 적 전체 공격력 -15%, 받는 피해 +15%
            var set = new StatusSet();
            set.Apply(new StatusEffect(StatusKind.Weaken, 200, valueA: 850, valueB: 1150));

            Assert.That(set.AttackDeltaPermille, Is.EqualTo(-150));
            Assert.That(set.DamageTakenDeltaPermille, Is.EqualTo(150));
        }

        [Test]
        public void 만료된_것만_사라지고_무엇이_사라졌는지_알려준다()
        {
            // 사라진 목록을 돌려주지 않으면 View 가 StatusExpire 를 못 내고
            // 화면에 상태 아이콘이 영원히 남는다.
            var set = new StatusSet();
            set.Apply(new StatusEffect(StatusKind.Slow, expireTick: 60, valueA: 1350));
            set.Apply(new StatusEffect(StatusKind.Root, expireTick: 100));

            var expired = new List<StatusKind>();
            set.ExpireAt(60, expired);

            Assert.That(expired, Is.EqualTo(new[] { StatusKind.Slow }));
            Assert.That(set.Has(StatusKind.Slow), Is.False);
            Assert.That(set.Has(StatusKind.Root), Is.True, "아직 만료 틱이 아니다");
        }

        [Test]
        public void 만료_틱_직전에는_살아_있다()
        {
            var set = new StatusSet();
            set.Apply(new StatusEffect(StatusKind.Root, expireTick: 60));

            var expired = new List<StatusKind>();
            set.ExpireAt(59, expired);

            Assert.That(expired, Is.Empty);
            Assert.That(set.Has(StatusKind.Root), Is.True);
        }

        [Test]
        public void 무기한_상태는_만료되지_않는다()
        {
            // C2-A 철벽의 taunt 처럼 전투 내내 유지되는 것들이 있다.
            var set = new StatusSet();
            set.Apply(new StatusEffect(StatusKind.Taunt, StatusEffect.Never, valueA: 3000));

            var expired = new List<StatusKind>();
            set.ExpireAt(100_000, expired);

            Assert.That(expired, Is.Empty);
            Assert.That(set.ThreatPermille, Is.EqualTo(3000));
        }

        [Test]
        public void 여러_개가_한꺼번에_만료돼도_다_걷힌다()
        {
            var set = new StatusSet();
            set.Apply(new StatusEffect(StatusKind.Slow, 50, 1350));
            set.Apply(new StatusEffect(StatusKind.Root, 50));
            set.Apply(new StatusEffect(StatusKind.Regen, 50, 30, 20));

            var expired = new List<StatusKind>();
            set.ExpireAt(50, expired);

            Assert.That(expired.Count, Is.EqualTo(3));
            Assert.That(set.Count, Is.EqualTo(0));
        }

        [Test]
        public void 상태이상_종류는_이벤트_로그와_같은_enum_을_쓴다()
        {
            // ★ 전투용 enum 을 따로 만들면 같은 8종이 두 벌 생기고, 그 사이 매핑이
            //   하나만 틀려도 화면에 다른 상태가 뜬다. 동결된 로그 포맷(23)을 정본으로 삼는다.
            var set = new StatusSet();
            set.Apply(new StatusEffect(StatusKind.Shield, StatusEffect.Never, valueA: 350));

            set.TryGet(StatusKind.Shield, out var shield);
            Assert.That((int)shield.Kind, Is.EqualTo(7), "23 의 StatusKind 번호와 같아야 한다");
        }

        [Test]
        public void 없는_상태를_지워도_터지지_않는다()
        {
            var set = new StatusSet();

            Assert.That(set.Remove(StatusKind.Taunt), Is.False);
            Assert.That(set.TryGet(StatusKind.Regen, out _), Is.False);
            Assert.That(set.ThreatPermille, Is.EqualTo(0));
        }
    }
}
