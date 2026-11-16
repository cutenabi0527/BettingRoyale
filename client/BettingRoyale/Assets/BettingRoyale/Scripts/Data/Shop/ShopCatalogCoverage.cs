using System.Collections.Generic;
using BettingRoyale.Data.Equipment;

namespace BettingRoyale.Data.Shop
{
    /// <summary>
    /// "진열한 칸을 <b>정말로</b> 지급할 수 있는가" 를 한 번에 묻는 자리.
    ///
    /// <para>
    /// <b>왜 따로 있는가.</b> 가격표(<see cref="ShopCatalog"/>)와 장비 정의
    /// (<see cref="IEquipmentItemCatalog"/>)는 소유자가 다르다 — 전자는 데이터 파이프라인(D-041),
    /// 후자는 아트다. 그래서 <b>한쪽만 바뀌는 일이 정상적으로 일어나고</b>, 어긋나면
    /// "목록에는 보이는데 사면 거절되는 칸" 이 생기는데 컴파일러는 아무 말도 안 한다
    /// (D-CLI-R69V68 이 배선으로 막으려던 것과 같은 결함의 데이터 쪽 얼굴이다).
    /// </para>
    ///
    /// <para>
    /// 판정은 <see cref="ShopRequestGate"/> 의 진열·구매와 <b>같은 조건</b>이어야 한다 —
    /// 그래서 규칙을 여기서 다시 쓰지 않고 <see cref="ShopRequestGate.IsSellable"/> 를 부른다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public static class ShopCatalogCoverage
    {
        /// <summary>
        /// 진열됐지만 <paramref name="items"/> 가 지급할 수 없는 칸을 모은다.
        /// <b>빈 목록이 정상이다.</b>
        /// </summary>
        public static IReadOnlyList<ShopOffer> FindUnsellable(
            ShopCatalog catalog, IEquipmentItemCatalog items)
        {
            var unsellable = new List<ShopOffer>();

            if (catalog == null || items == null)
            {
                return unsellable;
            }

            for (int i = 0; i < catalog.Count; i++)
            {
                ShopOffer offer = catalog.Offers[i];

                if (!ShopRequestGate.IsSellable(offer, items, out _))
                {
                    unsellable.Add(offer);
                }
            }

            return unsellable;
        }

        /// <summary>모든 진열 칸을 지급할 수 있으면 true.</summary>
        public static bool CoversEveryOffer(ShopCatalog catalog, IEquipmentItemCatalog items) =>
            FindUnsellable(catalog, items).Count == 0;

        /// <summary>진단 로그·테스트 실패 메시지에 쓸 한 줄.</summary>
        public static string Describe(IReadOnlyList<ShopOffer> unsellable)
        {
            if (unsellable == null || unsellable.Count == 0)
            {
                return "모든 진열 칸을 지급할 수 있습니다.";
            }

            var builder = new System.Text.StringBuilder();
            builder.Append("장비 정의가 없어 팔 수 없는 칸 ").Append(unsellable.Count).Append("개:");

            for (int i = 0; i < unsellable.Count; i++)
            {
                builder.Append(' ').Append(unsellable[i].ToString());
            }

            return builder.ToString();
        }
    }
}
