using System.Globalization;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>게임플레이 효과 하나의 읽기 전용 표시 모델.</summary>
    public readonly struct BlessingEffectInstance
    {
        public BlessingEffectInstance(int sourceId, string effectId, string iconId, BlessingEffectKind kind,
            float startTime, float totalSeconds, float remainingSeconds, bool isUnlimited, int stacks)
        {
            SourceId = sourceId;
            EffectId = effectId;
            IconId = string.IsNullOrEmpty(iconId) ? effectId : iconId;
            Kind = kind;
            StartTime = startTime;
            IsUnlimited = isUnlimited;
            TotalSeconds = isUnlimited || totalSeconds <= 0f || float.IsNaN(totalSeconds) ? 0f : totalSeconds;
            RemainingSeconds = isUnlimited || remainingSeconds <= 0f || float.IsNaN(remainingSeconds)
                ? 0f
                : remainingSeconds;
            Stacks = stacks < 1 ? 1 : stacks;
        }

        public readonly int SourceId;
        public readonly string EffectId;
        public readonly string IconId;
        public readonly BlessingEffectKind Kind;
        public readonly float StartTime;
        public readonly float TotalSeconds;
        public readonly float RemainingSeconds;
        public readonly bool IsUnlimited;
        public readonly int Stacks;

        public BlessingEffectKey Key => new BlessingEffectKey(SourceId, EffectId);

        /// <summary>HUD 페이드 등 표시 전용으로만 사용한다.</summary>
        public float NormalizedRemaining
        {
            get
            {
                if (IsUnlimited) return 1f;
                if (TotalSeconds <= 0f) return 0f;
                float ratio = RemainingSeconds / TotalSeconds;
                return ratio < 0f ? 0f : ratio > 1f ? 1f : ratio;
            }
        }

        public override string ToString()
        {
            string time = IsUnlimited
                ? "무제한"
                : RemainingSeconds.ToString("0.###", CultureInfo.InvariantCulture) + "초";
            return $"{SourceId}:{EffectId} · {time}" + (Stacks > 1 ? $" · {Stacks}중첩" : string.Empty);
        }
    }
}
