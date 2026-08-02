// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using DomoNinja.Core.Domain;
using DomoNinja.Core.Rng;

namespace DomoNinja.Core.Economy
{
    /// <summary>
    /// 상점에서 <b>목표 빌드의 품목만</b> 사는 봇.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>사람의 실력을 흉내 내지 않는다.</b> 상황을 보고 판단하지도, 아이템을 사지도 않는다.
    /// 목표를 고정해두면 측정 대상이 <b>빌드 자체의 성립 가능성</b>으로 깨끗하게 분리된다 (`08` §6.1).
    /// 봇이 똑똑해지면 `M1`~`M7` 이 재는 게 게임이 아니라 봇의 판단력이 된다.
    /// </para>
    /// <para>
    /// 정책은 두 줄뿐이다:
    /// <code>
    /// ① 목표 액티브가 나오면 산다 — 빌드가 서지 않으면 나머지가 의미 없다
    /// ② 목표 보조가 나오면 산다 — "2개 다 산다"가 표준 정책이다 (§6.1)
    /// </code>
    /// </para>
    /// <para>
    /// ⚠️ <b>리롤하지 않는다.</b> 리롤은 "원하는 게 나올 때까지 재화를 태우는" 판단이고,
    /// 그게 들어가면 봇이 저축 전략을 흉내 내기 시작한다. 저축은 `M6` 가 따로 재는 축이라
    /// 봇에 넣으면 두 지표가 서로를 오염시킨다.
    /// </para>
    /// </remarks>
    public static class ShopBot
    {
        /// <summary>
        /// 이번 라운드 상점을 본다. <b>살 수 있는 목표 품목을 전부 산다.</b>
        /// </summary>
        /// <remarks>
        /// 재화가 모자라면 그냥 못 산다 — 다음 라운드에 다시 나오길 기다린다.
        /// 그 "못 삼"이 곧 밸런스 신호다. 재화를 넉넉히 주면 모든 빌드가 성립해
        /// <c>M3b</c>(선택률)가 평평해지고 배타 선택이 의미를 잃는다.
        /// </remarks>
        public static void Visit(RunState run, Shop shop, BuildTarget target,
                                DeterministicRandom rng, int round)
        {
            shop.Restock(run, rng, round);

            // 액티브를 먼저 훑는다. 같은 라운드에 액티브와 보조가 같이 나올 수 있는데,
            // 보조는 메인이 활성화돼 있어야 살 수 있으므로 순서가 결과를 바꾼다.
            Buy(run, shop, target, OfferKind.ActiveSkill);
            Buy(run, shop, target, OfferKind.SupportSkill);
        }

        private static void Buy(RunState run, Shop shop, BuildTarget target, OfferKind kind)
        {
            // 뒤에서부터 훑는다 — 구매에 성공하면 그 자리가 목록에서 빠져 인덱스가 밀린다.
            for (int i = shop.Offers.Count - 1; i >= 0; i--)
            {
                var offer = shop.Offers[i];
                if (offer.Kind != kind) continue;
                if (!IsWanted(target, offer)) continue;

                shop.TryBuy(run, i);
            }
        }

        private static bool IsWanted(BuildTarget target, ShopOffer offer)
        {
            if (offer.CharacterId == null) return false;

            switch (offer.Kind)
            {
                case OfferKind.ActiveSkill:
                    return target.ActiveByCharacter.TryGetValue(offer.CharacterId, out string? active)
                           && active == offer.Id;

                case OfferKind.SupportSkill:
                {
                    if (!target.SupportsByCharacter.TryGetValue(offer.CharacterId, out var supports))
                        return false;

                    for (int i = 0; i < supports.Count; i++)
                        if (supports[i] == offer.Id) return true;

                    return false;
                }

                default:
                    // 아이템은 빌드 공간의 축이 아니다. 사면 M4 가 아이템 효과까지 섞어 재게 된다.
                    return false;
            }
        }
    }
}
