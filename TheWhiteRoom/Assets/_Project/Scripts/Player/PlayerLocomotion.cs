using System;
using UnityEngine;
using TWR.Core;
using TWR.Level;

namespace TWR.Player
{
    /// <summary>
    /// The movement RULES of the player (D-032): walk, sprint, crouch, jump, gravity. It decides the velocity
    /// and hands it to the IPlayerMotor, which does the actual collision-aware moving (swappable, D-053c).
    /// Built to grow into D-033 abilities: jump COUNT (double jump), AddImpulse (dash/knockback) and
    /// SetIgnoredLayers (phase). Crouch checks headroom before standing; also sets the Head's eye height.
    /// ResetMotion stops all movement (section transitions, D-167); ResetForNewRun also removes item abilities (D-162).
    /// R4 (D-182, D-191): ApplyKnockback = a planar push (Bull fast charge) moved ONLY through IPlayerMotor.Move, never a
    /// teleport: its distance is first shortened by a capsule cast to the first wall/obstacle, and it stops at once if the
    /// motor reports it is blocked. Falling is capped at PlayerTuning.maxFallSpeed (about 40 m/s); there is no fall damage.
    /// D-241: standing on a MovingPlatform, the player is first moved by the platform's own step this frame (through
    /// IPlayerMotor.Move, the platform already moved earlier in the frame), then by their own movement, so riding is
    /// exact. After jumping or walking off, the platform's sideways speed is kept until the next landing.
    /// </summary>
    public class PlayerLocomotion : MonoBehaviour, IRunResettable
    {
        [SerializeField] PlayerTuning tuning;
        [SerializeField] PlayerInputReader input;
        [Tooltip("Child transform at eye height (same Head as PlayerLook).")]
        [SerializeField] Transform head;

        IPlayerMotor motor;

        Vector3 planarVelocity;   // from WASD, along the ground plane
        Vector3 impulseVelocity;  // from AddImpulse (dash, launch pads), fades out
        Vector3 knockbackVelocity; // from ApplyKnockback (D-182), slows down with tuning.knockbackDeceleration
        float knockbackRemaining;  // metres of knockback left (clamped by the capsule cast)
        float upSpeed;            // jump/gravity speed along motor.Up
        int jumpsUsed;
        float lastGroundedTime = -10f;
        bool wasGrounded;
        float currentHeight;
        float currentEyeHeight;
        MovingPlatform groundPlatform; // D-241: the platform under the feet after the last move (null = none)
        Collider groundCollider;       // what the ground probe hit last, so the platform lookup runs only on change
        Vector3 carriedVelocity;       // the platform's sideways speed, kept while airborne after leaving it

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

            // D-241: ride first (the platform moved earlier this frame), so our own move below stays relative to it.
            if (IsGrounded && groundPlatform != null && groundPlatform.FrameDelta.sqrMagnitude > 0f)
                motor.Move(groundPlatform.FrameDelta);

            float upSpeedBeforeMove = upSpeed;
            Vector3 knockStep = KnockbackStep(dt);
            motor.Move((planarVelocity + impulseVelocity + carriedVelocity + up * upSpeed) * dt + knockStep);

            if (motor.HitCeiling && upSpeed > 0f) upSpeed = 0f; // bonk: stop rising

            UpdateGrounded(upSpeedBeforeMove);
            UpdateGroundPlatform(up);
            HorizontalSpeed = Vector3.ProjectOnPlane(motor.Velocity, up).magnitude;
            impulseVelocity = Vector3.MoveTowards(impulseVelocity, Vector3.zero, tuning.impulseDrag * dt);
            UpdateKnockback(dt, knockStep);
        }

        // ---------- knockback (D-182, D-191) ----------

        /// <summary>
        /// Push the player along the ground by up to distance metres (Bull fast charge, D-182). Planar only. The distance is
        /// shortened first by a capsule cast (the player's capsule size) against everything solid except bodies, so the
        /// push ends at the first wall or column; then it is applied over the next frames through IPlayerMotor.Move with a
        /// start speed of sqrt(2 x deceleration x distance). Never a teleport; deals no damage itself (D-190).
        /// </summary>
        public void ApplyKnockback(Vector3 direction, float distance)
        {
            if (motor == null || tuning == null || distance <= 0f) return;
            Vector3 up = motor.Up;
            Vector3 flat = Vector3.ProjectOnPlane(direction, up);
            if (flat.sqrMagnitude < 0.0001f) return;
            flat.Normalize();

            float allowed = ClampKnockbackDistance(flat, distance, up);
            if (allowed <= 0.05f)
            {
                knockbackVelocity = Vector3.zero;
                knockbackRemaining = 0f;
                return;
            }
            knockbackRemaining = allowed;
            knockbackVelocity = flat * Mathf.Sqrt(2f * tuning.knockbackDeceleration * allowed);
        }

        /// <summary>True while a knockback is still pushing the player.</summary>
        public bool IsKnockedBack => knockbackRemaining > 0f;

        float ClampKnockbackDistance(Vector3 direction, float distance, Vector3 up)
        {
            float radius = Mathf.Max(0.05f, tuning.controllerRadius * 0.95f);
            float height = Mathf.Max(motor.Height, radius * 2f + 0.4f);
            Vector3 feet = transform.position;
            // Bottom sphere lifted above step height, so small floor bumps don't stop the push.
            Vector3 bottom = feet + up * (radius + 0.35f);
            Vector3 top = feet + up * Mathf.Max(radius + 0.35f, height - radius);
            int mask = GameLayers.WithoutActors(tuning.obstacleMask);
            if (Physics.CapsuleCast(bottom, top, radius, direction, out RaycastHit hit, distance, mask, QueryTriggerInteraction.Ignore))
                return Mathf.Max(0f, hit.distance - 0.05f);
            return distance;
        }

        Vector3 KnockbackStep(float dt)
        {
            if (knockbackRemaining <= 0f) return Vector3.zero;
            Vector3 step = knockbackVelocity * dt;
            float length = step.magnitude;
            if (length > knockbackRemaining) step *= knockbackRemaining / length;
            return step;
        }

        void UpdateKnockback(float dt, Vector3 intended)
        {
            if (knockbackRemaining <= 0f) return;
            float intendedLength = intended.magnitude;
            knockbackRemaining -= intendedLength;

            // Blocked by something the cast didn't see (a moving body, a corner): stop at once (D-191).
            if (dt > 0f && intendedLength > 0.0001f)
            {
                float intendedSpeed = intendedLength / dt;
                float achieved = Vector3.Dot(motor.Velocity, intended / intendedLength);
                if (intendedSpeed > 1f && achieved < intendedSpeed * tuning.knockbackBlockedRatio) knockbackRemaining = 0f;
            }

            knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, tuning.knockbackDeceleration * dt);
            if (knockbackRemaining <= 0f || knockbackVelocity.sqrMagnitude < 0.0001f)
            {
                knockbackRemaining = 0f;
                knockbackVelocity = Vector3.zero;
            }
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
            if (upSpeed < -tuning.maxFallSpeed) upSpeed = -tuning.maxFallSpeed; // D-191: capped fall, no fall damage
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
            if (groundPlatform != null) carriedVelocity = Vector3.ProjectOnPlane(groundPlatform.Velocity, motor.Up); // D-241
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

        // ---------- moving platforms (D-241) ----------

        /// After the move: on the ground, find the MovingPlatform under the feet (one short sphere cast; the component
        /// lookup only when the collider changes). In the air, keep the last platform's sideways speed until landing.
        void UpdateGroundPlatform(Vector3 up)
        {
            if (!IsGrounded)
            {
                if (groundPlatform != null) carriedVelocity = Vector3.ProjectOnPlane(groundPlatform.Velocity, up);
                groundPlatform = null;
                groundCollider = null;
                return;
            }
            carriedVelocity = Vector3.zero;

            float radius = Mathf.Max(0.05f, tuning.controllerRadius * 0.9f);
            Vector3 origin = transform.position + up * (radius + 0.05f);
            int mask = GameLayers.WithoutActors(tuning.obstacleMask);
            if (!Physics.SphereCast(origin, radius, -up, out RaycastHit hit, 0.3f, mask, QueryTriggerInteraction.Ignore))
            {
                groundPlatform = null;
                groundCollider = null;
                return;
            }
            if (hit.collider == groundCollider) return;
            groundCollider = hit.collider;
            groundPlatform = hit.collider.GetComponentInParent<MovingPlatform>();
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

        /// <summary>
        /// Stop dead: no walking speed, no impulse, no falling speed (D-167: the SectionLoader calls this before it
        /// freezes the player, so footsteps stop and nobody keeps sliding after a teleport).
        /// </summary>
        public void ResetMotion()
        {
            planarVelocity = Vector3.zero;
            impulseVelocity = Vector3.zero;
            knockbackVelocity = Vector3.zero;
            knockbackRemaining = 0f;
            carriedVelocity = Vector3.zero;
            groundPlatform = null;
            groundCollider = null;
            upSpeed = 0f;
            jumpsUsed = 0;
            HorizontalSpeed = 0f;
            IsSprinting = false;
        }

        /// <summary>Run start (D-162): abilities found in the run are gone (D-033), movement stops.</summary>
        public void ResetForNewRun()
        {
            BonusJumps = 0;
            if (motor != null) motor.SetIgnoredLayers(0);
            ResetMotion();
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
            knockbackVelocity = turn * knockbackVelocity;
        }
    }
}
