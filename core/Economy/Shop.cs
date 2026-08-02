// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System.Collections.Generic;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Rng;
using Newtonsoft.Json.Linq;

namespace DomoNinja.Core.Economy
{
    /// <summary>상점 품목의 종류. 자리가 종류별로 고정돼 있다 (`D-69`).</summary>
    public enum OfferKind
    {
        /// <summary>액티브 스킬 활성화. <b>사는 순간 같은 캐릭터의 다른 하나가 사라진다.</b></summary>
        ActiveSkill,

        /// <summary>보조 스킬. 2·4·6 라운드에 <b>스킬 자리를 차지한다</b>(별도 칸 없음).</summary>
        SupportSkill,

        /// <summary>아이템 4종. <b>유일하게 팔 수 있다</b> (`D-70`).</summary>
        Item,
    }

    /// <summary>상점 한 자리에 놓인 품목.</summary>
    public readonly struct ShopOffer
    {
        public readonly OfferKind Kind;

        /// <summary>스킬 id 또는 아이템 키(`statBoost` 등).</summary>
        public readonly string Id;

        /// <summary>스킬이면 소유 캐릭터. 아이템이면 <c>null</c> — 대상은 <b>살 때</b> 고른다.</summary>
        public readonly string? CharacterId;

        public readonly int Price;

        public ShopOffer(OfferKind kind, string id, string? characterId, int price)
        {
            Kind = kind; Id = id; CharacterId = characterId; Price = price;
        }

        public override string ToString() => $"{Kind}:{Id}({Price})";
    }

    /// <summary>
    /// 상점. <b>이 게임의 랜덤성은 여기 하나뿐이다</b> (`08` §4.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 이게 없으면 적 고정 + 로스터 고정 + 스킬 2택이라 <b>첫 런에 최적해가 나오고 게임이 끝난다.</b>
    /// </para>
    /// <para>
    /// ★ <b>자리를 종류별로 고정한 게 핵심이다</b> (`D-69`).
    /// 한 풀에서 무작위로 5개를 뽑으면 *"이번 라운드엔 스킬이 하나도 안 나옴"* 이 생기고,
    /// `M6`(첫 활성화 라운드)이 그 노이즈를 그대로 먹는다.
    /// 자리를 나누면 <b>스킬은 매 라운드 반드시 3개 나온다</b> — 무엇이 나올지만 랜덤이다.
    /// </para>
    /// <para>
    /// ⚠️ <b>추첨은 상점 스트림에서만 뽑는다.</b> 전투·관문과 섞으면 전투의 난수 소비가
    /// 한 번만 달라져도 이후 상점이 통째로 밀린다 (`17` §6 CRN).
    /// </para>
    /// </remarks>
    public sealed class Shop
    {
        private static readonly string[] ItemKeys = { "statBoost", "teamBoost", "conditionalBoost", "healItem" };

        private readonly GameData _data;
        private readonly List<ShopOffer> _offers = new List<ShopOffer>();
        private readonly List<ShopOffer> _skillPool = new List<ShopOffer>();
        private readonly List<ShopOffer> _itemPool = new List<ShopOffer>();

        public IReadOnlyList<ShopOffer> Offers => _offers;

        public Shop(GameData data)
        {
            _data = data;
        }

        /// <summary>매 라운드 재구성. <b>스킬 3칸 + 아이템 2칸.</b></summary>
        public void Restock(RunState run, DeterministicRandom rng, int round)
        {
            BuildSkillPool(run, round);
            BuildItemPool();

            _offers.Clear();
            Draw(_skillPool, _data.Economy.ShopSkillSlots, rng, _offers);
            Draw(_itemPool, _data.Economy.ShopItemSlots, rng, _offers);
        }

        /// <summary>리롤. 재화가 모자라면 아무것도 하지 않는다.</summary>
        /// <remarks>
        /// 횟수 제한이 없다 — <b>재화가 곧 제한이다</b>(`economy.shop._rerollNote`).
        /// 팀원 원안은 최대 3회였는데, 비용으로 막는 쪽이 파라미터 하나로 조정된다.
        /// </remarks>
        public bool TryReroll(RunState run, DeterministicRandom rng, int round)
        {
            int cost = _data.Economy.RerollCost;
            if (run.Currency < cost) return false;

            run.Currency -= cost;
            Restock(run, rng, round);
            return true;
        }

        /// <summary>
        /// 산다. 성공하면 재화가 빠지고 <b>배타 규칙이 그 자리에서 발동한다.</b>
        /// </summary>
        /// <param name="targetCharacterId">
        /// 대상을 지정하는 아이템에만 쓴다. 스킬은 품목이 이미 소유자를 안다.
        /// </param>
        public bool TryBuy(RunState run, int offerIndex, string? targetCharacterId = null)
        {
            if (offerIndex < 0 || offerIndex >= _offers.Count) return false;

            var offer = _offers[offerIndex];
            if (run.Currency < offer.Price) return false;

            switch (offer.Kind)
            {
                case OfferKind.ActiveSkill:
                {
                    var entry = FindEntry(run, offer.CharacterId);
                    if (entry == null || entry.ActiveSkillId != null) return false;

                    entry.ActiveSkillId = offer.Id;

                    // ★ 배타 규칙 — 같은 캐릭터의 다른 액티브를 런 종료까지 풀에서 뺀다 (08 §2.2).
                    //   구매 "순간" 발동하는 게 저축 전략의 전제다 (§4.3).
                    var character = _data.FindCharacter(offer.CharacterId!);
                    if (character != null)
                    {
                        foreach (string id in character.SkillIds)
                            if (id != offer.Id && !run.RemovedSkillIds.Contains(id))
                                run.RemovedSkillIds.Add(id);
                    }
                    break;
                }

                case OfferKind.SupportSkill:
                {
                    var entry = FindEntry(run, offer.CharacterId);
                    if (entry == null) return false;

                    // 메인을 활성화한 뒤에야 살 수 있다 — 강화할 대상이 없으면 의미가 없다.
                    if (entry.ActiveSkillId == null) return false;
                    if (entry.SupportSkillIds.Count >= _data.Economy.SupportMaxSelectPerCharacter) return false;
                    if (entry.SupportSkillIds.Contains(offer.Id)) return false;

                    entry.SupportSkillIds.Add(offer.Id);
                    break;
                }

                case OfferKind.Item:
                {
                    string? owner = targetCharacterId;
                    if (TargetsCharacter(offer.Id))
                    {
                        var entry = FindEntry(run, owner);
                        if (entry == null || !entry.IsAlive) return false;
                        entry.Items.Add(offer.Id);
                    }
                    else
                    {
                        run.TeamItems.Add(offer.Id);
                    }
                    break;
                }
            }

            run.Currency -= offer.Price;
            _offers.RemoveAt(offerIndex);
            return true;
        }

        /// <summary>
        /// 아이템을 판다. <b>스킬은 팔 수 없다</b> (`D-70`).
        /// </summary>
        /// <remarks>
        /// 스킬을 팔 수 있으면 배타 규칙이 무너지고 <b>빌드 공간 전수 탐색의 전제가 통째로 날아간다.</b>
        /// 환급은 50% 다 — 손해를 남겨야 *"일단 사고 되팔기"* 가 정답이 되지 않는다.
        /// </remarks>
        public bool TrySellItem(RunState run, string characterId, string itemKey)
        {
            var entry = FindEntry(run, characterId);
            if (entry == null || !entry.Items.Remove(itemKey)) return false;

            run.Currency += Refund(PriceOf(itemKey));
            return true;
        }

        /// <inheritdoc cref="TrySellItem"/>
        public bool TrySellTeamItem(RunState run, string itemKey)
        {
            if (!run.TeamItems.Remove(itemKey)) return false;

            run.Currency += Refund(PriceOf(itemKey));
            return true;
        }

        private int Refund(int price)
        {
            var sell = (_data.Economy.Raw["shop"] as JObject)?["sell"] as JObject;
            return Combat.Permille.Apply(price, (int?)sell?["refundPermille"] ?? 0);
        }

        // ────────────────────────────── 풀 구성

        private void BuildSkillPool(RunState run, int round)
        {
            _skillPool.Clear();
            bool supportRound = IsSupportRound(round);

            foreach (var entry in run.Deployed)
            {
                // 죽은 캐릭터의 품목은 상점에서 뺀다 — 살 이유가 없고, 자리만 먹으면
                // 살아 있는 캐릭터의 기회가 줄어 M6 가 사망 여부에 끌려다닌다.
                if (!entry.IsAlive) continue;

                var character = _data.FindCharacter(entry.CharacterId);
                if (character == null) continue;

                if (entry.ActiveSkillId == null)
                {
                    foreach (string id in character.SkillIds)
                    {
                        if (run.RemovedSkillIds.Contains(id)) continue;
                        _skillPool.Add(new ShopOffer(OfferKind.ActiveSkill, id, character.Id, SkillPrice()));
                    }
                    continue;
                }

                // 보조는 지정된 라운드에만, 그리고 메인을 활성화한 캐릭터에게만.
                if (!supportRound) continue;
                if (entry.SupportSkillIds.Count >= _data.Economy.SupportMaxSelectPerCharacter) continue;

                foreach (var support in _data.SupportSkills)
                {
                    if (support.CharacterId != character.Id) continue;
                    if (entry.SupportSkillIds.Contains(support.Id)) continue;

                    _skillPool.Add(new ShopOffer(OfferKind.SupportSkill, support.Id, character.Id,
                                                 SupportPrice(entry.SupportSkillIds.Count)));
                }
            }
        }

        private void BuildItemPool()
        {
            _itemPool.Clear();
            foreach (string key in ItemKeys)
                _itemPool.Add(new ShopOffer(OfferKind.Item, key, null, PriceOf(key)));
        }

        /// <summary>풀에서 <paramref name="count"/> 개를 <b>중복 없이</b> 뽑는다.</summary>
        /// <remarks>
        /// 풀이 자리보다 작으면 있는 만큼만 나온다. 빈 자리를 아이템으로 메우지 않는 이유 —
        /// 스킬 자리가 아이템으로 채워지면 `D-69` 가 막으려던 상황(스킬이 안 나옴)이 다시 생긴다.
        /// </remarks>
        private static void Draw(List<ShopOffer> pool, int count, DeterministicRandom rng, List<ShopOffer> into)
        {
            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                int pick = rng.NextInt(pool.Count);
                into.Add(pool[pick]);
                pool.RemoveAt(pick);
            }
        }

        private bool IsSupportRound(int round)
        {
            var support = ((_data.Economy.Raw["shop"] as JObject)?["supportSkill"]) as JObject;
            if (!(support?["appearRounds"] is JArray rounds)) return false;

            foreach (var r in rounds)
                if ((int?)r == round) return true;
            return false;
        }

        private static RosterEntry? FindEntry(RunState run, string? characterId)
        {
            if (characterId == null) return null;

            foreach (var e in run.Deployed)
                if (e.CharacterId == characterId) return e;
            return null;
        }

        private JObject Prices => _data.Economy.Raw["prices"] as JObject ?? new JObject();

        private int SkillPrice() => (int?)Prices["skillActivate"] ?? 0;

        /// <summary>보조는 <b>몇 번째냐에 따라 값이 다르다</b>. `prices.supportSkill: [3, 5]`</summary>
        private int SupportPrice(int alreadyOwned)
        {
            if (!(Prices["supportSkill"] is JArray steps) || steps.Count == 0) return 0;

            int index = alreadyOwned < steps.Count ? alreadyOwned : steps.Count - 1;
            return (int?)steps[index] ?? 0;
        }

        private int PriceOf(string itemKey) => (int?)Prices[itemKey] ?? 0;

        /// <summary>캐릭터를 지정해서 사는 아이템인가. `teamBoost` 만 전체 대상이다.</summary>
        private bool TargetsCharacter(string itemKey)
        {
            var items = _data.Economy.Raw["items"] as JObject;
            var item = items?[itemKey] as JObject;
            return (bool?)item?["targetsSpecificCharacter"] ?? false;
        }
    }
}
