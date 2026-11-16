using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Shop
{
    /// <summary>
    /// 상점에 진열된 상품 목록 (S-GD-PVVHP9).
    ///
    /// <para>
    /// <b>이 클래스에는 일부러 <c>Default</c> 가 없다.</b> 상품 구성과 가격은 확정된 적이 없고
    /// (SoT §3.5 는 "소모품·장신구를 구매·교체한다" 까지만 정했다), 확정되더라도 <b>코드 상수가 아니라
    /// 데이터로 들어온다</b>(D-041). 기본값을 두면 그 숫자가 데이터를 우회해 사실상의 스펙으로 굳는다.
    /// 지금 쓸 임시 목록은 <see cref="PlaceholderShopCatalog"/> 에 따로 두었다 — 이름만 봐도
    /// 임시라는 것이 드러나고, 진짜 데이터가 들어올 때 지울 자리가 명확하다.
    /// </para>
    ///
    /// <para>
    /// <b>목록은 만들 때 고정된다.</b> 라운드마다 상품이 바뀌는 규칙은 아직 없으므로(미정),
    /// 바뀌는 것을 지금 흉내 내지 않는다. 바뀌게 되면 이 클래스를 라운드별로 새로 만들어 넘기면 되고,
    /// 게이트는 손대지 않아도 된다.
    /// </para>
    ///
    /// <para>
    /// <c>UnityEngine</c> 을 참조하지 않는다 (D-060).
    /// </para>
    /// </summary>
    public sealed class ShopCatalog
    {
        private readonly ShopOffer[] offers;
        private readonly Dictionary<int, int> indexByOfferId;

        /// <param name="offers">
        /// 진열할 상품. <b>칸 번호가 겹치면 던진다</b> — 겹친 채로 두면 뒤엣것이 앞엣것을 조용히
        /// 가려서, 화면에는 두 칸이 보이는데 어느 쪽을 눌러도 같은 물건이 나온다.
        /// </param>
        public ShopCatalog(IReadOnlyList<ShopOffer> offers)
        {
            if (offers == null)
            {
                throw new ArgumentNullException(nameof(offers));
            }

            this.offers = new ShopOffer[offers.Count];
            indexByOfferId = new Dictionary<int, int>(offers.Count);

            for (int i = 0; i < offers.Count; i++)
            {
                var offer = offers[i];

                if (indexByOfferId.ContainsKey(offer.OfferId))
                {
                    throw new ArgumentException(
                        $"상품 칸 번호가 중복됩니다: {offer.OfferId}. 칸 번호는 목록 안에서 유일해야 합니다.",
                        nameof(offers));
                }

                this.offers[i] = offer;
                indexByOfferId[offer.OfferId] = i;
            }
        }

        /// <summary>진열 순서대로의 상품 목록. 화면이 그대로 그린다.</summary>
        public IReadOnlyList<ShopOffer> Offers => offers;

        public int Count => offers.Length;

        /// <summary>칸 번호로 상품을 찾는다. 없으면 <c>false</c> — 진열되지 않은 칸을 요청한 것이다.</summary>
        public bool TryGet(int offerId, out ShopOffer offer)
        {
            if (indexByOfferId.TryGetValue(offerId, out int index))
            {
                offer = offers[index];
                return true;
            }

            offer = default;
            return false;
        }

        public override string ToString() => $"상점 {offers.Length}칸";
    }
}
