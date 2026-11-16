using System.Collections.Generic;

namespace BettingRoyale.Data.Combat
{
    /// <summary>
    /// 소모품 효과를 HUD 효과 칸에 올리는 쪽 — 축복 효과와 <b>같은 줄</b>에 선다 (S-UX-3ESB5T).
    /// </summary>
    /// <remarks>
    /// 권위 피어에서만 값이 있다(효과가 권위 피어에서만 걸린다). 다른 피어의 화면은 이 목록을 복제로 받는다.
    /// </remarks>
    public interface IConsumableHudEffectSource
    {
        /// <summary>켜진 소모품 효과를 <paramref name="target"/> 뒤에 덧붙인다. 비우지 않고 순서도 정하지 않는다.</summary>
        void AppendHudEffects(int sourceId, List<HudStatusEffect> target);
    }
}
