using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BettingRoyale.Data.Common;

namespace BettingRoyale.Data.Combat
{
    /// <summary>Length-delimited wire data. Packet fragmentation never imposes a visible-effect count limit.</summary>
    public static class StatusVfxWireCodec
    {
        private const int Version = 1;
        private const int MinimumStateBytes = 64;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static byte[] Encode(StatusVfxWireFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write(Version); writer.Write(frame.Scope.Kind); Text(writer, frame.Scope.Id);
                writer.Write(frame.Scope.Round); Text(writer, frame.Scope.Scene); Text(writer, frame.Scope.Epoch);
                writer.Write(frame.Generation); writer.Write(frame.Revision); writer.Write(frame.AuthorityTime);
                writer.Write(frame.IsEvent); writer.Write(frame.States.Count);
                foreach (var state in frame.States)
                {
                    writer.Write((int)state.Key.Kind); Text(writer, state.Key.SourceId);
                    writer.Write(state.Key.SourceCombatantId); writer.Write(state.Key.TargetCombatantId); writer.Write(state.Key.InstanceId);
                    writer.Write(state.StartTime); writer.Write(state.EndTime); writer.Write(state.Magnitude);
                    writer.Write(state.Phase); writer.Write(state.EventSequence);
                    Position(writer, state.Position); Position(writer, state.Direction); writer.Write(state.Radius);
                }
                writer.Flush(); return stream.ToArray();
            }
        }

        public static bool TryDecode(byte[] payload, out StatusVfxWireFrame frame, out string error)
        {
            frame = null; error = null;
            if (payload == null) { error = "missing-payload"; return false; }
            try
            {
                using (var stream = new MemoryStream(payload, false))
                using (var reader = new BinaryReader(stream, Utf8, true))
                {
                    if (reader.ReadInt32() != Version) { error = "unsupported-version"; return false; }
                    var scope = new StatusVfxScope(reader.ReadInt32(), Text(reader), reader.ReadInt32(), Text(reader), Text(reader));
                    int generation = reader.ReadInt32(); long revision = reader.ReadInt64(); float clock = reader.ReadSingle();
                    bool isEvent = reader.ReadBoolean(); int count = reader.ReadInt32();
                    if (generation < 0 || revision <= 0 || !Finite(clock) || count < 0 ||
                        count > (stream.Length - stream.Position) / MinimumStateBytes || (isEvent && count != 1))
                    { error = "invalid-envelope"; return false; }
                    var states = new List<StatusVfxState>(count);
                    for (int i = 0; i < count; i++)
                    {
                        int kind = reader.ReadInt32(); string source = Text(reader);
                        int actor = reader.ReadInt32(), target = reader.ReadInt32(), instance = reader.ReadInt32();
                        float start = reader.ReadSingle(), end = reader.ReadSingle(), magnitude = reader.ReadSingle();
                        int phase = reader.ReadInt32(), sequence = reader.ReadInt32();
                        var position = Position(reader); var direction = Position(reader); float radius = reader.ReadSingle();
                        if (kind < 0 || kind > (int)StatusVfxKind.LastFive || string.IsNullOrEmpty(source) || actor <= 0 || target <= 0 || instance <= 0 ||
                            !Finite(start) || float.IsNaN(end) || end <= start || !Finite(magnitude) || phase < 0 || sequence < 0 ||
                            !Finite(position.X) || !Finite(position.Y) || !Finite(position.Z) ||
                            !Finite(direction.X) || !Finite(direction.Y) || !Finite(direction.Z) || !Finite(radius) || radius < 0f || (isEvent && sequence <= 0))
                        { error = "invalid-effect"; return false; }
                        states.Add(new StatusVfxState(new StatusVfxKey((StatusVfxKind)kind, source, actor, target, instance), start, end,
                            magnitude, phase, sequence, position, direction, radius));
                    }
                    if (stream.Position != stream.Length) { error = "trailing-data"; return false; }
                    frame = new StatusVfxWireFrame(scope, generation, revision, clock, isEvent, states); return true;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is OverflowException)
            { error = "malformed-payload"; return false; }
        }

        public static StatusVfxState Rebase(in StatusVfxState state, float authorityNow, float localNow) =>
            new StatusVfxState(state.Key, localNow + (state.StartTime - authorityNow),
                float.IsPositiveInfinity(state.EndTime) ? float.PositiveInfinity : localNow + (state.EndTime - authorityNow),
                state.Magnitude, state.Phase, state.EventSequence, state.Position, state.Direction, state.Radius);

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static void Text(BinaryWriter writer, string value)
        { byte[] bytes = Utf8.GetBytes(value ?? string.Empty); writer.Write(bytes.Length); writer.Write(bytes); }
        private static string Text(BinaryReader reader)
        {
            int size = reader.ReadInt32();
            if (size < 0 || size > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException();
            return Utf8.GetString(reader.ReadBytes(size));
        }
        private static void Position(BinaryWriter writer, WorldPosition value)
        { writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z); }
        private static WorldPosition Position(BinaryReader reader) => new WorldPosition(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }
}
