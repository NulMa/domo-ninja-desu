// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System.Collections.Generic;

namespace DomoNinja.Core.Domain
{
    /// <summary>구매한 아이템 1개. <b>런 전용</b>이다 — 런이 끝나면 같이 사라진다.</summary>
    public readonly struct OwnedItem
    {
        /// <summary>`economy.items` 의 키 (`statBoost` 등).</summary>
        public readonly string Key;

        /// <summary>그 아이템의 <c>options</c> 중 몇 번째인가. 선택지가 없으면 -1.</summary>
        public readonly int OptionIndex;

        public OwnedItem(string key, int optionIndex)
        {
            Key = key;
            OptionIndex = optionIndex;
        }

        public override string ToString() => OptionIndex >= 0 ? $"{Key}#{OptionIndex}" : Key;
    }

    /// <summary>런에 출전한 캐릭터 1명의 진행 상태. <b>라운드를 넘어 살아남는다.</b></summary>
    /// <remarks>
    /// HP 가 여기 있는 이유 — 라운드가 끝나도 체력이 <b>누적</b>되고(A-6), 죽으면 런 종료까지 부활하지 않는다.
    /// 전투용 <see cref="Unit"/> 은 라운드마다 새로 만들어지므로 상태를 담을 곳이 아니다.
    /// </remarks>
    public sealed class RosterEntry
    {
        public string CharacterId { get; }

        /// <summary>라운드를 넘어 누적되는 현재 HP. 0 이면 사망이고 <b>되돌아오지 않는다</b>(A6 — 부활 없음).</summary>
        public int Hp { get; set; }

        public int MaxHp { get; set; }

        /// <summary>활성화한 액티브 스킬. <b>둘 중 하나를 고르면 다른 하나는 런 종료까지 못 산다</b>(08 §2.2 배타 규칙).</summary>
        public string? ActiveSkillId { get; set; }

        /// <summary>구매한 보조 스킬. <b>최대 2개</b> (`economy.shop.supportSkill.maxSelectPerCharacter`).</summary>
        public List<string> SupportSkillIds { get; } = new List<string>();

        /// <summary>이 캐릭터에게 지정해 산 아이템. 같은 것을 여러 개 살 수 있어 <b>중복을 허용한다.</b></summary>
        /// <remarks>
        /// 아이템은 <b>런 전용</b>이다(`runOnly`). 런이 끝나면 같이 사라진다.
        /// 스킬과 달리 팔 수 있으므로(`D-70`) 목록에서 빠질 수도 있다.
        /// </remarks>
        public List<OwnedItem> Items { get; } = new List<OwnedItem>();

        public bool IsAlive => Hp > 0;

        public RosterEntry(string characterId, int maxHp)
        {
            CharacterId = characterId;
            MaxHp = maxHp;
            Hp = maxHp;
        }

        public override string ToString() => $"{CharacterId} {Hp}/{MaxHp}";
    }

    /// <summary>
    /// 런 하나의 진행 상태. <b>스테이지 = 1런이다</b> (D-68).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>이 객체는 저장되지 않는다</b> (D-56). 새로고침하면 런이 사라진다.
    /// 직렬화하려면 시드 스트림 위치·상점 풀·배타 제거 목록까지 전부 담아야 하는데,
    /// 그게 틀리면 <b>재현 불가능한 버그</b>가 생긴다. 이 프로젝트는 결정론이 전부라 그 위험이 더 크다.
    /// 저장되는 건 메타 진행도뿐이고 그건 `meta.json` 쪽이다.
    /// </para>
    /// <para>
    /// ★ <see cref="Currency"/> 는 <b>런 전용 재화</b>다. 런이 끝나면 소멸하고 이월되지 않는다.
    /// 영구 재화(metaCurrency)와 <b>절대 같은 필드에 담지 않는다</b> — 섞이면
    /// "런 안에서 잘하기"와 "런을 반복하기"가 같은 자원을 두고 경쟁해 밸런스 해석이 불가능해진다.
    /// </para>
    /// </remarks>
    public sealed class RunState
    {
        public string StageId { get; }

        /// <summary>현재 라운드 (1부터).</summary>
        public int Round { get; set; } = 1;

        public int Lives { get; set; }

        /// <summary>런 전용 재화. <b>영구 재화가 아니다.</b></summary>
        public int Currency { get; set; }

        /// <summary>출전 3명. 순서가 <b>슬롯 인덱스</b>이며 모든 동률 판정의 최종 타이브레이커다.</summary>
        public IReadOnlyList<RosterEntry> Deployed { get; }

        /// <summary>
        /// 배타 규칙으로 <b>런 종료까지 상점 풀에서 빠진</b> 스킬 id.
        /// </summary>
        /// <remarks>
        /// 이걸 상태로 들고 있어야 하는 이유 — 배타 선택이 이 게임의 정체성이고(08 §2.2),
        /// 제거된 쪽이 다시 나오면 선택의 무게가 사라진다.
        /// <b>List 인 이유는 순서를 보존하기 위해서다</b> — HashSet 순회는 결정론에 쓸 수 없다.
        /// </remarks>
        public List<string> RemovedSkillIds { get; } = new List<string>();

        /// <summary>아군 전체에 걸리는 아이템(`teamBoost`). 캐릭터를 지정하지 않는다.</summary>
        /// <remarks>
        /// 가격이 2배인 이유가 여기 있다 — 전원에게 걸리므로 기회비용을 가격으로 만든다.
        /// ⚠️ *"사면 무조건 이득"* 이라 선택의 무게가 없다는 우려가 있어 `M4` 로 감시한다
        /// (`economy.items.teamBoost._tension`).
        /// </remarks>
        public List<OwnedItem> TeamItems { get; } = new List<OwnedItem>();

        public RunState(string stageId, int lives, int startingCurrency, IReadOnlyList<RosterEntry> deployed)
        {
            StageId = stageId;
            Lives = lives;
            Currency = startingCurrency;
            Deployed = deployed;
        }

        /// <summary>살아 있는 출전 캐릭터가 하나도 없거나 생명이 0 이면 런이 끝난다.</summary>
        public bool IsOver
        {
            get
            {
                if (Lives <= 0) return true;
                foreach (var r in Deployed) if (r.IsAlive) return false;
                return true;
            }
        }

        public override string ToString() => $"{StageId} R{Round} 생명{Lives} 재화{Currency}";
    }
}
