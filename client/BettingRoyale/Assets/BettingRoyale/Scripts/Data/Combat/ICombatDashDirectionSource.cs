using UnityEngine;

namespace BettingRoyale.Data.Combat
{
    /// <summary>입력/조준과 별개로 모터가 확정한 대시 방향을 표현·복제에 제공한다.</summary>
    public interface ICombatDashDirectionSource
    {
        Vector2 DashDirection { get; }
    }
}
