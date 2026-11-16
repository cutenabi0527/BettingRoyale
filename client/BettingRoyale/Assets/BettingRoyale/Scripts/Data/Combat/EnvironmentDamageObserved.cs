using BettingRoyale.Core;

namespace BettingRoyale.Data.Combat
{
    public readonly struct EnvironmentDamageObserved : IGameEvent
    {
        public readonly CombatDamageSource Source;
        public readonly CombatantId Target;
        public readonly float PlannedDamage, AppliedDamage, RemainingHealth;
        public readonly string Suppression;
        public readonly bool IsDowned;
        public EnvironmentDamageObserved(CombatDamageSource source, CombatantId target, float planned,
            float applied, float remaining, string suppression, bool isDowned)
        {
            Source = source; Target = target; PlannedDamage = planned; AppliedDamage = applied;
            RemainingHealth = remaining; Suppression = suppression; IsDowned = isDowned;
        }
    }
}
