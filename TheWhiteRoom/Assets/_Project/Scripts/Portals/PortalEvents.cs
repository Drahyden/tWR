using System;
using UnityEngine;

namespace TWR.Portals
{
    /// <summary>
    /// Shared static portal events (D-142), like PuzzleEvents / InteractionEvents. Inner-voice triggers
    /// (D-080), music (D-075) and later run logic (D-058) can listen without scene references.
    /// </summary>
    public static class PortalEvents
    {
        /// <summary>The player went through a portal: (portalId, from = entry portal, to = exit portal).</summary>
        public static event Action<string, Transform, Transform> Crossed;

        public static void RaiseCrossed(string portalId, Transform fromAnchor, Transform toAnchor)
        {
            Crossed?.Invoke(portalId, fromAnchor, toAnchor);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Crossed = null;
    }
}
