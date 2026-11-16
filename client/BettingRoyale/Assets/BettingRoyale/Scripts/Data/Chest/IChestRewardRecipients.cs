using BettingRoyale.Data.Equipment;

namespace BettingRoyale.Data.Chest
{
    /// <summary>
    /// 상자 보상을 <b>실제로 넣을 곳</b> — 실행 경로(제품 Host 등)가 구현한다 (S-GD-DDXPX9 · S-GD-RVVB4H §7).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ChestRewardPayout"/> 는 무엇을 줄지만 정하고 지갑·인벤토리를 모른다(D-060). 이 경계가 그 둘을 잇는다.
    /// </para>
    /// <para>
    /// ⚠️ <b>경제(지갑)를 인벤토리 지급 도중에 건드리지 않는다</b> — <c>IShopInventoryProvider</c> 와 같은 규칙이다.
    /// 지급 순서는 아이템 → 골드 → 과잉 보상이고, 서로의 콜백 안에서 다른 쪽을 부르지 않는다.
    /// </para>
    /// </remarks>
    public interface IChestRewardRecipients
    {
        /// <summary>
        /// 이 사람에게 <b>지금</b> 보상을 줄 수 있으면 그 사람의 권위 인벤토리를 내준다.
        /// </summary>
        /// <returns>
        /// 라운드가 이미 끝났으면 false 다 — 개봉 완료와 라운드 종료가 겹치면 라운드 종료가 이긴다(S-GD-DFG3VR).
        /// 전투원이 없거나 권위 피어가 아니어도 false. false 면 골드·아이템·과잉 보상 <b>무엇도 주지 않는다.</b>
        /// </returns>
        bool TryResolveRecipient(int playerId, out IEquipmentInventoryDriver inventory);

        /// <summary>골드를 넣고 방 전체에 알린다. 넣었으면 true.</summary>
        bool TryGrantGold(int playerId, int amount);
    }
}
