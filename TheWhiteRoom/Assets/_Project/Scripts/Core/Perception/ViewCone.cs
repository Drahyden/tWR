using UnityEngine;

namespace TWR.Core.Perception
{
    /// <summary>
    /// Shared "can this eye see that point" geometry: a view cone (angle + distance) and a line-of-sight ray.
    /// Used by the Seeker's sight (D-132 confirmed, D-043) and meant to be reused by the future "is observed"
    /// system (D-045 Lurker, D-053a rooms that change when not watched), so every system agrees on what
    /// "in view" means. Pure static maths + one Physics.Linecast: no allocations (D-100).
    /// The caller chooses the blocker mask; leave Player/Enemy out (see GameLayers.WithoutActors) so bodies
    /// never block their own view.
    /// </summary>
    public static class ViewCone
    {
        /// <summary>
        /// True if point is within maxDistance of eye and inside a cone of coneAngleDegrees (FULL angle,
        /// e.g. 110) around forward. Does not check walls.
        /// </summary>
        public static bool IsInCone(Vector3 eye, Vector3 forward, Vector3 point, float coneAngleDegrees, float maxDistance)
        {
            Vector3 toPoint = point - eye;
            float sqrDistance = toPoint.sqrMagnitude;
            if (sqrDistance > maxDistance * maxDistance) return false;
            if (sqrDistance < 0.0001f) return true;

            float cosHalfAngle = Mathf.Cos(coneAngleDegrees * 0.5f * Mathf.Deg2Rad);
            float cos = Vector3.Dot(forward.normalized, toPoint / Mathf.Sqrt(sqrDistance));
            return cos >= cosHalfAngle;
        }

        /// <summary>True if nothing on the blockers mask is between eye and point (trigger colliders ignored).</summary>
        public static bool HasLineOfSight(Vector3 eye, Vector3 point, int blockers)
        {
            return !Physics.Linecast(eye, point, blockers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>IsInCone and HasLineOfSight together (cheap test first).</summary>
        public static bool CanSee(Vector3 eye, Vector3 forward, Vector3 point, float coneAngleDegrees, float maxDistance, int blockers)
        {
            return IsInCone(eye, forward, point, coneAngleDegrees, maxDistance) && HasLineOfSight(eye, point, blockers);
        }
    }
}
