using UnityEngine;

namespace TWR.Combat
{
    /// <summary>
    /// One hit of damage: how much, who did it, where and from which direction (D-118).
    /// Carries the attacker so deaths can be credited to the player for performance scoring (D-029).
    /// A struct so dealing damage never allocates garbage (D-100).
    /// </summary>
    public readonly struct DamageInfo
    {
        public readonly float Amount;
        /// <summary>Who caused it (the player for gunshots). May be null for hazards.</summary>
        public readonly GameObject Attacker;
        public readonly Vector3 Point;
        /// <summary>Direction the damage travelled (e.g. the bullet direction), normalized.</summary>
        public readonly Vector3 Direction;
        /// <summary>The gun used, or null if not a gun.</summary>
        public readonly GunData Gun;

        public DamageInfo(float amount, GameObject attacker, Vector3 point, Vector3 direction, GunData gun)
        {
            Amount = amount;
            Attacker = attacker;
            Point = point;
            Direction = direction;
            Gun = gun;
        }
    }
}
