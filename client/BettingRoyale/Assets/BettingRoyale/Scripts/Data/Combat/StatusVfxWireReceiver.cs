using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Combat
{
    /// <summary>Atomic snapshot replacement and per-generation exactly-once pulse acceptance.</summary>
    public sealed class StatusVfxWireReceiver
    {
        private int lastEventSequence;
        private bool hasJoined;
        private StatusVfxScope scope;
        private int generation = -1;
        private long snapshotRevision;
        private long eventBaselineRevision;
        public int Generation => generation;
        public bool HasSnapshot { get; private set; }
        public IReadOnlyList<StatusVfxState> Snapshot { get; private set; } = Array.Empty<StatusVfxState>();
        public bool TryAccept(StatusVfxWireFrame frame, StatusVfxScope expectedScope, out string reason)
        {
            reason = null;
            if (frame == null || !frame.Scope.Equals(expectedScope)) { reason = "scope-mismatch"; return false; }
            if (!frame.IsEvent)
            {
                var keys = new HashSet<StatusVfxKey>();
                foreach (var value in frame.States)
                    if (!keys.Add(value.Key)) { reason = "duplicate-instance"; return false; }
            }
            bool newScope = !scope.Equals(expectedScope);
            if (!newScope && frame.Generation < generation) { reason = "stale-generation"; return false; }
            if (newScope || frame.Generation > generation)
            {
                if (frame.IsEvent) { reason = "snapshot-required"; return false; }
                Clear(preserveJoined: true); scope = expectedScope; generation = frame.Generation;
            }
            if (frame.IsEvent)
            {
                if (!HasSnapshot || frame.States.Count != 1) { reason = "snapshot-required"; return false; }
                if (frame.Revision <= eventBaselineRevision) { reason = "historical-event"; return false; }
                if (frame.States[0].EventSequence <= lastEventSequence) { reason = "duplicate-event"; return false; }
                lastEventSequence = frame.States[0].EventSequence;
                return true;
            }
            if (frame.Revision <= snapshotRevision) { reason = "stale-snapshot"; return false; }
            if (!hasJoined) eventBaselineRevision = frame.Revision;
            hasJoined = true;
            Snapshot = frame.States; snapshotRevision = frame.Revision; HasSnapshot = true; return true;
        }
        public void Clear(bool preserveJoined = false)
        { if (!preserveJoined) hasJoined = false; lastEventSequence = 0; generation = -1; snapshotRevision = 0; eventBaselineRevision = 0; HasSnapshot = false; Snapshot = Array.Empty<StatusVfxState>(); scope = default; }
    }
}
