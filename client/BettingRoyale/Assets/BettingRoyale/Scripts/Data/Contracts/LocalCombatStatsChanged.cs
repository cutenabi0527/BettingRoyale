using BettingRoyale.Core;
using BettingRoyale.Data.Combat;

namespace BettingRoyale.Data.Contracts
{
    public readonly struct LocalCombatStatsChanged : IGameEvent
    {
        public readonly bool Available;
        public readonly CombatStatsSnapshot Snapshot;
        public LocalCombatStatsChanged(bool available, in CombatStatsSnapshot snapshot)
        { Available = available; Snapshot = snapshot; }
    }

}
