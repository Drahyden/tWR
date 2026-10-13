using UnityEngine;

namespace TWR.Player
{
    /// <summary>
    /// Front door of the player: one place other systems talk to (portals, rooms, enemies).
    /// Exposes PlayerCamera (future observer for the "is observed" check, D-045 / D-053),
    /// the input reader (ControlsInverted, D-053d) and a motor-safe Teleport (D-097).
    /// The real work is done by PlayerLocomotion (+ its IPlayerMotor), PlayerLook, PlayerLean, PlayerInteractor.
    /// </summary>
    [RequireComponent(typeof(PlayerLocomotion), typeof(PlayerLook), typeof(PlayerInputReader))]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] Camera playerCamera;

        PlayerLocomotion locomotion;
        PlayerLook look;
        PlayerInputReader input;

        /// <summary>The first-person camera. Will be the observer for the view-cone check (D-045, D-053).</summary>
        public Camera PlayerCamera => playerCamera;
        public PlayerLocomotion Locomotion => locomotion;
        public PlayerLook Look => look;
        public PlayerInputReader InputReader => input;

        void Awake()
        {
            locomotion = GetComponent<PlayerLocomotion>();
            look = GetComponent<PlayerLook>();
            input = GetComponent<PlayerInputReader>();
            if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
        }

        /// <summary>
        /// Instantly move the player (feet) to position and face rotation (yaw + pitch; roll ignored).
        /// Goes through the IPlayerMotor so it is safe with the CharacterController, and keeps
        /// PlayerLook's stored yaw/pitch in sync (D-097). Momentum turns with the view.
        /// </summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            float oldYaw = look.Yaw;
            look.SetLookRotation(rotation);
            float yawDelta = Mathf.DeltaAngle(oldYaw, look.Yaw);
            locomotion.TeleportTo(position, yawDelta);
        }
    }
}
