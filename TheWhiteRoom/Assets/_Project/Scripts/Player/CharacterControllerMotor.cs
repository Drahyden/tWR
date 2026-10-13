using UnityEngine;

namespace TWR.Player
{
    /// <summary>
    /// IPlayerMotor built on Unity's CharacterController. This is the ONLY script that touches the
    /// CharacterController (Move, isGrounded, height/center, enable/disable for teleport, excludeLayers).
    /// D-053c: when gravity-shift rooms arrive, write e.g. "KinematicCapsuleMotor : IPlayerMotor" and swap
    /// it in here; nothing else on the player needs to change.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class CharacterControllerMotor : MonoBehaviour, IPlayerMotor
    {
        CharacterController controller;

        // Lazy so other scripts may call us from their Awake before ours has run.
        CharacterController Controller => controller != null ? controller : (controller = GetComponent<CharacterController>());

        public Vector3 Up => Vector3.up; // CharacterController can only be upright.
        public bool IsGrounded => Controller.isGrounded;
        public bool HitCeiling { get; private set; }
        public Vector3 Velocity { get; private set; }
        public float Height => Controller.height;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        public void Move(Vector3 displacement)
        {
            Vector3 before = transform.position;
            CollisionFlags flags = Controller.Move(displacement);
            HitCeiling = (flags & CollisionFlags.Above) != 0;

            float dt = Time.deltaTime;
            Velocity = dt > 0f ? (transform.position - before) / dt : Vector3.zero;
        }

        public void SetCapsuleHeight(float height)
        {
            Controller.height = height;
            Controller.center = new Vector3(0f, height * 0.5f, 0f);
        }

        public bool HasHeadroom(float targetHeight, LayerMask obstacles)
        {
            float extra = targetHeight - Controller.height;
            if (extra <= 0f) return true;

            float radius = Controller.radius * 0.95f;
            // Cast up from the top sphere of our own capsule. Casts ignore colliders they start inside,
            // so our own CharacterController is never "hit".
            Vector3 origin = transform.position + Up * (Controller.height - Controller.radius);
            return !Physics.SphereCast(origin, radius, Up, out _, extra + 0.05f, obstacles, QueryTriggerInteraction.Ignore);
        }

        public void Teleport(Vector3 position)
        {
            // A CharacterController overwrites transform changes unless it is disabled while we move it.
            Controller.enabled = false;
            transform.position = position;
            Controller.enabled = true;
        }

        public void SetIgnoredLayers(LayerMask layers)
        {
            Controller.excludeLayers = layers;
        }
    }
}
