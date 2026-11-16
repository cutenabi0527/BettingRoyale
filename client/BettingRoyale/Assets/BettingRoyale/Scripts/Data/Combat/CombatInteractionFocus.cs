using BettingRoyale.Data.Chest;
using UnityEngine;

namespace BettingRoyale.Data.Combat
{
    /// <summary>Local/Host가 조준·행동 실행 전에 적용하는 개봉 집중 계약.</summary>
    public static class CombatInteractionFocus
    {
        public static bool InterruptForInput(ICombatInteractionCommandSink sink, Vector2 move,
            bool attackPressed, bool dashPressed, bool isDashing)
        {
            ChestCancelReason reason;
            if (move.sqrMagnitude > 0f) reason = ChestCancelReason.Moved;
            else if (dashPressed || isDashing) reason = ChestCancelReason.Dashed;
            else if (attackPressed) reason = ChestCancelReason.Attacked;
            else return false;

            (sink as ICombatInteractionFocusSink)?.CancelOpening(reason);
            return true;
        }

        public static bool TryHandleInteraction(ICombatInteractionCommandSink sink, bool interrupted) =>
            sink is ICombatInteractionFocusSink focus
                ? focus.TryHandleInteraction(allowBeginOpen: !interrupted)
                : sink != null && sink.TryHandleInteraction();
    }
}
