using UnityEngine;

namespace BettingRoyale.Data.Combat
{
    public readonly struct CombatInteractionCapsule
    {
        public CombatInteractionCapsule(Vector3 start, Vector3 end, float radius)
        {
            Start = start; End = end; Radius = Mathf.Max(0f, radius);
        }
        public Vector3 Start { get; }
        public Vector3 End { get; }
        public float Radius { get; }
        public static CombatInteractionCapsule AtPoint(Vector3 point) => new(point, point, 0f);
    }

    /// <summary>
    /// 상자 이동 차단물의 월드 형상을 상호작용 판정에 전달하는 값 객체다.
    /// 중심점까지의 거리로 상자 크기를 다시 추정하지 않고, 실제 BoxCollider의 중심·반축 길이·회전을 보존한다.
    /// </summary>
    public readonly struct CombatChestInteractionBounds
    {
        public CombatChestInteractionBounds(Vector3 center, Vector3 halfExtents, Quaternion rotation)
        {
            Center = center;
            HalfExtents = new Vector3(
                Mathf.Abs(halfExtents.x),
                Mathf.Abs(halfExtents.y),
                Mathf.Abs(halfExtents.z));
            float rotationMagnitudeSquared =
                rotation.x * rotation.x
                + rotation.y * rotation.y
                + rotation.z * rotation.z
                + rotation.w * rotation.w;
            Rotation = rotationMagnitudeSquared > 0.000001f
                ? rotation.normalized
                : Quaternion.identity;
        }

        public Vector3 Center { get; }
        public Vector3 HalfExtents { get; }
        public Quaternion Rotation { get; }

        public static CombatChestInteractionBounds AtPoint(Vector3 position) =>
            new CombatChestInteractionBounds(position, Vector3.zero, Quaternion.identity);
    }

    /// <summary>
    /// 월드 장비 획득과 상자 상호작용이 공유하는 근거리 판정 거리. UI 문구나 제품 밸런스 값이 아니라
    /// 기존 장비 pickup의 확정된 runtime contract를 한 곳으로 옮긴 것이다.
    /// 장비는 기존처럼 월드 점까지 1.5m를 비교하고, 상자는 전용 1.2m 임계값을 실제 Collider 표면까지의
    /// 3차원 거리(<see cref="DistanceToChest(Vector3, CombatChestInteractionBounds)"/>)에 적용한다.
    /// </summary>
    public static class CombatInteractionDistance
    {
        public const float NearbyWorldDistance = 1.5f;
        public const float ChestInteractionDistance = 1.2f;

        /// <summary>Exact segment-to-OBB distance, less the actor's capsule radius. No pivot approximation.</summary>
        public static float DistanceToChest(CombatInteractionCapsule actor, CombatChestInteractionBounds bounds)
        {
            Quaternion inverse = Quaternion.Inverse(bounds.Rotation);
            Vector3 start = inverse * (actor.Start - bounds.Center);
            Vector3 direction = inverse * (actor.End - actor.Start);
            // Slab crossings split squared distance into convex quadratic pieces.
            System.Span<float> cuts = stackalloc float[8];
            int count = 2;
            cuts[0] = 0f; cuts[1] = 1f;
            for (int axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(direction[axis]) < .000001f) continue;
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    float t = (sign * bounds.HalfExtents[axis] - start[axis]) / direction[axis];
                    if (t > 0f && t < 1f) cuts[count++] = t;
                }
            }
            for (int i = 1; i < count; i++)
            {
                float value = cuts[i]; int j = i - 1;
                while (j >= 0 && cuts[j] > value) { cuts[j + 1] = cuts[j]; j--; }
                cuts[j + 1] = value;
            }
            float minimum = float.PositiveInfinity;
            for (int i = 0; i < count - 1; i++)
            {
                float mid = (cuts[i] + cuts[i + 1]) * .5f;
                float a = 0f, b = 0f;
                for (int axis = 0; axis < 3; axis++)
                {
                    float sample = start[axis] + direction[axis] * mid;
                    if (Mathf.Abs(sample) <= bounds.HalfExtents[axis]) continue;
                    float offset = start[axis] - Mathf.Sign(sample) * bounds.HalfExtents[axis];
                    a += direction[axis] * direction[axis];
                    b += direction[axis] * offset;
                }
                float t = a > 0f ? Mathf.Clamp(-b / a, cuts[i], cuts[i + 1]) : mid;
                Vector3 p = start + direction * t;
                Vector3 delta = new(Mathf.Max(Mathf.Abs(p.x) - bounds.HalfExtents.x, 0),
                    Mathf.Max(Mathf.Abs(p.y) - bounds.HalfExtents.y, 0),
                    Mathf.Max(Mathf.Abs(p.z) - bounds.HalfExtents.z, 0));
                minimum = Mathf.Min(minimum, delta.sqrMagnitude);
            }
            return Mathf.Max(0f, Mathf.Sqrt(minimum) - actor.Radius);
        }

        public static float DistanceToChestSquared(CombatInteractionCapsule actor, CombatChestInteractionBounds bounds)
        {
            float distance = DistanceToChest(actor, bounds);
            return distance * distance;
        }

        /// <summary>
        /// 상자 OBB와 상호작용 원점 사이의 최소 3차원 유클리드 거리 제곱을 계산한다.
        /// 원점이 OBB 안에 있으면 거리는 0이다.
        /// </summary>
        public static float DistanceToChestSquared(
            Vector3 interactionOrigin,
            CombatChestInteractionBounds bounds)
        {
            Vector3 local = Quaternion.Inverse(bounds.Rotation) * (interactionOrigin - bounds.Center);
            Vector3 delta = new Vector3(
                Mathf.Max(Mathf.Abs(local.x) - bounds.HalfExtents.x, 0f),
                Mathf.Max(Mathf.Abs(local.y) - bounds.HalfExtents.y, 0f),
                Mathf.Max(Mathf.Abs(local.z) - bounds.HalfExtents.z, 0f));
            return delta.sqrMagnitude;
        }

        /// <summary>상자 OBB 표면까지의 최소 3차원 유클리드 거리를 반환한다.</summary>
        public static float DistanceToChest(
            Vector3 interactionOrigin,
            CombatChestInteractionBounds bounds) =>
            Mathf.Sqrt(DistanceToChestSquared(interactionOrigin, bounds));

        /// <summary>표면 거리와 상자 전용 1.2m 임계값을 함께 적용하고 측정값을 반환한다.</summary>
        public static bool IsWithinChestRange(
            Vector3 interactionOrigin,
            CombatChestInteractionBounds bounds,
            out float measuredDistance)
        {
            measuredDistance = DistanceToChest(interactionOrigin, bounds);
            return measuredDistance <= ChestInteractionDistance;
        }
    }
}
