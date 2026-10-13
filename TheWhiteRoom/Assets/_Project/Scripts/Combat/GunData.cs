using UnityEngine;
using UnityEngine.Localization;
using TWR.Items;

namespace TWR.Combat
{
    /// <summary>
    /// All tunable numbers for one gun (D-119; D-049 gun list). Defaults are the pistol:
    /// 25 damage, semi-auto, 0.15 s between shots, 12-round magazine, 100 m hitscan, ~1.4 s reload.
    /// Also holds the hands-free reload timings (D-117), recoil (D-123) and noise radii
    /// (D-079, D-114). Create via: Project window > right-click > Create > TWR > Gun Data.
    /// </summary>
    [CreateAssetMenu(fileName = "Gun_", menuName = "TWR/Gun Data")]
    public class GunData : ScriptableObject
    {
        [Header("Identity - D-049, D-084")]
        public GunType gunType = GunType.Pistol;
        [Tooltip("Localized name shown on the HUD (D-086). Table 'UI', e.g. key 'gun.pistol.name'.")]
        public LocalizedString displayName = new LocalizedString();
        [Tooltip("Reserve ammo item in the Inventory. D-050: every gun uses the same Ammo item.")]
        public ItemData ammoItem;

        [Header("Firing - D-118, D-119")]
        public float damage = 25f;
        [Tooltip("Off = one shot per click (pistol). On = hold to keep firing (AK later).")]
        public bool automatic = false;
        [Tooltip("Minimum seconds between shots.")]
        [Min(0.01f)] public float fireInterval = 0.15f;
        [Min(1)] public int magazineSize = 12;
        [Tooltip("Hitscan range in metres.")]
        [Min(1f)] public float range = 100f;
        [Tooltip("Random cone half-angle in degrees. 0 = perfectly accurate.")]
        [Min(0f)] public float spreadDegrees = 0f;
        [Tooltip("Impulse pushed into Rigidbodies and passed to IShootables (D-036 knock objects).")]
        [Min(0f)] public float hitForce = 4f;
        [Tooltip("Seconds after switching to this gun before it can fire (it is still raising).")]
        [Min(0f)] public float equipTime = 0.3f;

        [Header("Reload without hands - D-117 (fractions of reloadTime, in order)")]
        [Min(0.1f)] public float reloadTime = 1.4f;
        [Range(0f, 1f)] public float reloadLowerEnd = 0.2f;
        [Range(0f, 1f)] public float reloadMagOutEnd = 0.45f;
        [Range(0f, 1f)] public float reloadMagInStart = 0.55f;
        [Range(0f, 1f)] public float reloadMagInEnd = 0.8f;
        [Tooltip("How far the gun dips during a reload (m).")]
        public float reloadDipDistance = 0.06f;
        [Tooltip("Gun tilt during a reload: x = pitch (negative = muzzle up), y = roll (degrees).")]
        public Vector2 reloadTiltDegrees = new Vector2(-20f, 30f);
        [Tooltip("How far the magazine slides down out of the grip (m). Far enough to leave the screen.")]
        public float magazineDropDistance = 0.3f;
        [Tooltip("How far the gun drops when being switched in (m).")]
        public float equipDropDistance = 0.2f;

        [Header("Recoil - D-123")]
        [Tooltip("Gun kicks back toward the camera by this much (m).")]
        public float kickBackDistance = 0.04f;
        [Tooltip("Gun muzzle flips up by this many degrees.")]
        public float kickPitchDegrees = 8f;
        [Tooltip("Slide travel on each shot (m).")]
        public float slideKickDistance = 0.025f;
        [Tooltip("How fast the gun springs back (higher = snappier).")]
        public float kickReturnSharpness = 14f;
        [Tooltip("Camera pitch kick per shot (degrees). Recovers on its own.")]
        public float cameraKickDegrees = 1f;
        [Tooltip("How fast the camera kick recovers (higher = faster).")]
        public float cameraKickRecoverSharpness = 10f;

        [Header("Muzzle flash - D-100 (cheap)")]
        public float muzzleFlashDuration = 0.05f;

        [Header("Noise radii (m) - D-079, D-114")]
        [Tooltip("Gunshot noise at the muzzle. Dry-fire clicks make no noise.")]
        public float gunshotNoiseRadius = 25f;
        [Tooltip("Impact noise where the bullet lands, credited to the shooter.")]
        public float impactNoiseRadius = 6f;

        [Header("Placeholder audio (optional, can be empty)")]
        public AudioClip fireClip;
        public AudioClip dryFireClip;
        public AudioClip reloadClip;

        void OnValidate()
        {
            // Keep the reload phases in order so the animation never runs backwards.
            reloadMagOutEnd = Mathf.Max(reloadMagOutEnd, reloadLowerEnd);
            reloadMagInStart = Mathf.Max(reloadMagInStart, reloadMagOutEnd);
            reloadMagInEnd = Mathf.Max(reloadMagInEnd, reloadMagInStart);
        }
    }
}
