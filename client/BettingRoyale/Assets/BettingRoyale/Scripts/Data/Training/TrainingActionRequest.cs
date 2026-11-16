namespace BettingRoyale.Data.Training
{
    /// <summary>PlayerId·피해량·골드 금액은 요청에 없다. 발신자는 전송 연결에서 결정한다.</summary>
    public readonly struct TrainingActionRequest
    {
        public string SessionId { get; }
        public int Sequence { get; }
        public TrainingActionKind Kind { get; }
        public int Value { get; }
        public string Selection { get; }

        public TrainingActionRequest(string sessionId, int sequence, TrainingActionKind kind,
            int value = 0, string selection = "")
        {
            SessionId = sessionId ?? string.Empty;
            Sequence = sequence;
            Kind = kind;
            Value = value;
            Selection = selection ?? string.Empty;
        }
    }
}
