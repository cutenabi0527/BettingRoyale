namespace BettingRoyale.Data.Combat
{
    [System.Flags]
    public enum BlessingVisualFlags
    {
        None = 0, Shield = 1, Counter = 2, FixedLine = 4, Deadline = 8,
        Interest = 16, LastChip = 32, Parry = 64, Windup = 128, Mark = 256,
    }

    public readonly struct BlessingPresentation
    {
        public readonly BlessingVisualFlags Flags;
        public readonly float Shield;
        public readonly int MarkOwner;
        public readonly int RageTarget, ExtraProjectileCount;
        public readonly float ExtraProjectileSpreadDegrees, PreviewRangeMultiplier, PreviewHalfAngle;
        public readonly float AttackRangeMultiplier, AttackHalfAngle, ProjectileSpeedMultiplier, ProjectileRadius;

        public BlessingPresentation(BlessingVisualFlags flags, float shield, int markOwner,
            float attackRangeMultiplier = 1f, float attackHalfAngle = -1f,
            float projectileSpeedMultiplier = 1f, float projectileRadius = 0f,
            int rageTarget = 0, int extraProjectileCount = 0, float extraProjectileSpreadDegrees = 0f,
            float previewRangeMultiplier = 1f, float previewHalfAngle = -1f)
        {
            Flags = flags; Shield = shield; MarkOwner = markOwner;
            PreviewRangeMultiplier = previewRangeMultiplier; PreviewHalfAngle = previewHalfAngle;
            RageTarget = rageTarget; ExtraProjectileCount = extraProjectileCount; ExtraProjectileSpreadDegrees = extraProjectileSpreadDegrees;
            AttackRangeMultiplier = attackRangeMultiplier; AttackHalfAngle = attackHalfAngle;
            ProjectileSpeedMultiplier = projectileSpeedMultiplier; ProjectileRadius = projectileRadius;
        }
    }

    public interface ICombatBlessingPresentationDriver
    {
        BlessingPresentation CaptureBlessings();
        void ApplyBlessings(in BlessingPresentation presentation);
    }

    public interface ICombatStateAuthority
    {
        bool HasCombatStateAuthority { get; }
    }

    public interface ICombatAttackTickDriver
    {
        bool TickAttack(float now);
    }
}
