using System;

namespace BettingRoyale.Data.Combat
{
    /// <summary>
    /// 전투 참여자 식별자. 추후 Fusion 어댑터가 경계에서 Fusion 쪽 플레이어 id 와 상호 변환한다 —
    /// Gameplay/Data 는 Fusion 을 모르는 채 이 값만 쓴다 (D-009, D-044).
    /// </summary>
    public readonly struct CombatantId : IEquatable<CombatantId>
    {
        public readonly int Value;

        public CombatantId(int value) => Value = value;

        public bool Equals(CombatantId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CombatantId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => $"Combatant({Value})";

        public static bool operator ==(CombatantId left, CombatantId right) => left.Equals(right);
        public static bool operator !=(CombatantId left, CombatantId right) => !left.Equals(right);
    }
}
