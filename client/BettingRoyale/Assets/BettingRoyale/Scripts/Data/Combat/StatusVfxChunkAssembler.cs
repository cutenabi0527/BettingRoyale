using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BettingRoyale.Data.Combat
{
    /// <summary>
    /// Reliable per-peer sender queues transmit every transfer contiguously, in increasing ID order.
    /// One logical frame may contain any number of effects/chunks; replay bookkeeping remains constant.
    /// </summary>
    public sealed class StatusVfxChunkAssembler
    {
        private sealed class Transfer
        {
            public long Id;
            public int Count, Bytes;
            public double LastProgress;
            public readonly Dictionary<int, byte[]> Parts = new Dictionary<int, byte[]>();
        }
        private readonly int packetBytes;
        private readonly Func<double> clock;
        private readonly double progressTimeout;
        private Transfer pending;
        private long completedThrough;
        private bool needsResync;
        public int PendingTransferCount => pending == null ? 0 : 1;
        public int RetainedReplayWatermarkCount => completedThrough > 0 ? 1 : 0;
        public StatusVfxChunkAssembler(int packetBytes, Func<double> clock = null, double progressTimeout = 30d)
        {
            if (packetBytes <= 0) throw new ArgumentOutOfRangeException(nameof(packetBytes));
            if (!(progressTimeout > 0d) || double.IsInfinity(progressTimeout)) throw new ArgumentOutOfRangeException(nameof(progressTimeout));
            this.packetBytes = packetBytes;
            this.clock = clock ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
            this.progressTimeout = progressTimeout;
        }

        public static IReadOnlyList<byte[]> Split(byte[] payload, int packetBytes)
        {
            if (payload == null || payload.Length == 0) throw new ArgumentException("A frame must contain bytes.", nameof(payload));
            if (packetBytes <= 0) throw new ArgumentOutOfRangeException(nameof(packetBytes));
            var result = new List<byte[]>();
            for (int offset = 0; offset < payload.Length;)
            {
                int size = Math.Min(packetBytes, payload.Length - offset);
                var part = new byte[size]; Buffer.BlockCopy(payload, offset, part, 0, size); result.Add(part); offset += size;
            }
            return result;
        }

        public bool TryAdd(long transferId, int index, int count, int totalBytes, byte[] part,
            out byte[] assembled, out string error)
        {
            assembled = null; error = null;
            if (transferId <= 0 || totalBytes <= 0 || count <= 0 || index < 0 || index >= count || part == null ||
                count != ((long)totalBytes + packetBytes - 1) / packetBytes ||
                part.Length != Math.Min(packetBytes, totalBytes - (long)index * packetBytes))
            { error = "invalid-chunk"; needsResync = true; return false; }
            PruneExpired();
            if (transferId <= completedThrough) return false;
            if (pending != null && transferId != pending.Id)
            {
                // This cannot occur on the declared reliable contiguous stream. Retain no orphan frames.
                if (transferId < pending.Id) { error = "out-of-order-transfer"; needsResync = true; return false; }
                completedThrough = Math.Max(completedThrough, pending.Id);
                pending = null; needsResync = true;
            }
            if (pending == null) pending = new Transfer { Id = transferId, Count = count, Bytes = totalBytes, LastProgress = clock() };
            var transfer = pending;
            if (transfer.Count != count || transfer.Bytes != totalBytes)
            { error = "inconsistent-chunk-envelope"; needsResync = true; return false; }
            if (transfer.Parts.TryGetValue(index, out var existing))
            {
                for (int i = 0; i < part.Length; i++) if (existing[i] != part[i])
                    { error = "conflicting-duplicate"; needsResync = true; return false; }
                return false;
            }
            transfer.Parts.Add(index, (byte[])part.Clone());
            transfer.LastProgress = clock();
            if (transfer.Parts.Count != count) return false;
            assembled = new byte[totalBytes];
            for (int i = 0; i < count; i++) Buffer.BlockCopy(transfer.Parts[i], 0, assembled, i * packetBytes, transfer.Parts[i].Length);
            pending = null; completedThrough = transferId; return true;
        }

        /// <summary>Timeout concerns lack of progress, never total frame size or duration while chunks advance.</summary>
        public bool PruneExpired()
        {
            if (pending == null || clock() - pending.LastProgress < progressTimeout) return false;
            completedThrough = Math.Max(completedThrough, pending.Id); pending = null; needsResync = true; return true;
        }
        public bool ConsumeResyncRequest() { bool value = needsResync; needsResync = false; return value; }
        public void Clear() { pending = null; completedThrough = 0; needsResync = false; }
    }
}
