using BettingRoyale.Data.Combat;

namespace BettingRoyale.Data.Chest
{
    /// <summary>전투원 식별자를 세션 PlayerId로 바꾸는 제품 경계. 둘이 같다고 가정하지 않는다.</summary>
    public interface IChestPlayerIdMapper
    {
        bool TryGetPlayerId(CombatantId combatantId, out int playerId);
    }
}
