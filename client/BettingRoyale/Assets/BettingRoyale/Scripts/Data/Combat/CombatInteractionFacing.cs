using UnityEngine;

namespace BettingRoyale.Data.Combat
{
    public static class CombatInteractionFacing
    {
        public static Vector2 Capture(Vector3 actorPosition, Vector3 chestPosition)
        {
            Vector3 delta = chestPosition - actorPosition;
            Vector2 direction = new(delta.x, delta.z);
            return direction.sqrMagnitude > 0f ? direction.normalized : Vector2.up;
        }

        public static bool ApplyLockedAim(IAimCommandHandler motion, ICombatInteractionCommandSink sink)
        {
            if (sink is not ICombatInteractionFacingSource source || !source.TryGetLockedFacing(out Vector2 facing))
                return false;
            motion.Handle(new AimCommand(facing));
            return true;
        }
    }
}
