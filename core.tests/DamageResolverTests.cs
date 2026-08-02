using System.Linq;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Events;
using NUnit.Framework;

namespace DomoNinja.Core.Tests
{
    /// <summary>피해·회복·보호막 적용 순서 (`_schema` §3 shield · §8).</summary>
    [TestFixture]
    public class DamageResolverTests
    {
        private ListEventSink _sink = null!;

        [SetUp]
        public void SetUp() => _sink = new ListEventSink();

        private static Unit Make(int maxHp = 100) =>
            new Unit(0, Team.Ally, "C1", maxHp, attack: 10, attackInterval: 20,
                     range: 1, moveInterval: 5, at: new Coord(0, 0));

        // ────────────────────────────── 피해

        [Test]
        public void 보호막이_먼저_깎이고_HP_는_그대로다()
        {
            var u = Make();
            u.Shield = 50;

            var dealt = DamageResolver.ApplyDamage(u, 30, actorId: 9, tick: 10, _sink);

            Assert.That(u.Shield, Is.EqualTo(20));
            Assert.That(u.Hp, Is.EqualTo(100), "보호막이 남아 있으면 HP 는 건드리지 않는다");
            Assert.That(dealt.Dealt, Is.EqualTo(30));
        }

        [Test]
        public void 보호막을_넘긴_만큼만_HP_로_간다()
        {
            var u = Make();
            u.Shield = 20;

            var dealt = DamageResolver.ApplyDamage(u, 50, 9, 10, _sink);

            Assert.That(u.Shield, Is.EqualTo(0));
            Assert.That(u.Hp, Is.EqualTo(70));
            Assert.That(dealt.Dealt, Is.EqualTo(50));
        }

        [Test]
        public void 보호막은_받는_피해_배율을_적용한_뒤의_피해를_흡수한다()
        {
            // ★ 순서를 뒤집으면 방어 스킬이 있을수록 보호막이 더 오래 버텨 두 방어가 곱해진다.
            //   그건 합연산 규칙(§8)이 막으려던 것이다.
            var u = Make();
            u.Shield = 100;
            u.DamageTakenDeltaPermille = -400;   // 받는 피해 -40%

            DamageResolver.ApplyDamage(u, 50, 9, 10, _sink);

            Assert.That(u.Shield, Is.EqualTo(70), "50 이 아니라 30 만 흡수해야 한다");
        }

        [Test]
        public void 받는_피해_보정은_스킬과_상태이상을_더한_뒤_한_번만_적용한다()
        {
            var u = Make();
            u.DamageTakenDeltaPermille = -400;                                  // 스킬 -40%
            u.Status.Apply(new StatusEffect(StatusKind.Weaken, 200, 850, 1150)); // weaken +15%

            DamageResolver.ApplyDamage(u, 100, 9, 10, _sink);

            // 100 * (1 - 0.4 + 0.15) = 75. 곱연산(100*0.6*1.15=69)과 다르다.
            Assert.That(u.Hp, Is.EqualTo(25));
        }

        [Test]
        public void 첫_피격_무효는_한_번만_먹는다()
        {
            // C3-A 그림자. 소모되고 사라진다.
            var u = Make();
            u.Status.Apply(new StatusEffect(StatusKind.Invulnerable, StatusEffect.Never));

            var first = DamageResolver.ApplyDamage(u, 40, 9, 10, _sink);
            var second = DamageResolver.ApplyDamage(u, 40, 9, 20, _sink);

            Assert.That(first.Dealt, Is.EqualTo(0));
            Assert.That(first.Dodged, Is.True, "회피한 0 과 그냥 0 피해는 구분돼야 한다 — on_dodge 가 여기 걸린다");
            Assert.That(second.Dealt, Is.EqualTo(40));
            Assert.That(second.Dodged, Is.False);
            Assert.That(u.Hp, Is.EqualTo(60));
            Assert.That(u.Status.Has(StatusKind.Invulnerable), Is.False);
        }

        [Test]
        public void 죽으면_Death_가_한_번_나간다()
        {
            var u = Make(30);

            DamageResolver.ApplyDamage(u, 50, 9, 10, _sink);

            Assert.That(u.IsAlive, Is.False);
            Assert.That(u.Hp, Is.EqualTo(0), "HP 가 음수로 내려가지 않는다");
            Assert.That(_sink.Events.Count(e => e.Kind == EventKind.Death), Is.EqualTo(1));
        }

        [Test]
        public void 죽은_유닛은_더_맞지_않는다()
        {
            var u = Make(30);
            DamageResolver.ApplyDamage(u, 50, 9, 10, _sink);
            _sink.Clear();

            var dealt = DamageResolver.ApplyDamage(u, 20, 9, 20, _sink);

            Assert.That(dealt.Dealt, Is.EqualTo(0));
            Assert.That(_sink.Events, Is.Empty, "시체를 때리는 이벤트가 로그에 쌓이면 안 된다");
        }

        [Test]
        public void 피해_이벤트의_Aux_는_적용_후_HP_다()
        {
            // View 는 이 값을 그대로 체력바에 대입한다. 자기가 빼면 규칙이 복제된다(23 §2.1).
            var u = Make();
            DamageResolver.ApplyDamage(u, 30, 9, 10, _sink);

            var dmg = _sink.Events.Single(e => e.Kind == EventKind.Damage);
            Assert.That(dmg.Value, Is.EqualTo(30));
            Assert.That(dmg.Aux, Is.EqualTo(70));
        }

        [Test]
        public void 보호막_이벤트의_Aux_는_적용_후_총량이다()
        {
            var u = Make();
            u.Shield = 50;
            DamageResolver.ApplyDamage(u, 30, 9, 10, _sink);

            var shield = _sink.Events.Single(e => e.Kind == EventKind.Shield);
            Assert.That(shield.Value, Is.EqualTo(-30), "차감은 음수로 실린다");
            Assert.That(shield.Aux, Is.EqualTo(20));
        }

        // ────────────────────────────── 회복

        [Test]
        public void 회복은_최대_체력을_넘지_않는다()
        {
            var u = Make();
            u.Hp = 80;

            int healed = DamageResolver.ApplyHeal(u, 50, 9, 10, _sink);

            Assert.That(healed, Is.EqualTo(20), "실제 회복량만 돌려준다 — 흡혈 계산이 이 값을 쓴다");
            Assert.That(u.Hp, Is.EqualTo(100));
        }

        [Test]
        public void 회복은_보호막을_채우지_않는다()
        {
            var u = Make();
            u.Hp = 50;
            u.Shield = 10;

            DamageResolver.ApplyHeal(u, 30, 9, 10, _sink);

            Assert.That(u.Hp, Is.EqualTo(80));
            Assert.That(u.Shield, Is.EqualTo(10), "보호막은 회복 대상이 아니다");
        }

        [Test]
        public void 죽은_유닛은_회복되지_않는다()
        {
            // A6 — 부활 없음. 여기서 막지 않으면 광역 회복이 시체를 일으킨다.
            var u = Make();
            u.Hp = 0;

            int healed = DamageResolver.ApplyHeal(u, 50, 9, 10, _sink);

            Assert.That(healed, Is.EqualTo(0));
            Assert.That(u.IsAlive, Is.False);
        }

        // ────────────────────────────── 보호막 부여

        [Test]
        public void 보호막은_상한을_넘지_않는다()
        {
            var u = Make();
            DamageResolver.GrantShield(u, amount: 300, maxShield: 200, overflowToHp: false, 0, 10, _sink);

            Assert.That(u.Shield, Is.EqualTo(200));
            Assert.That(u.Hp, Is.EqualTo(100), "overflowToHp 가 false 면 초과분은 버려진다");
        }

        [Test]
        public void overflowToHp_면_초과분이_체력으로_간다()
        {
            // C3-A 그림자 · C5-P2 결계 — 체력 90 짜리 캐릭터에게 "안 맞으면 단단해진다"를 준다.
            var u = Make();
            u.Hp = 60;
            DamageResolver.GrantShield(u, amount: 300, maxShield: 200, overflowToHp: true, 0, 10, _sink);

            Assert.That(u.Shield, Is.EqualTo(200));
            Assert.That(u.Hp, Is.EqualTo(100), "초과 100 중 40 만 들어가고 최대 체력에서 멈춘다");
        }

        [Test]
        public void 보호막이_다_깎이면_상태도_같이_사라진다()
        {
            var u = Make();
            DamageResolver.GrantShield(u, 50, 50, false, 0, 10, _sink);
            _sink.Clear();

            DamageResolver.ApplyDamage(u, 50, 9, 20, _sink);

            Assert.That(u.Status.Has(StatusKind.Shield), Is.False);
            Assert.That(_sink.Events.Any(e => e.Kind == EventKind.StatusExpire
                                              && e.Value == (int)StatusKind.Shield), Is.True,
                "상태가 사라진 걸 알리지 않으면 화면에 보호막 아이콘이 남는다");
        }

        [Test]
        public void 라운드가_끝나면_보호막은_사라지고_HP_는_남는다()
        {
            // A-6 — HP 만 누적된다. 조절할 수 없는 자원이 누적되면 결과가 운으로 갈린다.
            var u = Make();
            u.Hp = 70;
            DamageResolver.GrantShield(u, 50, 50, false, 0, 10, _sink);

            DamageResolver.ClearShield(u, 900, _sink);

            Assert.That(u.Shield, Is.EqualTo(0));
            Assert.That(u.Hp, Is.EqualTo(70));
            Assert.That(u.Status.Has(StatusKind.Shield), Is.False);
        }
    }
}
