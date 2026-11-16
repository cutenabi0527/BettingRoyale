using BettingRoyale.Core;
using BettingRoyale.Data.Scenes;

namespace BettingRoyale.Data.Contracts
{
    /// <summary>Own actor only; authenticated transport supplies identity outside this payload.</summary>
    public readonly struct TrainingTravelCommand : IGameEvent
    {
        public TrainingZoneId ZoneId { get; }
        public TrainingTravelCommand(TrainingZoneId zoneId) => ZoneId = zoneId;
    }
    public readonly struct TrainingTravelResolved : IGameEvent
    {
        public bool Accepted { get; }
        public string Reason { get; }
        public TrainingTravelResolved(bool accepted, string reason) { Accepted = accepted; Reason = reason; }
    }
    public readonly struct TrainingMenuVisibilityChanged : IGameEvent
    {
        public bool IsOpen { get; }
        public TrainingMenuVisibilityChanged(bool isOpen) => IsOpen = isOpen;
    }
    public readonly struct TrainingTravelRejected : IGameEvent
    {
        public TrainingZoneId ZoneId { get; }
        public string Reason { get; }
        public TrainingTravelRejected(TrainingZoneId zoneId, string reason) { ZoneId = zoneId; Reason = reason; }
    }
}
