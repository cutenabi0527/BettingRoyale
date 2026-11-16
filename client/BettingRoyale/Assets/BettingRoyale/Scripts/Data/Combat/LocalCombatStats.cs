using BettingRoyale.Core;
using BettingRoyale.Data.Contracts;

namespace BettingRoyale.Data.Combat
{
    /// <summary>로컬 관찰자의 마지막 확정값. 늦게 생성된 HUD도 같은 스냅샷을 받는다.</summary>
    public static class LocalCombatStats
    {
        public static bool Available { get; private set; }
        public static CombatStatsSnapshot Latest { get; private set; }
        private static int owner;

        public static void Publish(int source, in CombatStatsSnapshot snapshot)
        {
            if (Available && owner == source && Latest.Equals(snapshot)) return;
            owner = source; Latest = snapshot; Available = true;
            EventBus.Publish(new LocalCombatStatsChanged(true, snapshot));
        }

        public static void Clear(int source)
        {
            if (!Available || owner != source) return;
            Reset();
            EventBus.Publish(new LocalCombatStatsChanged(false, default));
        }

        public static void Reset() { Available = false; Latest = default; owner = 0; }
    }
}
