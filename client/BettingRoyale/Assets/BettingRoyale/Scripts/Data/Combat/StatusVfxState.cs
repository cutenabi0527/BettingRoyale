using System;
using BettingRoyale.Data.Common;

namespace BettingRoyale.Data.Combat
{
    /// <summary>Session-local presentation identity; no damage or authority is implied.</summary>
    public readonly struct StatusVfxKey : IEquatable<StatusVfxKey>
    {
        public readonly StatusVfxKind Kind;
        public readonly string SourceId;
        public readonly int SourceCombatantId;
        public readonly int TargetCombatantId;
        public readonly int InstanceId;

        public StatusVfxKey(StatusVfxKind kind, string sourceId, int sourceCombatantId,
            int targetCombatantId, int instanceId)
        {
            Kind = kind;
            SourceId = sourceId ?? string.Empty;
            SourceCombatantId = sourceCombatantId;
            TargetCombatantId = targetCombatantId;
            InstanceId = instanceId;
        }

        public bool Equals(StatusVfxKey other) => Kind == other.Kind &&
            string.Equals(SourceId ?? string.Empty, other.SourceId ?? string.Empty, StringComparison.Ordinal) &&
            SourceCombatantId == other.SourceCombatantId && TargetCombatantId == other.TargetCombatantId &&
            InstanceId == other.InstanceId;
        public override bool Equals(object other) => other is StatusVfxKey key && Equals(key);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(SourceId ?? string.Empty);
                hash = hash * 397 ^ SourceCombatantId;
                hash = hash * 397 ^ TargetCombatantId;
                return hash * 397 ^ InstanceId;
            }
        }
        public static bool operator ==(StatusVfxKey left, StatusVfxKey right) => left.Equals(right);
        public static bool operator !=(StatusVfxKey left, StatusVfxKey right) => !left.Equals(right);
        public override string ToString() => $"{Kind}/{SourceId}/{SourceCombatantId}>{TargetCombatantId}/{InstanceId}";
    }

    /// <summary>Times use the caller's monotonic clock, in seconds. Infinity means a persistent effect.</summary>
    public readonly struct StatusVfxState
    {
        public readonly StatusVfxKey Key;
        public readonly float StartTime;
        public readonly float EndTime;
        public readonly float Magnitude;
        public readonly int Phase;
        public readonly int EventSequence;
        public readonly WorldPosition Position;
        public readonly WorldPosition Direction;
        public readonly float Radius;

        public StatusVfxState(StatusVfxKey key, float startTime, float endTime, float magnitude = 1f,
            int phase = 0, int eventSequence = 0, WorldPosition position = default,
            WorldPosition direction = default, float radius = 1f)
        {
            Key = key;
            StartTime = startTime;
            EndTime = endTime;
            Magnitude = magnitude;
            Phase = phase;
            EventSequence = eventSequence;
            Position = position;
            Direction = direction;
            Radius = radius;
        }

        public bool IsActiveAt(float now) => now >= StartTime && now < EndTime;
    }
}
