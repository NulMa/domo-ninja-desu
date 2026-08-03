using System.Collections.Generic;
using System.Linq;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Events;
using DomoNinja.Core.Skills;
using NUnit.Framework;

namespace DomoNinja.Core.Tests
{
    /// <summary>
    /// 실제 스킬 데이터가 전투에서 동작하는지.
    /// </summary>
    /// <remarks>
    /// ★ 단위 테스트는 전부 통과하면서 <b>배선만 빠져 있는</b> 상태가 가능하다.
    /// 트리거가 컴파일되고 효과가 실행돼도, 전투 루프가 그걸 안 부르면 아무 일도 일어나지 않는다.
    /// 그런데 그 상태에서도 전투는 정상으로 보이고 시뮬은 숫자를 뱉는다 —
    /// <b>스킬이 없는 게임의 밸런스를 재고 있게 된다.</b> 여기서 그걸 막는다.
    /// </remarks>
    [TestFixture]
    public class SkillsInBattleTests
    {
        private static GameData _data = null!;

        [OneTimeSetUp]
        public void Load() => _data = GameDataLoader.Load(
            RepoData.Characters, RepoData.Skills, RepoData.Encounters, RepoData.Economy, RepoData.Meta);

        private static CombatConfig Config() => CombatConfig.From(_data.Economy, 20);

        /// <summary>실제 캐릭터 + 스킬로 아군 유닛을 만든다.</summary>
        private static Unit Ally(int id, string characterId, string? skillId, Coord at,
                                 params string[] supportIds)
        {
            var ch = _data.FindCharacter(characterId)!;
            var skill = skillId == null ? null : _data.FindSkill(skillId);
            var supports = supportIds.Select(s => _data.FindSkill(s)!).ToList();

            var stats = SkillResolver.BuildStats(ch, skill, supports);
            var u = new Unit(id, Team.Ally, ch.Id, stats.Hp, stats.Attack, stats.AttackInterval,
                             stats.Range, stats.MoveInterval, at)
            {
                DamageTakenDeltaPermille = stats.DamageTakenDeltaPermille,
                Loadout = Loadout.Build(skill, supports),
            };
            return u;
        }

        private static Unit Enemy(int id, string type, Coord at, int? hpOverride = null)
        {
            var t = _data.EnemyTypes[type];
            return new Unit(id, Team.Enemy, t.Type, hpOverride ?? t.Hp, t.Attack, t.AttackInterval,
                            t.Range, t.MoveInterval ?? 99, at, t.Immobile);
        }

        private static (BattleResult result, ListEventSink sink) Run(params Unit[] units)
        {
            var sink = new ListEventSink();
            var result = new BattleSimulator(Config()).Run(units, sink);
            return (result, sink);
        }

        // ────────────────────────────── 전투 시작 시 거는 것

        [Test]
        public void 철벽의_어그로가_전투_시작에_걸린다()
        {
            // C2-A — 적이 자신을 우선 노린다. 시작 효과가 배선 안 되면 조용히 안 걸린다.
            var tank = Ally(0, "C2", "C2-A", new Coord(3, 2));
            var squishy = Ally(1, "C3", "C3-A", new Coord(3, 3));
            var foe = Enemy(2, "slime", new Coord(4, 2));

            Run(tank, squishy, foe);

            Assert.That(tank.Status.ThreatPermille, Is.EqualTo(3000));
            Assert.That(squishy.Status.ThreatPermille, Is.EqualTo(0));
        }

        [Test]
        public void 주술이_적_전체에_걸린다()
        {
            // C6-A — 적 전체 공격력 -15% / 받는 피해 +15%.
            var shaman = Ally(0, "C6", "C6-A", new Coord(3, 2));
            var a = Enemy(1, "slime", new Coord(4, 2));
            var b = Enemy(2, "slime", new Coord(7, 5));

            Run(shaman, a, b);

            Assert.That(a.Status.AttackDeltaPermille, Is.EqualTo(-150));
            Assert.That(b.Status.AttackDeltaPermille, Is.EqualTo(-150),
                "멀리 있는 적에게도 걸린다 — all_enemies 다");
        }

        [Test]
        public void 가호는_자신을_빼고_아군에게_걸린다()
        {
            var healer = Ally(0, "C6", "C6-B", new Coord(3, 2));
            var mate = Ally(1, "C1", "C1-A", new Coord(3, 3));

            Run(healer, mate, Enemy(2, "slime", new Coord(4, 2)));

            Assert.That(healer.Status.Has(StatusKind.Regen), Is.False);
            Assert.That(mate.Status.Has(StatusKind.Regen), Is.True);
        }

        // ────────────────────────────── 광역

        [Test]
        public void 연격은_인접한_적에게_파급된다()
        {
            // C1-B — 인접 대상에 50% 파급.
            var samurai = Ally(0, "C1", "C1-B", new Coord(3, 2));
            var primary = Enemy(1, "kappa", new Coord(4, 2));
            var beside = Enemy(2, "kappa", new Coord(4, 3));   // 주 표적에 인접
            var away = Enemy(3, "kappa", new Coord(7, 5));     // 멀다

            var (_, sink) = Run(samurai, primary, beside, away);

            Assert.That(sink.Events.Any(e => e.Kind == EventKind.Damage && e.TargetId == 2), Is.True,
                "인접한 적이 파급을 맞아야 한다");
        }

        [Test]
        public void 난사는_세_체를_동시에_때린다()
        {
            // C4-B — multi_target 3.
            var hunter = Ally(0, "C4", "C4-B", new Coord(3, 2));
            var foes = new[]
            {
                Enemy(1, "slime", new Coord(4, 1)),
                Enemy(2, "slime", new Coord(4, 2)),
                Enemy(3, "slime", new Coord(4, 3)),
                Enemy(4, "slime", new Coord(7, 5)),
            };

            var sink = new ListEventSink();
            new BattleSimulator(Config()).Run(new[] { hunter }.Concat(foes).ToArray(), sink);

            // 첫 공격 틱에 세 체가 맞는지 본다.
            int firstAttackTick = sink.Events.First(e => e.Kind == EventKind.Attack).Tick;
            var hit = sink.Events
                .Where(e => e.Kind == EventKind.Damage && e.Tick == firstAttackTick && e.ActorId == 0)
                .Select(e => e.TargetId).Distinct().ToList();

            Assert.That(hit.Count, Is.EqualTo(3), "동시에 최대 3체");
        }

        // ────────────────────────────── 트리거

        [Test]
        public void 연격의_흡혈이_돈다()
        {
            // C1-B — on_hit 으로 가한 피해의 15% 회복.
            // 순 체력 증가가 아니라 회복 이벤트로 본다. 맞으면서 때리는 상황이라
            // 순증감으로 재면 적 화력에 따라 결과가 뒤집히고, 그건 흡혈 여부와 무관하다.
            var samurai = Ally(0, "C1", "C1-B", new Coord(3, 2));
            samurai.Hp = samurai.MaxHp / 2;

            var (_, sink) = Run(samurai, Enemy(1, "kappa", new Coord(4, 2)));

            Assert.That(sink.Events.Any(e => e.Kind == EventKind.Heal && e.TargetId == 0), Is.True,
                "적중할 때마다 회복이 들어와야 한다");
        }

        [Test]
        public void 그림자의_회피가_보호막을_준다()
        {
            // C3-A — on_dodge. 회피와 "그냥 0 피해"를 구분하지 못하면 이게 안 터진다.
            var ninja = Ally(0, "C3", "C3-A", new Coord(3, 2));
            var (_, sink) = Run(ninja, Enemy(1, "bear", new Coord(4, 2)));

            Assert.That(sink.Events.Any(e => e.Kind == EventKind.Shield && e.TargetId == 0 && e.Value > 0),
                        Is.True, "첫 피격을 무효화한 뒤 보호막을 얻어야 한다");
        }

        [Test]
        public void 철벽은_맞을_때마다_보호막을_얻는다()
        {
            // C2-A — on_damaged.
            var monk = Ally(0, "C2", "C2-A", new Coord(3, 2));
            var (_, sink) = Run(monk, Enemy(1, "bear", new Coord(4, 2)));

            Assert.That(sink.Events.Count(e => e.Kind == EventKind.Shield && e.TargetId == 0 && e.Value > 0),
                        Is.GreaterThan(0));
        }

        [Test]
        public void 일격은_처치하면_즉시_재공격한다()
        {
            // C1-A — on_kill → extra_attack.
            var samurai = Ally(0, "C1", "C1-A", new Coord(3, 2));
            var weak = Enemy(1, "mouse", new Coord(4, 2), hpOverride: 1);
            var next = Enemy(2, "mouse", new Coord(4, 3));

            var (_, sink) = Run(samurai, weak, next);

            int killTick = sink.Events.First(e => e.Kind == EventKind.Death).Tick;
            Assert.That(sink.Events.Any(e => e.Kind == EventKind.Attack && e.ActorId == 0 && e.Tick == killTick
                                             && e.TargetId != 1), Is.True,
                "처치한 틱에 추가 공격이 나가야 한다");
        }

        [Test]
        public void 연쇄는_처치하면_다음_대상으로_넘어간다()
        {
            // C5-B — on_kill → recast(maxChain 3) + 아군 전체 회복.
            var mage = Ally(0, "C5", "C5-B", new Coord(3, 2));
            var a = Enemy(1, "mouse", new Coord(4, 2), hpOverride: 1);
            var b = Enemy(2, "mouse", new Coord(4, 3), hpOverride: 1);

            var (_, sink) = Run(mage, a, b);

            int firstKill = sink.Events.First(e => e.Kind == EventKind.Death).Tick;
            Assert.That(sink.Events.Any(e => e.Kind == EventKind.Attack && e.ActorId == 0
                                             && e.Tick == firstKill && e.TargetId == 2), Is.True,
                "같은 틱에 다음 대상으로 재발동해야 한다");
        }

        [Test]
        public void 파동은_주기적으로_자기_체력을_태운다()
        {
            // C2-B — every_n_tick 60 → self_damage 8%.
            var monk = Ally(0, "C2", "C2-B", new Coord(0, 0));
            var (_, sink) = Run(monk, Enemy(1, "bear", new Coord(7, 5), hpOverride: 4000));

            Assert.That(sink.Events.Any(e => e.Kind == EventKind.Damage && e.ActorId == 0 && e.TargetId == 0),
                        Is.True, "자해 이벤트가 있어야 한다");
        }

        [Test]
        public void 가호의_재생이_실제로_돈다()
        {
            // ★ regen 은 걸리는 것만으로는 아무 일도 안 한다. 매 초 돌려주지 않으면
            //   C6-B 는 상태 아이콘만 뜨고 효과가 없는 스킬이 된다.
            // 동료는 오래 버티는 쪽으로 고른다 — 금방 죽으면 재생이 한 번밖에 안 돌고,
            // 그건 "재생이 도는가"가 아니라 "동료가 버티는가"를 재는 것이 된다.
            var healer = Ally(0, "C6", "C6-B", new Coord(0, 0));
            var mate = Ally(1, "C2", "C2-A", new Coord(0, 1));
            mate.Hp = mate.MaxHp / 2;

            var (_, sink) = Run(healer, mate, Enemy(2, "bear", new Coord(7, 5), hpOverride: 3000));

            var heals = sink.Events.Where(e => e.Kind == EventKind.Heal && e.TargetId == 1).ToList();

            // 최종 HP 로 재지 않는다 — 맞으면서 회복하는 상황이라 순증감은 적 화력에 좌우되고,
            // 그건 재생이 도는지와 무관하다.
            Assert.That(heals.Count, Is.GreaterThan(1), "재생이 매 초 회복을 넣어야 한다");
            Assert.That(heals.All(h => h.Value > 0), Is.True, "회복량이 0 이면 도는 시늉만 하는 것이다");
        }

        [Test]
        public void 각인의_지속피해가_시간이_갈수록_커진다()
        {
            // C5-A — dot_ramping. 걸어두고 돌리지 않으면 아무 일도 안 일어난다.
            var mage = Ally(0, "C5", "C5-A", new Coord(3, 2));
            var foe = Enemy(1, "bear", new Coord(4, 2), hpOverride: 2000);

            var (_, sink) = Run(mage, foe);

            // 평타와 지속 피해가 같은 모양(Damage, actor=시전자)으로 나오므로 틱으로 가른다 —
            // 같은 틱에 Attack 이 없는 Damage 가 지속 피해다.
            var attackTicks = sink.Events
                .Where(e => e.Kind == EventKind.Attack && e.ActorId == 0)
                .Select(e => e.Tick).ToHashSet();

            var dots = sink.Events
                .Where(e => e.Kind == EventKind.Damage && e.ActorId == 0 && e.TargetId == 1
                            && !attackTicks.Contains(e.Tick))
                .Select(e => e.Value).ToList();

            Assert.That(dots.Count, Is.GreaterThan(2), "지속 피해가 여러 번 들어와야 한다");
            Assert.That(dots.Max(), Is.GreaterThan(dots.Min()), "시간이 지날수록 커져야 한다");
        }

        // ────────────────────────────── 안전장치

        [Test]
        public void 연쇄가_무한히_돌지_않는다()
        {
            // 상한이 없으면 sim 수만 런 중 하나만 걸려도 CI 가 통째로 멈춘다.
            var mage = Ally(0, "C5", "C5-B", new Coord(3, 2));
            var foes = Enumerable.Range(1, 6)
                .Select(i => Enemy(i, "mouse", new Coord(4 + (i - 1) % 4, (i - 1) / 4), hpOverride: 1))
                .ToArray();

            var result = new BattleSimulator(Config())
                .Run(new[] { mage }.Concat(foes).ToArray(), NullEventSink.Instance);

            Assert.That(result.Outcome, Is.EqualTo(BattleOutcome.AllyWin));
        }

        [Test]
        public void 스킬이_붙어도_결정론이_유지된다()
        {
            // 🔴 트리거는 실행 순서가 얽히기 쉬운 지점이다. 여기서 갈리면 밸런스 수치가 전부 무의미해진다.
            IReadOnlyList<GameEvent> Once()
            {
                var sink = new ListEventSink();
                new BattleSimulator(Config()).Run(new[]
                {
                    Ally(0, "C1", "C1-B", new Coord(3, 2), "C1-P1", "C1-P2"),
                    Ally(1, "C2", "C2-A", new Coord(3, 3), "C2-P3"),
                    Ally(2, "C6", "C6-B", new Coord(2, 2), "C6-P1"),
                    Enemy(3, "kappa", new Coord(4, 2)),
                    Enemy(4, "eye", new Coord(6, 3)),
                    Enemy(5, "mouse", new Coord(4, 4)),
                }, sink);
                return sink.Events;
            }

            var a = Once();
            var b = Once();

            Assert.That(b.Count, Is.EqualTo(a.Count));
            for (int i = 0; i < a.Count; i++)
                Assert.That(b[i].ToString(), Is.EqualTo(a[i].ToString()), $"{i} 번째 이벤트가 갈렸다");
        }

        // ────────────────────────────── 지속 보호막 (refreshEveryTicks · whileStationary)

        /// <summary>한 유닛에게 실제로 들어온 보호막 변화만 시간순으로 뽑는다.</summary>
        private static List<GameEvent> ShieldEvents(ListEventSink sink, int unitId) =>
            sink.Events.Where(e => e.Kind == EventKind.Shield && e.TargetId == unitId).ToList();

        /// <summary>
        /// 오래 버티면서 아군을 조금씩만 깎는 샌드백.
        /// </summary>
        /// <remarks>
        /// 재충전은 <b>보호막이 비어 있어야 눈에 보인다</b> — 가득 차 있으면 <c>GrantShield</c> 가
        /// 아무 일도 안 하고 이벤트도 안 남긴다. 그래서 "맞긴 맞되 200틱은 버티는" 상대가 필요하다.
        /// <c>larva</c> 는 초당 0.32 피해라 최대 체력 90 짜리도 280틱을 산다.
        /// </remarks>
        private static Unit Sandbag(int id, Coord at) => Enemy(id, "larva", at, hpOverride: 5000);

        [Test]
        public void 잔영은_한_번_주고_끝나지_않는다()
        {
            // C3-P2 — "10초마다 최대 체력 30%의 보호막". refreshEveryTicks = 200틱 = 10초.
            // ★ 재충전이 배선 안 돼 있어도 전투는 정상으로 보인다. 시작 1회는 걸리기 때문이다 —
            //   화면에도 로그에도 "보호막이 걸렸다" 가 뜨고, 텍스트를 읽어보기 전에는 안 드러난다.
            //   액티브는 C3-B 를 쓴다. C3-A 그림자도 보호막을 줘서 출처가 섞이면 잰 값이 흐려진다.
            var hero = Ally(0, "C3", "C3-B", new Coord(3, 2), "C3-P2");

            var (_, sink) = Run(hero, Sandbag(1, new Coord(4, 2)));

            var grants = ShieldEvents(sink, 0).Where(e => e.Value > 0).Select(e => e.Tick).ToList();

            Assert.That(grants, Does.Contain(0), "전투 시작 1회가 없다");
            Assert.That(grants, Does.Contain(200), "10초 뒤 재충전이 없다 — refreshEveryTicks 가 죽어 있다");
        }

        [Test]
        public void 잔영의_상한은_최대_체력의_30퍼센트다()
        {
            // 재충전이 상한을 무시하면 보호막이 전투 내내 쌓여 사실상 무적이 된다.
            var hero = Ally(0, "C3", "C3-B", new Coord(3, 2), "C3-P2");
            var (_, sink) = Run(hero, Sandbag(1, new Coord(4, 2)));

            int cap = hero.MaxHp * 300 / 1000;
            foreach (var e in ShieldEvents(sink, 0))
                Assert.That(e.Aux, Is.LessThanOrEqualTo(cap), $"[{e.Tick}] 보호막이 상한 {cap} 을 넘었다");
        }

        [Test]
        public void 광명은_아군_전체에게_주기적으로_건다()
        {
            // C6-P3 — target 이 allies 다. 시전자만 받으면 "전체" 가 아니다.
            // 둘 다 맞아야 재충전이 이벤트로 드러나므로 샌드백을 각자 앞에 하나씩 세운다.
            var shaman = Ally(0, "C6", "C6-A", new Coord(3, 2), "C6-P3");
            var mate = Ally(1, "C1", "C1-A", new Coord(3, 3));

            var (_, sink) = Run(shaman, mate,
                                Sandbag(2, new Coord(4, 2)), Sandbag(3, new Coord(4, 3)));

            foreach (int id in new[] { 0, 1 })
            {
                var grants = ShieldEvents(sink, id).Where(e => e.Value > 0).Select(e => e.Tick).ToList();
                Assert.That(grants, Does.Contain(0), $"#{id} 가 시작 보호막을 못 받았다");
                Assert.That(grants, Does.Contain(200), $"#{id} 가 재충전을 못 받았다");
            }
        }

        [Test]
        public void 참호는_제자리에서_걸리고_움직이면_풀리고_다시_멈추면_돌아온다()
        {
            // C4-P2 — "제자리에 있는 동안 유지". 세 국면을 한 전투에서 다 지나가게 만든다:
            //   ① 사거리 안의 적을 쏘는 동안(제자리) → ② 그 적이 죽어 먼 적으로 걸어가는 동안 → ③ 다시 자리 잡은 뒤
            // 적은 둘 다 immobile 이다. 움직이는 적은 제가 다가와버려서 사냥꾼이 걸을 일이 없다.
            var hunter = Ally(0, "C4", "C4-A", new Coord(0, 2), "C4-P2");
            var near = Enemy(1, "totem", new Coord(4, 2), hpOverride: 60);
            var far = Enemy(2, "totem", new Coord(7, 5), hpOverride: 5000);

            var (_, sink) = Run(hunter, near, far);
            var events = ShieldEvents(sink, 0);

            Assert.That(events, Is.Not.Empty, "참호가 아예 안 걸렸다");
            Assert.That(events[0].Tick, Is.EqualTo(0), "사거리 안에서 시작했으므로 첫 틱부터 제자리다");
            Assert.That(events[0].Value, Is.GreaterThan(0));

            var revoke = events.FirstOrDefault(e => e.Value < 0);
            Assert.That(revoke.Kind, Is.EqualTo(EventKind.Shield), "걸어갔는데 보호막이 안 풀렸다");

            var regain = events.Where(e => e.Tick > revoke.Tick && e.Value > 0).ToList();
            Assert.That(regain, Is.Not.Empty, "다시 자리 잡아도 안 돌아온다 — 한 번 풀리면 끝이 된다");
        }

        [Test]
        public void 참호는_걸어가는_내내_켜져_있지_않는다()
        {
            // ★ "직전 틱에 안 움직였다" 로 판정하면 안 된다. 이동 간격이 11틱이라
            //   전진 중인 유닛도 10/11 틱은 정지 상태여서 걸어가는 내내 보호막이 켜진다.
            //   그러면 대가(이동 속도 −40%)만 내고 조건은 사실상 없는 스킬이 된다.
            //   ⚠️ C4-A 저격은 사거리를 7 로 올린다. 대각선 반대편 끝이라야 세 번 걷는다.
            var hunter = Ally(0, "C4", "C4-A", new Coord(0, 0), "C4-P2");
            var (_, sink) = Run(hunter, Enemy(1, "totem", new Coord(7, 5), hpOverride: 5000));

            var moves = sink.Events.Where(e => e.Kind == EventKind.Move && e.ActorId == 0)
                                   .Select(e => e.Tick).ToList();
            Assert.That(moves.Count, Is.GreaterThan(1), "이 배치는 두 번 이상 걸어야 한다");

            var grants = ShieldEvents(sink, 0).Where(e => e.Value > 0).ToList();
            for (int i = 0; i + 1 < moves.Count; i++)
            {
                var between = grants.Where(e => e.Tick > moves[i] && e.Tick <= moves[i + 1]);
                Assert.That(between, Is.Empty,
                    $"{moves[i]}틱과 {moves[i + 1]}틱 이동 사이에 보호막이 켜졌다 — 걸어가는 중이다");
            }
        }

        [Test]
        public void 제자리_보호막을_거둘_때_풀_전체를_밀지_않는다()
        {
            // 보호막은 출처가 여럿인 단일 풀이다. 이동할 때 0 으로 밀면 같은 유닛이
            // C6-P3 광명에게 받은 몫까지 날아가, 참호를 든 순간 광명이 그 유닛에게만 무효가 된다.
            var u = new Unit(0, Team.Ally, "C4", 100, attack: 10, attackInterval: 20,
                             range: 1, moveInterval: 8, at: new Coord(0, 0));
            var sink = new ListEventSink();

            DamageResolver.GrantShield(u, 35, maxShield: 35, overflowToHp: false, 0, 0, sink);
            DamageResolver.RevokeShield(u, 20, tick: 1, sink);

            Assert.That(u.Shield, Is.EqualTo(15), "자기 몫(20)만 거둬야 한다");
        }

        [Test]
        public void 상한이_낮은_보호막이_이미_쌓인_것을_깎지_않는다()
        {
            // ★ 상한은 출처별이 아니라 풀 전체에 걸린다. 그래서 상한이 낮은 효과가 나중에 걸리면
            //   이미 있던 보호막이 그 상한까지 내려간다 — 아군을 돕는 스킬이 보호막을 줄인다.
            var u = new Unit(0, Team.Ally, "C4", 100, attack: 10, attackInterval: 20,
                             range: 1, moveInterval: 8, at: new Coord(0, 0));
            var sink = new ListEventSink();

            DamageResolver.GrantShield(u, 20, maxShield: 20, overflowToHp: false, 0, 0, sink);
            Assert.That(u.Shield, Is.EqualTo(20));

            DamageResolver.GrantShield(u, 15, maxShield: 15, overflowToHp: false, 1, 1, sink);
            Assert.That(u.Shield, Is.EqualTo(20), "부여가 손해가 됐다");
        }

        [Test]
        public void 실제_로스터_6명의_모든_액티브가_전투를_완주한다()
        {
            // 스킬 하나가 무한 루프나 예외를 만들면 여기서 걸린다.
            foreach (var ch in _data.Characters)
            {
                foreach (string skillId in ch.SkillIds)
                {
                    var unit = Ally(0, ch.Id, skillId, new Coord(3, 2));
                    var result = new BattleSimulator(Config()).Run(
                        new[] { unit, Enemy(1, "kappa", new Coord(4, 2)) }, NullEventSink.Instance);

                    Assert.That(result.Ticks, Is.GreaterThan(0), $"{skillId} 가 전투를 못 돌렸다");
                }
            }
        }
    }
}
