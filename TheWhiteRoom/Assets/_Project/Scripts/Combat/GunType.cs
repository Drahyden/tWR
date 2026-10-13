using System;

namespace TWR.Combat
{
    /// <summary>The five gun kinds (D-049). Each GunData says which one it is.</summary>
    public enum GunType
    {
        Pistol,
        Revolver,
        Shotgun,
        AK,
        ScopedRifle
    }

    /// <summary>
    /// Optional "which gun counts" filter for shootable puzzle objects (D-118, D-105).
    /// Default = any gun (normal switches). For a gun-specific switch tick requireSpecificGun and pick
    /// the type, e.g. Pistol for a pistol-only switch. It checks the gun TYPE, not the asset, so gun
    /// variants or upgraded copies still count.
    /// </summary>
    [Serializable]
    public struct GunRequirement
    {
        public bool requireSpecificGun;
        public GunType requiredGun;

        public static GunRequirement Any => default;
        public static GunRequirement Only(GunType type) => new GunRequirement { requireSpecificGun = true, requiredGun = type };

        public bool Accepts(GunData gun)
        {
            if (!requireSpecificGun) return true;
            return gun != null && gun.gunType == requiredGun;
        }
    }
}
