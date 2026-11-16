using System;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>효과 인스턴스의 신원. 같은 효과라도 출처가 다르면 독립 인스턴스다.</summary>
    public readonly struct BlessingEffectKey : IEquatable<BlessingEffectKey>
    {
        public BlessingEffectKey(int sourceId, string effectId)
        {
            if (string.IsNullOrWhiteSpace(effectId))
            {
                throw new ArgumentException("효과 id 가 비었습니다.", nameof(effectId));
            }

            SourceId = sourceId;
            EffectId = effectId.Trim();
        }

        public readonly int SourceId;
        public readonly string EffectId;

        public bool Equals(BlessingEffectKey other) =>
            SourceId == other.SourceId && string.Equals(EffectId, other.EffectId, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is BlessingEffectKey other && Equals(other);
        public override int GetHashCode() => SourceId * 397 ^ (EffectId == null ? 0 : EffectId.GetHashCode());
        public override string ToString() => $"{SourceId}:{EffectId}";
    }
}
