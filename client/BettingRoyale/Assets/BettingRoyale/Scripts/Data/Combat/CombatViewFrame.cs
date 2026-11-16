using UnityEngine;

namespace BettingRoyale.Data.Combat
{
    /// <summary>Owner camera footprint, relative to the actor. Presentation only, never combat authority.</summary>
    public readonly struct CombatViewFrame
    {
        public readonly Vector3 Offset;
        public readonly Quaternion Rotation;
        public readonly float VerticalExtent;
        public readonly float Aspect;
        public readonly bool Orthographic;
        public bool IsValid => Finite(Offset.x) && Finite(Offset.y) && Finite(Offset.z)
            && Offset.sqrMagnitude < 100000000f && Finite(Rotation.x) && Finite(Rotation.y)
            && Finite(Rotation.z) && Finite(Rotation.w)
            && Mathf.Abs(Quaternion.Dot(Rotation, Rotation) - 1f) < .01f
            && Finite(VerticalExtent) && VerticalExtent > 0 && VerticalExtent < 10000
            && Finite(Aspect) && Aspect > 0 && Aspect < 100;

        public CombatViewFrame(Vector3 offset, Quaternion rotation, float verticalExtent, float aspect, bool orthographic)
        { Offset = offset; Rotation = rotation; VerticalExtent = verticalExtent; Aspect = aspect; Orthographic = orthographic; }

        public bool Contains(Vector3 actorPosition, Vector3 point)
        {
            if (!IsValid) return false;
            Vector3 local = Quaternion.Inverse(Rotation) * (point - actorPosition - Offset);
            if (local.z <= 0) return false;
            float height = Orthographic ? VerticalExtent : local.z * VerticalExtent;
            return Mathf.Abs(local.x) <= height * Aspect && Mathf.Abs(local.y) <= height;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public interface ICombatViewSource { CombatViewFrame ViewFrame { get; } }
    public interface ICombatViewSink { void SubmitView(in CombatViewFrame frame); }
}
