using System;

namespace BettingRoyale.Data.Blessing
{
    public sealed partial class BlessingCombatState
    {
        private readonly float[] lastShieldAbsorption = new float[ShieldSourceCount];

        /// <summary>Exact consumption by the latest BeforeDamage call, read before AfterDamage.</summary>
        public float LastHitAbsorbedByShieldFor(string effect)
        {
            int slot = ShieldSlot(effect);
            return slot >= 0 ? lastShieldAbsorption[slot] : 0f;
        }

        /// <summary>Read-only source projection. Unknown sources never inherit aggregate shield state.</summary>
        public float ShieldAmountFor(string effect)
        {
            int slot = ShieldSlot(effect);
            return slot >= 0 && shieldExpiry[slot] > time ? Math.Max(0f, shieldAmount[slot]) : 0f;
        }

        public float ShieldRemainingFor(string effect)
        {
            int slot = ShieldSlot(effect);
            return slot >= 0 && shieldAmount[slot] > 0f ? Math.Max(0f, shieldExpiry[slot] - time) : 0f;
        }

        public string SourceBlessingIdFor(string effect) =>
            byEffect.TryGetValue(effect, out var definition) ? definition.Definition.Id : string.Empty;

        public float CornerPocketMoveRemaining => cornerPocket != null ? Math.Max(0f, cornerMoveUntil - time) : 0f;

        private static int ShieldSlot(string effect)
        {
            switch (effect)
            {
                case EffectSafeAsset: return ShieldSafeAsset;
                case EffectInsurance: return ShieldInsurance;
                case EffectSpareChip: return ShieldSpareChip;
                case EffectParryDividend: return ShieldParry;
                default: return -1;
            }
        }
    }
}
