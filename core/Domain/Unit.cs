// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

namespace DomoNinja.Core.Domain
{
    /// <summary>아군 0 / 적 1. <see cref="Events.UnitSpec.Team"/> 과 같은 값이다.</summary>
    public enum Team
    {
        Ally = 0,
        Enemy = 1,
    }

    /// <summary>
    /// 전투에 올라간 유닛 1체. <b>전투 중에만 산다</b> — 라운드가 끝나면 HP 만 런 상태로 넘어간다(A-6).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 전부 정수다. HP·피해·좌표·시간 어디에도 부동소수점이 없다.
    /// 배율(<c>value: 1.5</c>)은 적용 시점에 정수 연산 후 절삭하며, 그 계산은 P2 가 한다.
    /// 여기 있는 스탯은 <b>이미 배율이 반영된 결과값</b>이다 — 매 틱 배율을 다시 곱하면
    /// 누산 순서에 따라 결과가 갈리고, 그건 결정론 요구사항(`_schema` §7) 위반이다.
    /// </para>
    /// <para>
    /// ★ <b>class 다.</b> <see cref="Events.GameEvent"/> 를 struct 로 둔 것과 반대인데 이유가 있다 —
    /// 이벤트는 한 런에 수만 건 생기지만 유닛은 전투당 최대 10체 남짓이고,
    /// 매 틱 HP·좌표가 바뀌므로 값 복사가 오히려 사고를 부른다.
    /// </para>
    /// </remarks>
    public sealed class Unit
    {
        /// <summary>이벤트 로그의 ActorId/TargetId 가 가리키는 번호. 전투 안에서만 유효하다.</summary>
        public int Id { get; }

        public Team Team { get; }

        /// <summary>아군은 `characters.json` 의 id, 적은 `encounters.json` 의 type.</summary>
        public string TypeId { get; }

        public int MaxHp { get; }
        public int Hp { get; set; }

        /// <summary>보호막. <b>라운드 종료 시 사라진다</b> — HP 만 누적된다(A-6).</summary>
        public int Shield { get; set; }

        public int Attack { get; set; }

        /// <summary>공격 간격(틱). <b>낮을수록 빠르다.</b></summary>
        public int AttackInterval { get; set; }

        /// <summary>사거리(칸). 판정은 <see cref="Coord.SqrDistanceTo"/> 와 제곱값 비교로만 한다.</summary>
        public int Range { get; set; }

        /// <summary>이동 1칸당 틱. <b>낮을수록 빠르다.</b> 고정포대는 이 값을 쓰지 않는다.</summary>
        public int MoveInterval { get; set; }

        /// <summary>고정포대형. 이동 판정 자체를 건너뛴다. <b>적 전용</b>(A5).</summary>
        public bool Immobile { get; }

        /// <summary>
        /// 받는 피해 보정. <b>스킬·아이템이 합연산으로 누적한 증감분</b>(천분율)이다.
        /// </summary>
        /// <remarks>
        /// 배율이 아니라 증감분인 이유 — 여러 출처를 곱하지 않고 더하기 위해서다 (`_schema` §8).
        /// 전투 중 걸리는 <c>weaken</c> 은 여기 더하지 않고 <see cref="Status"/> 에서 따로 읽는다.
        /// 스킬은 전투 내내 고정이고 상태이상은 붙었다 떨어지므로, 섞으면 해제할 때 뺄 값을 추적해야 한다.
        /// </remarks>
        public int DamageTakenDeltaPermille { get; set; }

        /// <summary>이 유닛에 걸린 상태이상. <b>8종뿐이다</b> (`_schema` §3).</summary>
        public Combat.StatusSet Status { get; } = new Combat.StatusSet();

        /// <summary>
        /// 들고 들어온 스킬을 실행 가능한 형태로 푼 것. 적은 <see cref="Skills.Loadout.Empty"/> 다.
        /// </summary>
        /// <remarks>
        /// 유닛마다 따로 만들어야 한다 — 주기 트리거가 각자의 시계를 갖기 때문이다.
        /// 같은 스킬을 든 두 유닛이 같은 틱에 동시에 터지면 그건 우연이지 규칙이 아니다.
        /// </remarks>
        public Skills.Loadout Loadout { get; set; } = Skills.Loadout.Empty;

        public Coord At { get; set; }

        /// <summary>
        /// 다음 이동/공격까지 남은 틱. <b>정수 누적으로만 관리한다</b> (`_schema` §7.1).
        /// </summary>
        /// <remarks>
        /// 초 단위 실수 누적을 쓰면 20틱마다 미세 오차가 쌓여 몇백 틱 뒤 행동 순서가 뒤집힌다.
        /// sim 과 Unity 가 다른 결과를 내는 전형적인 경로다.
        /// </remarks>
        public int MoveCooldown { get; set; }

        /// <inheritdoc cref="MoveCooldown"/>
        public int AttackCooldown { get; set; }

        public bool IsAlive => Hp > 0;

        /// <summary>
        /// 현재 HP <b>비율</b>의 천분율. `lowestHpAlly` 타겟팅이 쓴다.
        /// </summary>
        /// <remarks>
        /// 절대값이 아니라 비율인 이유 — 체력 180 인 수도승과 80 인 주술사를 절대값으로 비교하면
        /// 항상 주술사만 회복된다 (`_schema` §3).
        /// 천분율 정수인 이유는 float 비교로 동률 판정이 흔들리지 않게 하기 위해서다.
        /// </remarks>
        public int HpPermille => MaxHp <= 0 ? 0 : Hp * 1000 / MaxHp;

        public Unit(int id, Team team, string typeId, int maxHp, int attack,
                    int attackInterval, int range, int moveInterval, Coord at, bool immobile = false)
        {
            Id = id;
            Team = team;
            TypeId = typeId;
            MaxHp = maxHp;
            Hp = maxHp;
            Attack = attack;
            AttackInterval = attackInterval;
            Range = range;
            MoveInterval = moveInterval;
            At = at;
            Immobile = immobile;
        }

        /// <summary>사거리 안에 있는가. <b>제곱거리 비교다 — sqrt 를 쓰지 않는다.</b></summary>
        public bool InRangeOf(Unit other) => At.SqrDistanceTo(other.At) <= Range * Range;

        public override string ToString() => $"#{Id} {TypeId} {Hp}/{MaxHp} @{At}";
    }
}
