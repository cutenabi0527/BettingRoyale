using BettingRoyale.Data.Chest;

namespace BettingRoyale.Data.Combat
{
    /// <summary>개봉 집중 취소와 같은 프레임의 재개봉 방지 경계 (S-GD-68SAK2).</summary>
    public interface ICombatInteractionFocusSink : ICombatInteractionCommandSink
    {
        bool CancelOpening(ChestCancelReason reason);
        bool TryHandleInteraction(bool allowBeginOpen);
    }
}
