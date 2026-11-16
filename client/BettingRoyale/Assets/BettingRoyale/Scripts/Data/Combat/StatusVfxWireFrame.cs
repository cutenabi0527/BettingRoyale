using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Combat
{
    public readonly struct StatusVfxScope : IEquatable<StatusVfxScope>
    {
        public readonly int Kind;
        public readonly string Id;
        public readonly int Round;
        public readonly string Scene;
        public readonly string Epoch;
        public StatusVfxScope(int kind, string id, int round, string scene, string epoch)
        { Kind = kind; Id = id ?? string.Empty; Round = round; Scene = scene ?? string.Empty; Epoch = epoch ?? string.Empty; }
        public bool Equals(StatusVfxScope other) => Kind == other.Kind && Round == other.Round &&
            string.Equals(Id, other.Id, StringComparison.Ordinal) && string.Equals(Scene, other.Scene, StringComparison.Ordinal) && string.Equals(Epoch, other.Epoch, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is StatusVfxScope other && Equals(other);
        public override int GetHashCode() => unchecked(Kind * 397 ^ Round ^ (Id ?? string.Empty).GetHashCode() ^ (Scene ?? string.Empty).GetHashCode() ^ (Epoch ?? string.Empty).GetHashCode());
    }

    public sealed class StatusVfxWireFrame
    {
        public readonly StatusVfxScope Scope;
        public readonly int Generation;
        public readonly long Revision;
        public readonly float AuthorityTime;
        public readonly bool IsEvent;
        public readonly IReadOnlyList<StatusVfxState> States;
        public StatusVfxWireFrame(StatusVfxScope scope, int generation, long revision, float authorityTime,
            bool isEvent, IReadOnlyList<StatusVfxState> states)
        {
            Scope = scope; Generation = generation; Revision = revision; AuthorityTime = authorityTime; IsEvent = isEvent;
            var copy = new StatusVfxState[states?.Count ?? 0];
            for (int i = 0; i < copy.Length; i++) copy[i] = states[i];
            States = Array.AsReadOnly(copy);
        }
    }
}
