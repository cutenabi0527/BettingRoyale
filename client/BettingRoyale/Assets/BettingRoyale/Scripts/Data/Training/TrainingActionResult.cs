namespace BettingRoyale.Data.Training
{
    public readonly struct TrainingActionResult
    {
        public bool Accepted { get; }
        public bool Duplicate { get; }
        public string Message { get; }

        public TrainingActionResult(bool accepted, string message, bool duplicate = false)
        {
            Accepted = accepted;
            Message = message ?? string.Empty;
            Duplicate = duplicate;
        }
    }
}
