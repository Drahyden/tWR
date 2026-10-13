using System;
using UnityEngine;

namespace TWR.Player
{
    /// <summary>
    /// The movement RULES of the player (D-032): walk, sprint, crouch, jump, gravity. It decides the velocity
    /// and hands it to the IPlayerMotor, which does the actual collision-aware moving (swappable, D-053c).
    /// Built to grow into D-033 abilities: jump COUNT (double jump), AddImpulse (dash/knockback) and
    /// SetIgnoredLayers (phase). Crouch checks headroom before standing; also sets the Head's eye height.
    /// </summary>
    public class PlayerLocomotion : MonoBehaviour
    {
        [SerializeField] PlayerTuning tuning;
        [SerializeField] PlayerInputReader input;
        [Tooltip("Child transform at eye height (same Head as PlayerLook).")]
        [SerializeField] Transform head;

        IPlayerMotor motor;

        Vector3 planarVelocity;   // from WASD, along the ground plane
        Vector3 impulseVelocity;  // from AddImpulse (dash, knockback), fades out
        float upSpeed;            // jump/gravity speed along motor.Up
        int jumpsUsed;
        float lastGroundedTime = -10f;
        bool wasGrounded;
        float currentHeight;
        float currentEyeHeight;

        /// <summary>Fired when a jump starts (jump sound + noise, D-079).</summary>
        public event Action Jumped;
        /// <summary>Fired on touchdown with the downward speed in m/s (landing sound + noise).</summary>
        public event Action<float> Landed;

        public bool IsGrounded { get; private set; }
        public bool IsCrouching { get; private set; }
        public bool IsSprinting { get; private set; }
        /// <summary>Real speed along the ground this frame (after collisions), m/s.</summary>
        public float HorizontalSpeed { get; private set; }
        public IPlayerMotor Motor => motor;

        /// <summary>Extra jumps granted by items (D-033 double jump = 1). Added to tuning.maxJumps.</summary>
        public int BonusJumps { get; set; }
        public int MaxJumps => Mathf.Max(1, (tuning != null ? tuning.maxJumps : 1) + BonusJumps);

        void Awake()
        {
            motor = GetComponent<IPlayerMotor>();
            if (input == null) input = GetComponent<PlayerInputReader>();
            if (head == null) head = transform.Find("Head");

            if (tuning == null || motor == null)
            {
                Debug.LogError("[PlayerLocomotion] Needs a PlayerTuning asset and an IPlayerMotor component (e.g. CharacterControllerMotor).", this);
                enabled = false;
                return;
            }

            currentHeight = tuning.standingHeight;
            currentEyeHeight = tuning.standingEyeHeight;
            ApplyHeight();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Vector3 up = motor.Up;

            UpdateCrouch(dt);
            UpdatePlanar(dt, up);
            UpdateVertical(dt);

            float upSpeedBeforeMove = upSpeed;
            motor.Move((planarVelocity + impulseVelocity + up * upSpeed) * dt);

            if (motor.HitCeiling && upSpeed > 0f) upSpeed = 0f; // bonk: stop rising

            UpdateGrounded(upSpeedBeforeMove);
            HorizontalSpeed = Vector3.ProjectOnPlane(motor.Velocity, up).magnitude;
            impulseVelocity = Vector3.MoveTowards(impulseVelocity, Vector3.zero, tuning.impulseDrag * dt);
        }

        void UpdateCrouch(float dt)
        {
            bool wantsCrouch = input != null && input.CrouchHeld;
            if (wantsCrouch)
                IsCrouching = true;
            else if (IsCrouching && motor.HasHeadroom(tuning.standingHeight, tuning.obstacleMask))
                IsCrouching = false;

            float targetHeight = IsCrouching ? tuning.crouchHeight : tuning.standingHeight;
            float targetEye = IsCrouching ? tuning.crouchEyeHeight : tuning.standingEyeHeight;
            float step = tuning.crouchTransitionSpeed * dt;

            // Only grow if there is room for the next bit of growth (stops popping into low ceilings).
            float nextHeight = Mathf.MoveTowards(currentHeight, targetHeight, step);
            if (nextHeight <= currentHeight || motor.HasHeadroom(nextHeight, tuning.obstacleMask))
                currentHeight = nextHeight;
            currentEyeHeight = Mathf.MoveTowards(currentEyeHeight, targetEye, step);
            ApplyHeight();
        }

        void ApplyHeight()
        {
            motor.SetCapsuleHeight(currentHeight);
            if (head != null)
            {
                Vector3 p = head.localPosition;
                p.y = currentEyeHeight;
                head.localPosition = p;
            }
        }

        void UpdatePlanar(float dt, Vector3 up)
        {
            Vector2 move = input != null ? input.Move : Vector2.zero;
            if (move.sqrMagnitude > 1f) move.Normalize();
            bool moving = move.sqrMagnitude > 0.01f;

            IsSprinting = moving && input.SprintHeld && !IsCrouching && move.y > 0f;

            float speed = IsCrouching ? tuning.crouchSpeed : IsSprinting ? tuning.sprintSpeed : tuning.walkSpeed;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(transform.right, up).normalized;
            Vector3 wish = (forward * move.y + right * move.x) * speed;
            float accel = IsGrounded ? tuning.groundAcceleration : tuning.airAcceleration;
            planarVelocity = Vector3.MoveTowards(planarVelocity, wish, accel * dt);
        }

        void UpdateVertical(float dt)
        {
            if (IsGrounded && upSpeed < 0f)
                upSpeed = -2f; // small push to keep us glued to slopes and steps

            if (input != null && input.JumpPressedThisFrame)
                TryJump();

            upSpeed += tuning.gravity * dt;
        }

        /// <summary>Jump if allowed. First jump needs ground (or coyote time); extra jumps use MaxJumps (D-033).</summary>
        public bool TryJump()
        {
            bool onGround = IsGrounded || Time.time - lastGroundedTime <= tuning.coyoteTime;
            if (onGround && jumpsUsed == 0)
                jumpsUsed = 1;
            else if (jumpsUsed > 0 && jumpsUsed < MaxJumps)
                jumpsUsed++;
            else if (!onGround && jumpsUsed == 0 && MaxJumps > 1)
                jumpsUsed = 2; // walked off a ledge: the air jump also uses up the ground jump
            else
                return false;

            upSpeed = Mathf.Sqrt(2f * tuning.jumpHeight * -tuning.gravity);
            IsGrounded = false;
            lastGroundedTime = -10f; // no coyote double-dip
            Jumped?.Invoke();
            return true;
        }

        void UpdateGrounded(float upSpeedBeforeMove)
        {
            IsGrounded = motor.IsGrounded;
            if (IsGrounded)
            {
                lastGroundedTime = Time.time;
                jumpsUsed = 0;
                if (!wasGrounded)
                    Landed?.Invoke(Mathf.Max(0f, -upSpeedBeforeMove));
            }
            wasGrounded = IsGrounded;
        }

        /// <summary>
        /// Velocity-impulse hook (D-033 dash, knockback, launch pads). The sideways part fades with
        /// tuning.impulseDrag; the part along Up is added to jump/gravity speed.
        /// </summary>
        public void AddImpulse(Vector3 velocityChange)
        {
            Vector3 up = motor.Up;
            float along = Vector3.Dot(velocityChange, up);
            upSpeed += along;
            impulseVelocity += velocityChange - up * along;
        }

        /// <summary>Collision-layer toggle for D-033 phase. Pass 0 to collide with everything again.</summary>
        public void SetIgnoredLayers(LayerMask layers) => motor.SetIgnoredLayers(layers);

        /// <summary>
        /// Teleport through the motor (D-097). yawDelta turns current momentum so you keep moving
        /// "forward" out of a portal. Usually called via PlayerController.Teleport.
        /// </summary>
        public void TeleportTo(Vector3 position, float yawDelta)
        {
            motor.Teleport(position);
            Quaternion turn = Quaternion.AngleAxis(yawDelta, motor.Up);
            planarVelocity = turn * planarVelocity;
            impulseVelocity = turn * impulseVelocity;
        }
    }
}
