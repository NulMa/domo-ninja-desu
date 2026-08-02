using DomoNinja.Core.Combat;
using NUnit.Framework;

namespace DomoNinja.Core.Tests
{
    /// <summary>합연산 규칙 (`A-10` · `_schema` §8).</summary>
    [TestFixture]
    public class ModifierTests
    {
        [Test]
        public void 배율은_천분율_정수가_된다()
        {
            Assert.That(Permille.FromMultiplier(1.5), Is.EqualTo(1500));
            Assert.That(Permille.FromMultiplier(0.85), Is.EqualTo(850));
            Assert.That(Permille.FromMultiplier(1.0), Is.EqualTo(1000));

            Assert.That(Permille.DeltaFromMultiplier(1.5), Is.EqualTo(500));
            Assert.That(Permille.DeltaFromMultiplier(0.6), Is.EqualTo(-400));
        }

        [Test]
        public void 실수_표현_오차가_천분율에_새지_않는다()
        {
            // 0.85 는 이진수로 정확히 표현되지 않는다(0.84999999999999997...).
            // 로드 경계에서 한 번 정수로 못 박기 때문에 그 오차가 전투 안으로 들어오지 않는다.
            Assert.That(Permille.FromMultiplier(0.85), Is.EqualTo(850));
            Assert.That(Permille.FromMultiplier(0.15), Is.EqualTo(150));
            Assert.That(Permille.FromMultiplier(1.35), Is.EqualTo(1350));
            Assert.That(Permille.FromMultiplier(0.12), Is.EqualTo(120));
        }

        [Test]
        public void 적용_순서가_결과를_바꾸지_않는다()
        {
            // ★ 이 테스트가 합연산을 채택한 이유 그 자체다.
            var a = new ModifierSum();
            a.AddMultiplier(1.5);
            a.AddMultiplier(1.2);
            a.AddMultiplier(0.9);

            var b = new ModifierSum();
            b.AddMultiplier(0.9);
            b.AddMultiplier(1.5);
            b.AddMultiplier(1.2);

            Assert.That(b.ApplyTo(37), Is.EqualTo(a.ApplyTo(37)));
            Assert.That(a.DeltaPermille, Is.EqualTo(600)); // +50% +20% -10%
        }

        [Test]
        public void 곱연산과_결과가_다르다()
        {
            // 같은 값을 곱으로 쌓으면 절삭이 여러 번 일어나고 결과가 커진다.
            // 두 방식이 실제로 갈린다는 걸 못박아 둔다 — 나중에 누가 곱연산으로 되돌리면 여기서 깨진다.
            var sum = new ModifierSum();
            sum.AddMultiplier(1.5);
            sum.AddMultiplier(1.2);

            int additive = sum.ApplyTo(100);          // 100 * (1 + 0.5 + 0.2) = 170
            int multiplicative = 100 * 15 / 10 * 12 / 10; // 180

            Assert.That(additive, Is.EqualTo(170));
            Assert.That(multiplicative, Is.EqualTo(180));
            Assert.That(additive, Is.Not.EqualTo(multiplicative));
        }

        [Test]
        public void 절삭은_한_번만_일어난다()
        {
            // 37 * 1.15 = 42.55 → 42. 중간 절삭이 있었다면 다른 값이 나온다.
            var sum = new ModifierSum();
            sum.AddMultiplier(1.15);

            Assert.That(sum.ApplyTo(37), Is.EqualTo(42));
        }

        [Test]
        public void 보정이_없으면_원본_그대로다()
        {
            var sum = new ModifierSum();
            Assert.That(sum.ApplyTo(123), Is.EqualTo(123));
        }

        [Test]
        public void 감소가_겹쳐도_음수가_되지_않는다()
        {
            // damageTaken 감소가 여럿 겹치면 배율이 음수가 될 수 있는데,
            // 그대로 두면 피해가 회복이 된다. 밸런스가 아니라 고장이라 0 에서 막는다.
            var sum = new ModifierSum();
            sum.AddMultiplier(0.4);   // -60%
            sum.AddMultiplier(0.35);  // -65%
            sum.AddMultiplier(0.6);   // -40%  → 합계 -165%

            Assert.That(sum.DeltaPermille, Is.LessThan(-1000));
            Assert.That(sum.ApplyTo(100), Is.EqualTo(0));
        }

        [Test]
        public void 넘치면_감싸지_않고_잘린다()
        {
            // ★ 이 테스트가 구현 결함을 하나 잡았다.
            //   중간 계산만 long 으로 올려두면 결과를 int 로 되돌릴 때 감싸면서 음수가 된다.
            //   피해 계산이라면 그 자리에서 회복이 되는 셈이다.
            //   게임 수치로 도달하기 어려운 크기지만 조용히 틀리는 종류라 잘라낸다.
            Assert.That(Permille.Apply(int.MaxValue / 2, 3000), Is.EqualTo(int.MaxValue));
            Assert.That(Permille.Apply(int.MinValue / 2, 3000), Is.EqualTo(int.MinValue));

            var sum = new ModifierSum();
            sum.AddMultiplier(3.0);
            Assert.That(sum.ApplyTo(1_000_000), Is.EqualTo(3_000_000), "정상 범위는 그대로 계산된다");
        }
    }
}
