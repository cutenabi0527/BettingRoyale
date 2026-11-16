using System;
using System.Collections.Generic;
using System.Linq;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Common;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    [TestFixture]
    public sealed class StatusVfxWireTests
    {
        private static readonly StatusVfxScope Scope = new StatusVfxScope(1, "match-a", 2, "scene", "epoch-a");
        private static StatusVfxState State(int id = 1, int sequence = 0, float end = 15f) =>
            new StatusVfxState(new StatusVfxKey(StatusVfxKind.Poison, "poison-arrow-t2-출처-" + new string('x', 100), 10001, 10002, id),
                10f, end, 2f, 1, sequence, new WorldPosition(1, 2, 3), new WorldPosition(4, 5, 6), 3.2f);
        private static StatusVfxWireFrame Frame(long revision, params StatusVfxState[] states) => new StatusVfxWireFrame(Scope, 1, revision, 11f, false, states);

        [Test]
        public void CodecRetainsIdentityAllNumbersAndLongUnicodeSources()
        {
            var expected = State();
            Assert.That(StatusVfxWireCodec.TryDecode(StatusVfxWireCodec.Encode(Frame(1, expected)), out var decoded, out var error), Is.True, error);
            var actual = decoded.States[0];
            Assert.That(actual.Key, Is.EqualTo(expected.Key));
            Assert.That(actual.StartTime, Is.EqualTo(10)); Assert.That(actual.EndTime, Is.EqualTo(15));
            Assert.That(actual.Magnitude, Is.EqualTo(2)); Assert.That(actual.Phase, Is.EqualTo(1));
            Assert.That(actual.Position.Z, Is.EqualTo(3)); Assert.That(actual.Direction.X, Is.EqualTo(4));
            Assert.That(actual.Radius, Is.EqualTo(3.2f)); Assert.That(decoded.Scope, Is.EqualTo(Scope));
        }

        [Test]
        public void MoreThanThirtyThreeInstancesTravelInReverseChunksWithoutTruncation()
        {
            var original = Frame(1, Enumerable.Range(1, 160).Select(i => State(i)).ToArray());
            byte[] wire = StatusVfxWireCodec.Encode(original);
            var chunks = StatusVfxChunkAssembler.Split(wire, 128);
            var assembler = new StatusVfxChunkAssembler(128);
            byte[] completed = null;
            for (int i = chunks.Count - 1; i >= 0; i--)
            {
                bool ready = assembler.TryAdd(7, i, chunks.Count, wire.Length, chunks[i], out var data, out var error);
                Assert.That(error, Is.Null); Assert.That(ready, Is.EqualTo(i == 0));
                if (data != null) completed = data;
            }
            Assert.That(StatusVfxWireCodec.TryDecode(completed, out var decoded, out _), Is.True);
            Assert.That(decoded.States.Count, Is.EqualTo(160));
            Assert.That(decoded.States.Select(x => x.Key), Is.EqualTo(original.States.Select(x => x.Key)));
        }

        [Test]
        public void DuplicateChunksAreIdempotentAndOwnTheirBytes()
        {
            byte[] wire = StatusVfxWireCodec.Encode(Frame(1, State()));
            var chunks = StatusVfxChunkAssembler.Split(wire, 64); var assembler = new StatusVfxChunkAssembler(64);
            Assert.That(assembler.TryAdd(1, 0, chunks.Count, wire.Length, chunks[0], out _, out _), Is.False);
            Assert.That(assembler.TryAdd(1, 0, chunks.Count, wire.Length, chunks[0], out _, out var duplicate), Is.False);
            Assert.That(duplicate, Is.Null);
            chunks[0][0] ^= 1;
            Assert.That(assembler.TryAdd(1, 0, chunks.Count, wire.Length, chunks[0], out _, out var conflict), Is.False);
            Assert.That(conflict, Is.EqualTo("conflicting-duplicate"));
            byte[] complete = null;
            for (int i = 1; i < chunks.Count; i++) assembler.TryAdd(1, i, chunks.Count, wire.Length, chunks[i], out complete, out _);
            Assert.That(complete, Is.EqualTo(wire));
            Assert.That(assembler.TryAdd(1, 1, chunks.Count, wire.Length, chunks[1], out _, out _), Is.False);
        }

        [Test]
        public void MalformedPayloadAndChunkEnvelopeAreRejectedSafely()
        {
            Assert.That(StatusVfxWireCodec.TryDecode(new byte[] { 1, 2 }, out _, out _), Is.False);
            byte[] valid = StatusVfxWireCodec.Encode(Frame(1, State()));
            Assert.That(StatusVfxWireCodec.TryDecode(valid.Concat(new byte[] { 5 }).ToArray(), out _, out _), Is.False);
            var assembler = new StatusVfxChunkAssembler(64);
            Assert.That(assembler.TryAdd(1, int.MaxValue, int.MaxValue, 40, new byte[40], out _, out _), Is.False);
            Assert.That(assembler.TryAdd(1, 0, 2, 40, new byte[40], out _, out _), Is.False);
            Assert.That(StatusVfxWireCodec.TryDecode(StatusVfxWireCodec.Encode(Frame(1, State(end: float.NaN))), out _, out _), Is.False);
        }

        [Test]
        public void AtomicReceiverPreservesCurrentSnapshotUntilNewCompleteRevision()
        {
            var receiver = new StatusVfxWireReceiver();
            Assert.That(receiver.TryAccept(Frame(2, State()), Scope, out _), Is.True);
            Assert.That(receiver.TryAccept(Frame(1), Scope, out _), Is.False);
            Assert.That(receiver.Snapshot.Count, Is.EqualTo(1));
            Assert.That(receiver.TryAccept(Frame(3, State(), State()), Scope, out _), Is.False);
            Assert.That(receiver.Snapshot.Count, Is.EqualTo(1));
            Assert.That(receiver.TryAccept(Frame(4), Scope, out _), Is.True);
            Assert.That(receiver.Snapshot.Count, Is.Zero);
        }

        [Test]
        public void NewJoinStartsWithPersistentSnapshotAndNoHistoricalPulses()
        {
            var receiver = new StatusVfxWireReceiver();
            var pulse = new StatusVfxWireFrame(Scope, 1, 1, 11f, true, new[] { State(sequence: 8) });
            Assert.That(receiver.TryAccept(pulse, Scope, out var reason), Is.False);
            Assert.That(reason, Is.EqualTo("snapshot-required"));
            Assert.That(receiver.TryAccept(Frame(2, State()), Scope, out _), Is.True);
            Assert.That(receiver.Snapshot.Count, Is.EqualTo(1));
            Assert.That(receiver.TryAccept(pulse, Scope, out _), Is.False, "A pulse older than the join snapshot is historical");
            var current = new StatusVfxWireFrame(Scope, 1, 3, 11f, true, new[] { State(sequence: 9) });
            Assert.That(receiver.TryAccept(current, Scope, out _), Is.True);
            Assert.That(receiver.TryAccept(current, Scope, out _), Is.False);
        }

        [Test]
        public void ScopeAndGenerationRejectPreviousRoundTraffic()
        {
            var receiver = new StatusVfxWireReceiver();
            Assert.That(receiver.TryAccept(new StatusVfxWireFrame(Scope, 2, 1, 11f, false, new[] { State() }), Scope, out _), Is.True);
            Assert.That(receiver.TryAccept(Frame(100, State()), Scope, out _), Is.False);
            var otherScope = new StatusVfxScope(1, "match-a", 3, "scene", "epoch-a");
            Assert.That(receiver.TryAccept(Frame(100, State()), otherScope, out _), Is.False);
            Assert.That(receiver.Snapshot.Count, Is.EqualTo(1));
        }

        [Test]
        public void JoinedPeerRetainsUnexpiredPulseAcrossGenerationRecoverySnapshot()
        {
            var receiver = new StatusVfxWireReceiver();
            Assert.That(receiver.TryAccept(Frame(10, State()), Scope, out _), Is.True);
            var pending = new StatusVfxWireFrame(Scope, 2, 20, 11f, true, new[] { State(sequence: 20) });
            Assert.That(receiver.TryAccept(pending, Scope, out var reason), Is.False);
            Assert.That(reason, Is.EqualTo("snapshot-required"));
            var recovery = new StatusVfxWireFrame(Scope, 2, 21, 11f, false, new[] { State() });
            Assert.That(receiver.TryAccept(recovery, Scope, out _), Is.True);
            Assert.That(receiver.TryAccept(pending, Scope, out _), Is.True, "Recovery must not reclassify an already joined peer as a new join");
            Assert.That(receiver.TryAccept(pending, Scope, out _), Is.False);
            var newPeer = new StatusVfxWireReceiver();
            Assert.That(newPeer.TryAccept(recovery, Scope, out _), Is.True);
            Assert.That(newPeer.TryAccept(pending, Scope, out _), Is.False, "A genuinely new join still ignores history");
        }

        [Test]
        public void ReplayBookkeepingStaysConstantAfterTenThousandTransfers()
        {
            var assembler = new StatusVfxChunkAssembler(64);
            for (long id = 1; id <= 10000; id++)
                Assert.That(assembler.TryAdd(id, 0, 1, 8, new byte[8], out _, out _), Is.True);
            Assert.That(assembler.PendingTransferCount, Is.Zero);
            Assert.That(assembler.RetainedReplayWatermarkCount, Is.EqualTo(1));
            Assert.That(assembler.TryAdd(5, 0, 1, 8, new byte[8], out _, out _), Is.False);
        }

        [Test]
        public void PartialTimeoutFreesMemoryAndRequestsCompleteSnapshotRecovery()
        {
            double clock = 0;
            var assembler = new StatusVfxChunkAssembler(32, () => clock, 30);
            Assert.That(assembler.TryAdd(1, 0, 2, 64, new byte[32], out _, out _), Is.False);
            clock = 31;
            Assert.That(assembler.PruneExpired(), Is.True);
            Assert.That(assembler.PendingTransferCount, Is.Zero);
            Assert.That(assembler.ConsumeResyncRequest(), Is.True);
            Assert.That(assembler.TryAdd(1, 1, 2, 64, new byte[32], out _, out _), Is.False);
            Assert.That(assembler.TryAdd(2, 0, 1, 8, new byte[8], out _, out _), Is.True);
        }

        [Test]
        public void LargeAdvancingTransferHasNoTotalTimeOrEffectCountDeadline()
        {
            double clock = 0;
            var assembler = new StatusVfxChunkAssembler(32, () => clock, 30);
            for (int i = 0; i < 4; i++)
            {
                clock = i * 29;
                Assert.That(assembler.TryAdd(1, i, 4, 128, new byte[32], out _, out _), Is.EqualTo(i == 3));
            }
            Assert.That(assembler.ConsumeResyncRequest(), Is.False);
        }

        [Test]
        public void RebasePreservesElapsedAndRemainingIncludingInfinity()
        {
            var translated = StatusVfxWireCodec.Rebase(State(), 12f, 500f);
            Assert.That(translated.StartTime, Is.EqualTo(498f)); Assert.That(translated.EndTime, Is.EqualTo(503f));
            var late = StatusVfxWireCodec.Rebase(State(), 16f, 700f);
            Assert.That(late.IsActiveAt(700f), Is.False);
            Assert.That(StatusVfxWireCodec.Rebase(State(end: float.PositiveInfinity), 12f, 500f).EndTime, Is.EqualTo(float.PositiveInfinity));
        }
    }
}
