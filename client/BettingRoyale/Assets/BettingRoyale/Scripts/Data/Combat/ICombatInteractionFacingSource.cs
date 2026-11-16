using UnityEngine;

namespace BettingRoyale.Data.Combat
{
    /// <summary>Authority-owned facing for an accepted interaction; false releases the input constraint.</summary>
    public interface ICombatInteractionFacingSource
    {
        bool TryGetLockedFacing(out Vector2 direction);
    }
}
