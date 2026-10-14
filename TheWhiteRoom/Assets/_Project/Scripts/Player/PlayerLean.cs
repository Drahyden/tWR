using UnityEngine;
using TWR.Portals;

namespace TWR.Player
{
    /// <summary>
    /// Lean left/right with Q/E (D-032). Moves ONLY the camera (no body, no arms, D-070):
    /// a sideways offset plus a roll on the LeanPivot. A spherecast stops the camera entering walls,
    /// and portal planes count as walls too (proposed D-142).
    /// Sprinting cancels lean; leaning while crouched is allowed.
    /// </summary>
    [DefaultExecutionOrder(10)]
    public class PlayerLean : MonoBehaviour
    {
        [SerializeField] PlayerTuning tuning;
        [SerializeField] PlayerInputReader input;
        [SerializeField] PlayerLocomotion locomotion;
        [Tooltip("Head (pitch) transform; the cast starts here.")]
        [SerializeField] Transform head;
        [Tooltip("Child of Head; lean offset and roll are applied here. Camera is its child.")]
        [SerializeField] Transform leanPivot;

        float currentLean; // -1 (full left) .. +1 (full right)

        /// <summary>Current lean amount, -1 left to +1 right.</summary>
        public float CurrentLean => currentLean;

        void Awake()
        {
            if (input == null) input = GetComponent<PlayerInputReader>();
            if (locomotion == null) locomotion = GetComponent<PlayerLocomotion>();
            if (head == null) head = transform.Find("Head");
            if (leanPivot == null && head != null) leanPivot = head.Find("LeanPivot");
        }

        void LateUpdate()
        {
            if (tuning == null || input == null || head == null || leanPivot == null) return;

            float wanted = input.LeanAxis;
            if (locomotion != null && locomotion.IsSprinting) wanted = 0f;

            currentLean = Mathf.MoveTowards(currentLean, wanted, tuning.leanSpeed * Time.deltaTime);

            // Never lean further than the free space on that side allows.
            float side = currentLean != 0f ? Mathf.Sign(currentLean) : 0f;
            if (side != 0f)
            {
                float allowed = FreeLeanFraction(side);
                if (Mathf.Abs(currentLean) > allowed) currentLean = side * allowed;
            }

            leanPivot.localPosition = new Vector3(currentLean * tuning.leanDistance, 0f, 0f);
            leanPivot.localRotation = Quaternion.Euler(0f, 0f, -currentLean * tuning.leanAngle);
        }

        /// <summary>0..1: how much of the full lean distance is free of walls on this side.</summary>
        float FreeLeanFraction(float side)
        {
            Vector3 direction = head.right * side;
            float distance = tuning.leanDistance;
            float free = distance;
            if (Physics.SphereCast(head.position, tuning.leanCollisionRadius, direction, out RaycastHit hit,
                                   distance, tuning.obstacleMask, QueryTriggerInteraction.Ignore))
            {
                free = hit.distance;
            }
            // A portal opening has no collider, but its plane counts as a wall here, so leaning can never
            // push the camera through a portal (proposed D-142).
            free = Mathf.Min(free, PortalRaycast.ClearDistance(head.position, direction, distance, tuning.leanCollisionRadius));
            return Mathf.Clamp01(free / distance);
        }
    }
}
