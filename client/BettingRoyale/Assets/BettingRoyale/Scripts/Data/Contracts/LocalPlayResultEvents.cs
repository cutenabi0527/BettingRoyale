using System;
using BettingRoyale.Core;
using BettingRoyale.Data.Economy;

namespace BettingRoyale.Data.Contracts
{
    /// <summary>Local simulation standings; never a server receipt or an account medal grant.</summary>
    public readonly struct LocalPlayStandingsChanged : IGameEvent
    {
        private readonly FinalStanding[] standings;
        public int Round { get; }
        public int TotalRounds { get; }
        public bool IsFinal { get; }
        public LocalPlayStandingsChanged(int round, int totalRounds, FinalStanding[] standings, bool isFinal)
        {
            Round = round;
            TotalRounds = totalRounds;
            this.standings = standings == null ? Array.Empty<FinalStanding>() : (FinalStanding[])standings.Clone();
            IsFinal = isFinal;
        }
        public FinalStanding[] CopyStandings() => standings == null ? Array.Empty<FinalStanding>() : (FinalStanding[])standings.Clone();
    }
}
