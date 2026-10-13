using UnityEngine;

namespace TWR.Player
{
    /// <summary>
    /// The player's "body" contract: the ONLY way gameplay code moves the player or asks about collisions.
    /// Why separate (D-053c): Unity's CharacterController is always upright, so gravity-shift rooms will need
    /// a custom kinematic motor. That future motor just implements this interface and replaces
    /// CharacterControllerMotor on the Player prefab; PlayerLocomotion, look, lean, sounds stay unchanged.
    /// </summary>
    public interface IPlayerMotor
    {
        /// <summary>"Up" for this body. World up for now; a gravity-shift motor (D-053c) will change it.</summary>
        Vector3 Up { get; }

        /// <summary>Standing on ground after the last Move.</summary>
        bool IsGrounded { get; }

        /// <summary>Head hit something during the last Move.</summary>
        bool HitCeiling { get; }

        /// <summary>Real velocity achieved by the last Move (after collisions), m/s.</summary>
        Vector3 Velocity { get; }

        /// <summary>Current capsule height (m). Feet stay at the transform position.</summary>
        float Height { get; }

        /// <summary>Move by this displacement (metres, already multiplied by deltaTime), sliding along walls.</summary>
        void Move(Vector3 displacement);

        /// <summary>Resize the capsule, feet stay planted (crouch).</summary>
        void SetCapsuleHeight(float height);

        /// <summary>True if the capsule could grow to targetHeight without hitting obstacles (stand-up check).</summary>
        bool HasHeadroom(float targetHeight, LayerMask obstacles);

        /// <summary>Jump instantly to a position (feet), safe with the physics body (D-097 portals).</summary>
        void Teleport(Vector3 position);

        /// <summary>Pass through these layers until called again with 0 (D-033 phase).</summary>
        void SetIgnoredLayers(LayerMask layers);
    }
}
