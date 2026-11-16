using System;
using System.Globalization;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>유한 시간과 무제한을 별도 상태로 표현하는 효과 지속 시간.</summary>
    public readonly struct BlessingEffectDuration : IEquatable<BlessingEffectDuration>
    {
        public static readonly BlessingEffectDuration Unlimited = new BlessingEffectDuration(true, 0f);
        public static readonly BlessingEffectDuration Expired = new BlessingEffectDuration(false, 0f);

        private BlessingEffectDuration(bool isUnlimited, float seconds)
        {
            IsUnlimited = isUnlimited;
            Seconds = seconds;
        }

        public readonly bool IsUnlimited;
        public readonly float Seconds;
        public bool IsExpired => !IsUnlimited && Seconds <= 0f;

        public static BlessingEffectDuration Of(float seconds)
        {
            if (float.IsNaN(seconds) || seconds <= 0f)
            {
                return Expired;
            }

            return float.IsPositiveInfinity(seconds)
                ? Unlimited
                : new BlessingEffectDuration(false, seconds);
        }

        public bool Equals(BlessingEffectDuration other) =>
            IsUnlimited == other.IsUnlimited && Seconds.Equals(other.Seconds);

        public override bool Equals(object obj) => obj is BlessingEffectDuration other && Equals(other);
        public override int GetHashCode() => (IsUnlimited ? 1 : 0) * 397 ^ Seconds.GetHashCode();
        public override string ToString() => IsUnlimited
            ? "무제한"
            : Seconds.ToString("0.###", CultureInfo.InvariantCulture) + "초";
    }
}
