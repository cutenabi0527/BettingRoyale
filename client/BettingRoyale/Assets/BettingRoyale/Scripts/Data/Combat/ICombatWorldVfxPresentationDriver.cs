using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Combat
{
    /// <summary>Presentation-only bridge; transport never references gameplay classes or applies damage.</summary>
    public interface ICombatWorldVfxPresentationDriver
    {
        int WorldVfxGeneration { get; }
        event Action<StatusVfxState> WorldVfxEmitted;
        void CaptureWorldVfx(List<StatusVfxState> target, float presentationTime);
        void ApplyWorldVfxSnapshot(IReadOnlyList<StatusVfxState> values, float authorityNow, float localNow);
        void ApplyWorldVfxEvent(in StatusVfxState value, float authorityNow, float localNow);
        void ClearRemoteWorldVfx();
    }
}
