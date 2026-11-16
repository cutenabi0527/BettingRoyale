using BettingRoyale.Core;
using BettingRoyale.Data.Training;

namespace BettingRoyale.Data.Contracts
{
    public readonly struct TrainingEntryMenuRequested : IGameEvent { }
    public readonly struct TrainingRoomCreateRequested : IGameEvent { }
    public readonly struct TrainingRoomJoinRequested : IGameEvent
    {
        public readonly string Code;
        public TrainingRoomJoinRequested(string code) { Code = code ?? string.Empty; }
    }
    public readonly struct TrainingLeaveRequested : IGameEvent { }
    public readonly struct TrainingRoomPresentationChanged : IGameEvent
    {
        public readonly string RoomCode;
        public readonly int PlayerCount;
        public TrainingRoomPresentationChanged(string roomCode, int playerCount)
        { RoomCode = roomCode ?? string.Empty; PlayerCount = playerCount; }
    }
    public readonly struct TrainingEntryStatusChanged : IGameEvent
    {
        public readonly bool Busy;
        public readonly string Message;
        public TrainingEntryStatusChanged(bool busy, string message) { Busy = busy; Message = message ?? string.Empty; }
    }
    public readonly struct TrainingActionCommand : IGameEvent
    {
        public readonly TrainingActionKind Kind;
        public readonly int Value;
        public readonly string Selection;
        public TrainingActionCommand(TrainingActionKind kind, int value = 0, string selection = "")
        { Kind = kind; Value = value; Selection = selection ?? string.Empty; }
    }
    public readonly struct TrainingSessionStateChanged : IGameEvent
    {
        public readonly TrainingSessionView State;
        public TrainingSessionStateChanged(TrainingSessionView state) { State = state; }
    }
    public readonly struct TrainingActionRejected : IGameEvent
    {
        public readonly string Reason;
        public TrainingActionRejected(string reason) { Reason = reason ?? string.Empty; }
    }
}
