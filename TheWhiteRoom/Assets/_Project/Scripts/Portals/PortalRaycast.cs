using System.Collections.Generic;
using UnityEngine;

namespace TWR.Portals
{
    /// <summary>Result of a PortalRaycast: the real hit, the ray direction at the hit, and the portal it went through.</summary>
    public struct PortalRayHit
    {
        /// <summary>The physics hit (point and normal are where the object REALLY is, e.g. in the hall).</summary>
        public RaycastHit hit;
        /// <summary>Ray direction at the hit (turned by the portal if it went through one).</summary>
        public Vector3 direction;
        /// <summary>Distance travelled in total, both legs.</summary>
        public float totalDistance;
        /// <summary>The portal the ray went through, or null.</summary>
        public Portal throughPortal;

        public bool ViaPortal => throughPortal != null;
    }

    /// <summary>
    /// Shared "ray that can go through ONE portal" (D-143), used by Gun hitscan (D-118) and
    /// PlayerInteractor (D-112). It casts normally up to the first open portal it enters from the front; if nothing
    /// is hit before it, the ray continues from the matching spot on the destination portal, turned like the
    /// view, with the distance that is left (so interaction stays 2.5 m in total). Never more than one hop (D-140).
    /// Also gives PlayerLean the distance to the nearest portal plane, so leaning can't push the camera through
    /// (D-142). Allocation-free: one shared hit buffer.
    /// </summary>
    public static class PortalRaycast
    {
        const int MaxHits = 16;
        const float ExitNudge = 0.001f; // start the second leg just past the destination plane

        static readonly RaycastHit[] hits = new RaycastHit[MaxHits];

        /// <summary>
        /// Raycast that may pass through one open portal. ignoreRoot's own colliders (the shooter) are skipped.
        /// direction must be normalised. Returns false if nothing was hit within maxDistance.
        /// </summary>
        public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, int layerMask,
                                   QueryTriggerInteraction triggers, Transform ignoreRoot, out PortalRayHit result)
        {
            result = default;
            Portal portal = FindEnteredPortal(origin, direction, maxDistance, out float portalDistance);
            float firstLeg = portal != null ? portalDistance : maxDistance;

            if (Nearest(origin, direction, firstLeg, layerMask, triggers, ignoreRoot, out RaycastHit hit))
            {
                result.hit = hit;
                result.direction = direction;
                result.totalDistance = hit.distance;
                return true;
            }
            if (portal == null) return false;

            float remaining = maxDistance - portalDistance - ExitNudge;
            if (remaining <= 0f) return false;

            Vector3 exitDirection = portal.ToDestinationDirection(direction).normalized;
            Vector3 exitPoint = portal.ToDestinationPoint(origin + direction * portalDistance) + exitDirection * ExitNudge;
            if (!Nearest(exitPoint, exitDirection, remaining, layerMask, triggers, ignoreRoot, out hit)) return false;

            result.hit = hit;
            result.direction = exitDirection;
            result.totalDistance = portalDistance + ExitNudge + hit.distance;
            result.throughPortal = portal;
            return true;
        }

        /// <summary>The nearest open portal whose FRONT face this ray enters within maxDistance, or null.</summary>
        public static Portal FindEnteredPortal(Vector3 origin, Vector3 direction, float maxDistance, out float distance)
        {
            distance = maxDistance;
            Portal best = null;
            IReadOnlyList<Portal> portals = Portal.Active;
            for (int i = 0; i < portals.Count; i++)
            {
                Portal portal = portals[i];
                if (!portal.CanPass) continue;
                if (portal.RayEntersFront(origin, direction, distance, out float d) && d <= distance)
                {
                    distance = d;
                    best = portal;
                }
            }
            return best;
        }

        /// <summary>
        /// How far you can move from origin along direction (up to maxDistance) before coming within "margin" of
        /// any portal plane inside its opening (either side). Used by PlayerLean so the camera never crosses.
        /// </summary>
        public static float ClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float margin)
        {
            float clear = maxDistance;
            IReadOnlyList<Portal> portals = Portal.Active;
            for (int i = 0; i < portals.Count; i++)
            {
                Portal portal = portals[i];
                float side = portal.SignedDistance(origin);
                float rate = Vector3.Dot(direction, portal.Normal);
                if (Mathf.Abs(rate) < 1e-4f || side * rate >= 0f) continue; // parallel, or moving away from the plane
                float toPlane = -side / rate;
                if (toPlane > maxDistance + margin) continue;
                if (!portal.IsInsideOpening(origin + direction * toPlane, margin)) continue;
                clear = Mathf.Min(clear, Mathf.Max(0f, toPlane - margin));
            }
            return clear;
        }

        /// Nearest non-trigger hit along the ray, skipping ignoreRoot's own colliders.
        static bool Nearest(Vector3 origin, Vector3 direction, float distance, int layerMask,
                            QueryTriggerInteraction triggers, Transform ignoreRoot, out RaycastHit nearest)
        {
            nearest = default;
            if (distance <= 0f) return false;
            int count = Physics.RaycastNonAlloc(origin, direction, hits, distance, layerMask, triggers);
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].distance >= bestDistance) continue;
                if (ignoreRoot != null && hits[i].collider.transform.IsChildOf(ignoreRoot)) continue;
                best = i;
                bestDistance = hits[i].distance;
            }
            if (best < 0) return false;
            nearest = hits[best];
            return true;
        }
    }
}
