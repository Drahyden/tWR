using UnityEngine;
using TWR.Player;

namespace TWR.Portals
{
    /// <summary>
    /// Sits on the Player and teleports it when it walks through a portal (proposed D-142; D-097, D-113).
    /// It watches the UN-LEANED head pivot (the Head transform, before LeanPivot), so leaning can never trigger a
    /// crossing. When the line from last frame's head position to this frame's crosses a portal's front face inside
    /// the opening, the player is moved in the SAME frame through PlayerController.Teleport (IPlayerMotor): feet
    /// position mapped through the pair, view turned by the pair's yaw (pitch kept), momentum turned with it.
    /// Runs in Update right after PlayerLocomotion moved, before interaction/shooting and before the cameras.
    /// </summary>
    [DefaultExecutionOrder(5)]
    public class PortalTraveller : MonoBehaviour
    {
        [SerializeField] PlayerController controller;
        [Tooltip("Head transform (pitch pivot, NOT the leaned LeanPivot or the camera).")]
        [SerializeField] Transform pivot;
        [Tooltip("Write a [TWR] line to the Console on every crossing (slice testing).")]
        [SerializeField] bool logCrossings = true;

        Vector3 lastPivot;
        bool hasLast;

        void Awake()
        {
            if (controller == null) controller = GetComponent<PlayerController>();
            if (pivot == null && controller != null && controller.Look != null) pivot = controller.Look.Head;
            if (pivot == null) pivot = transform.Find("Head");
            if (controller == null || pivot == null)
            {
                Debug.LogError("[PortalTraveller] Needs the PlayerController and the Head transform. Run TWR > Build Test Scene.", this);
                enabled = false;
            }
        }

        void OnEnable() => hasLast = false;

        void Update()
        {
            Vector3 now = pivot.position;
            if (hasLast)
            {
                var portals = Portal.Active;
                for (int i = 0; i < portals.Count; i++)
                {
                    Portal portal = portals[i];
                    if (!portal.CanPass || !portal.SegmentCrossesFront(lastPivot, now)) continue;
                    Travel(portal);
                    now = pivot.position; // start tracking from the exit side (no instant re-crossing)
                    break;
                }
            }
            lastPivot = now;
            hasLast = true;
        }

        void Travel(Portal portal)
        {
            Portal exit = portal.Destination;
            PlayerLook look = controller.Look;

            Vector3 feet = portal.ToDestinationPoint(controller.transform.position);
            // Build the new view from the stored yaw/pitch (not the camera), so recoil kick and lean roll don't stick.
            Quaternion view = Quaternion.Euler(look.Pitch, look.Yaw + portal.YawDelta, 0f);
            controller.Teleport(feet, view); // IPlayerMotor.Teleport + momentum turned by the yaw change (D-113)

            PortalEvents.RaiseCrossed(portal.PortalId, portal.transform, exit.transform);
            if (logCrossings)
                Debug.Log($"[TWR] Portal crossed: '{portal.PortalId}' -> '{exit.PortalId}' (view turned {portal.YawDelta:0} deg)", this);
        }
    }
}
