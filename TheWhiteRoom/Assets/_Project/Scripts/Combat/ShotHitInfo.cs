using UnityEngine;

namespace TWR.Combat
{
    /// <summary>
    /// Everything a shot-reactive puzzle object needs to know about the bullet that hit it
    /// (D-118, D-036, D-105): where, surface normal, bullet direction, push force, and which gun.
    /// The Gun field lets gun-specific switches filter shots (see GunRequirement). Struct: no garbage.
    /// </summary>
    public readonly struct ShotHitInfo
    {
        public readonly Vector3 Point;
        public readonly Vector3 Normal;
        /// <summary>Bullet travel direction, normalized.</summary>
        public readonly Vector3 Direction;
        /// <summary>Push strength (impulse) from GunData.hitForce, for knock-over puzzles (D-105 shotgun).</summary>
        public readonly float Force;
        public readonly GunData Gun;
        public readonly GameObject Shooter;
        public readonly Collider Collider;

        public ShotHitInfo(Vector3 point, Vector3 normal, Vector3 direction, float force, GunData gun, GameObject shooter, Collider collider)
        {
            Point = point;
            Normal = normal;
            Direction = direction;
            Force = force;
            Gun = gun;
            Shooter = shooter;
            Collider = collider;
        }
    }
}
