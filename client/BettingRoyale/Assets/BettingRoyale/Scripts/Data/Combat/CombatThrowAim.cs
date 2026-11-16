using UnityEngine;

namespace BettingRoyale.Data.Combat
{
    /// <summary>무기 방향과 독립된 투척 지면 목표. default는 발밑이 아니라 투척 불가다.</summary>
    public readonly struct CombatThrowAim
    {
        public readonly Vector3 Point;
        public readonly bool IsValid;

        public CombatThrowAim(Vector3 point)
        {
            Point = point;
            IsValid = float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z);
        }
    }
}
