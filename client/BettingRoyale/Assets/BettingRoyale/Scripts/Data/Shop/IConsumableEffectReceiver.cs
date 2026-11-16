using System.Collections.Generic;

namespace BettingRoyale.Data.Shop
{
    /// <summary>
    /// 아이템을 쓰지 않고 <b>소모품 효과만</b> 받는다 — 상자 과잉 보상이 쓴다 (S-GD-DDXPX9 §4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>사용 경로와 같은 적용부를 탄다.</b> 회복은 체력에, 지속형은 같은 효과 칸에 걸린다 — 과잉 보상의 이동 속도가
    /// 먼저 걸려 있던 물약 이동 속도를 덮어쓰는 것(S-GD-RVVB4H §5)도 그래서 저절로 지켜진다.
    /// </para>
    /// <para>
    /// ⚠️ <b>사용 차단(대시·공격·상호작용 중)을 보지 않는다.</b> 마시는 행동이 아니라 보상이다. 상자 수령 자체가
    /// 상호작용이라, 차단을 보면 과잉 보상이 언제나 거절된다.
    /// </para>
    /// <para>
    /// 권위 피어에서만 부른다. 비권위 피어에 걸면 그 화면에서만 잠깐 보이고 복제가 덮어쓴다.
    /// </para>
    /// </remarks>
    public interface IConsumableEffectReceiver
    {
        /// <param name="sourceItemId">
        /// 효과를 건 근원. HUD 가 이 근원의 그림 한 칸으로 묶는다 — 상자 과잉 보상은
        /// <see cref="ShopItemEffectCatalog.ChestOverflowSourceId"/> (S-UX-XEC4CX).
        /// </param>
        /// <returns>하나라도 걸었으면 true. 효과 목록이 비었거나 받을 몸이 없으면 false.</returns>
        bool TryAuthorityApplyConsumableEffects(IReadOnlyList<ConsumableEffect> effects, int sourceItemId);
    }
}
