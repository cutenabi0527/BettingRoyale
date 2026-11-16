using BettingRoyale.Data.Combat;

namespace BettingRoyale.Data.Chest
{
    /// <summary>
    /// Fusion adapter가 소비하는 Host chest authority 경계. Network 계층은 Gameplay runtime 구체형을 참조하지
    /// 않으며, Host만 이 계약을 tick/cancel/publish한다.
    /// </summary>
    public interface IChestAuthorityRuntime
    {
        int Version { get; }
        int Round { get; }
        BettingRoyale.Data.Common.WorldPosition GetChestPosition(int chestId);
        void Tick();
        bool CancelFor(CombatantId combatantId, ChestCancelReason reason);
        bool TryGetState(int chestId, out ChestAuthorityState state);
        bool TryGetClaimSnapshot(int chestId, out int sequence, out int claimerId);
    }
}
