// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System.Collections.Generic;

namespace DomoNinja.Core.Events
{
    /// <summary>
    /// 전투에 등장하는 유닛 한 명의 명세. <b>전투당 한 번만 만들어진다.</b>
    /// </summary>
    /// <remarks>
    /// ★ 이 타입이 있는 이유 — 이벤트 스트림만으로는 화면을 그릴 수 없다.
    /// unity 는 "3번 유닛이 (2,4)에서 (3,4)로 갔다" 는 알 수 있어도
    /// 3번이 누구인지, 스프라이트가 뭔지, 체력바 분모가 얼마인지는 모른다.
    ///
    /// 그렇다고 그걸 이벤트마다 실으면 문자열 할당이 틱마다 생겨
    /// <see cref="GameEvent"/> 를 struct 로 둔 이유가 사라진다.
    /// → <b>전투당 1회의 헤더</b>와 <b>틱당 다수의 스트림</b>으로 나눈다.
    /// 할당 제약은 후자에만 걸린다.
    /// </remarks>
    public readonly struct UnitSpec
    {
        /// <summary>이벤트의 ActorId/TargetId 가 가리키는 값. 전투 안에서만 유효하다.</summary>
        public readonly int UnitId;

        /// <summary>0 = 아군, 1 = 적.</summary>
        public readonly int Team;

        /// <summary>
        /// 무엇인가. unity 가 스프라이트를 고르는 열쇠다.
        /// <b>아군은 data/characters.json 의 id("C3"), 적은 data/encounters.json 의 type("slime")</b> 이 온다.
        /// </summary>
        /// <remarks>
        /// 이름을 CharacterId 로 두지 않은 이유 — 적은 캐릭터가 아니다.
        /// 적은 스킬을 갖지 않고(D-39) 로스터에도 없다. 두 출처의 식별자가 한 필드에 실리므로
        /// 이름이 한쪽만 가리키면 반대쪽을 다루는 코드가 틀린 곳을 찾게 된다.
        /// 값이 겹칠 일은 없다 — 한쪽은 C+숫자, 다른 쪽은 영단어다.
        /// </remarks>
        public readonly string TypeId;

        /// <summary>체력바 분모. 전투 중 변하지 않는다.</summary>
        public readonly int MaxHp;

        /// <summary>시작 좌표 <see cref="Domain.Coord.OrderKey"/>. 이후 위치는 Move 이벤트로만 바뀐다.</summary>
        public readonly int StartCoordKey;

        public UnitSpec(int unitId, int team, string typeId, int maxHp, int startCoordKey)
        {
            UnitId = unitId;
            Team = team;
            TypeId = typeId;
            MaxHp = maxHp;
            StartCoordKey = startCoordKey;
        }

        public override string ToString() =>
            $"#{UnitId} {TypeId} team={Team} hp={MaxHp} @{StartCoordKey}";
    }

    /// <summary>
    /// 전투 1회(= 라운드 1회)의 완전한 기록. <b>이것 하나면 화면을 처음부터 끝까지 재생할 수 있다.</b>
    /// </summary>
    /// <remarks>
    /// unity 는 core 의 어떤 타입도 호출하지 않고 이 객체만 읽는다.
    /// 그래서 되감기·배속·일시정지가 전부 View 안에서 끝나고, 전투 결과에 영향을 줄 수 없다. (08 §5.5)
    ///
    /// 런 전체가 아니라 전투 단위인 이유 — 라운드 사이에는 상점이라는 개입 지점이 있어서
    /// 재생이 어차피 끊긴다. 런 단위로 묶으면 상점 상태까지 이 타입이 떠안게 된다.
    /// </remarks>
    public sealed class BattleLog
    {
        /// <summary>포맷 버전. 계약이 바뀌면 올린다. 팀원이 버전 불일치를 즉시 알 수 있게 두는 값이다.</summary>
        public const int FormatVersion = 1;

        public int Version { get; }

        /// <summary>스테이지 번호 (1 또는 2). D-68.</summary>
        public int Stage { get; }

        /// <summary>라운드 번호 1..8.</summary>
        public int Round { get; }

        /// <summary>이 전투에 쓰인 시드. 같은 값이면 같은 로그가 나온다 — 버그 재현의 유일한 입력이다.</summary>
        public ulong Seed { get; }

        /// <summary>등장 유닛 전원. 아군·적 모두. UnitId 오름차순.</summary>
        public IReadOnlyList<UnitSpec> Units { get; }

        /// <summary>시간순 이벤트. 마지막 항목은 항상 <see cref="EventKind.RoundEnd"/> 다.</summary>
        public IReadOnlyList<GameEvent> Events { get; }

        public BattleLog(
            int stage,
            int round,
            ulong seed,
            IReadOnlyList<UnitSpec> units,
            IReadOnlyList<GameEvent> events)
        {
            Version = FormatVersion;
            Stage = stage;
            Round = round;
            Seed = seed;
            Units = units;
            Events = events;
        }
    }
}
