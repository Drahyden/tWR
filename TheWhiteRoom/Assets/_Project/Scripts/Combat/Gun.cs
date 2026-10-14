using System;
using UnityEngine;
using TWR.Core;
using TWR.Items;
using TWR.Portals;

namespace TWR.Combat
{
    /// <summary>
    /// The rules of one gun: magazine, fire rate, hitscan shot, reload timer (D-118, D-119, D-117).
    /// Shoots a ray from the CAMERA CENTRE (same origin as the interaction ray, D-112), so bullets go exactly
    /// where the crosshair dot is, also through one open portal via the shared PortalRaycast (D-143). Hits are resolved to IDamageable (HP) and IShootable (puzzles), both found
    /// on the collider or its parents. Emits a Gunshot noise at the muzzle and an Impact noise at the hit
    /// point (D-079). Reserve ammo is read from / taken out of the owner's Inventory (D-050).
    /// Visuals (recoil, reload motion) are in GunViewModel; WeaponInventory decides WHEN to call TryFire.
    /// </summary>
    public class Gun : MonoBehaviour
    {
        [SerializeField] GunData data;
        [Tooltip("Barrel tip: gunshot noise origin and muzzle flash position.")]
        [SerializeField] Transform muzzle;
        [SerializeField] MuzzleFlash muzzleFlash;
        [Tooltip("What bullets can hit. Must NOT include the Weapon layer or Ignore Raycast (pickups, trigger zones). " +
                 "Keep Interactable ON so buttons and switches stop bullets.")]
        [SerializeField] LayerMask hitMask = ~0;

        static readonly Vector3 ScreenCentre = new Vector3(0.5f, 0.5f, 0f);

        GameObject owner;
        Transform ownerRoot;
        Camera aimCamera;
        Inventory inventory;
        AudioSource audioSource;

        int loadedRounds;
        float nextFireTime;
        float equipStartTime;
        bool reloading;
        float reloadStartTime;
        float reloadEndTime;

        /// <summary>A real shot left the barrel (recoil, camera kick).</summary>
        public event Action Fired;
        /// <summary>Loaded or reserve ammo may have changed (HUD).</summary>
        public event Action AmmoChanged;
        /// <summary>A bullet did something useful (hit marker, D-091).</summary>
        public event Action<HitResult> HitLanded;

        public GunData Data => data;
        public int LoadedRounds => loadedRounds;
        public int ReserveRounds => inventory != null && data != null ? inventory.Count(data.ammoItem) : 0;
        public bool IsReloading => reloading;

        /// <summary>0..1 through the current reload, 0 when not reloading. GunViewModel animates from this.</summary>
        public float ReloadProgress => reloading ? Mathf.InverseLerp(reloadStartTime, reloadEndTime, Time.time) : 0f;

        /// <summary>0..1 how far the "raise after switching" is done. 1 = ready.</summary>
        public float EquipProgress => data == null || data.equipTime <= 0f ? 1f : Mathf.Clamp01((Time.time - equipStartTime) / data.equipTime);

        void Awake()
        {
            if (data == null)
            {
                Debug.LogError($"[Gun] '{name}' has no GunData. Run TWR > Build Test Scene.", this);
                enabled = false;
                return;
            }
            loadedRounds = data.magazineSize; // D-120: start with a full magazine
            if (muzzle == null) muzzle = transform;
        }

        /// <summary>Called once by WeaponInventory: who holds the gun, where it aims from, where reserve ammo is.</summary>
        public void Bind(GameObject holder, Camera aim, Inventory ammoSource)
        {
            owner = holder;
            ownerRoot = holder != null ? holder.transform : null;
            aimCamera = aim;
            inventory = ammoSource;
            if (holder != null) audioSource = holder.GetComponent<AudioSource>();
        }

        void OnEnable()
        {
            equipStartTime = Time.time;
            if (data != null) nextFireTime = Mathf.Max(nextFireTime, Time.time + data.equipTime);
            AmmoChanged?.Invoke();
        }

        void OnDisable()
        {
            CancelReload(); // switching weapons cancels the reload (D-117)
        }

        void Update()
        {
            if (reloading && Time.time >= reloadEndTime) FinishReload();
        }

        /// <summary>Pull the trigger once. Returns true if a bullet was fired.</summary>
        public bool TryFire()
        {
            if (data == null || aimCamera == null || reloading || Time.time < nextFireTime) return false;

            if (loadedRounds <= 0)
            {
                // Empty: reload automatically if we have reserve ammo, otherwise a silent dry click.
                if (ReserveRounds > 0) StartReload();
                else DryFire();
                return false;
            }

            nextFireTime = Time.time + data.fireInterval;
            loadedRounds--;

            Vector3 muzzlePosition = muzzle.position;
            NoiseSystem.Emit(muzzlePosition, data.gunshotNoiseRadius, NoiseType.Gunshot, owner);
            if (muzzleFlash != null) muzzleFlash.Play(data.muzzleFlashDuration);
            PlayClip(data.fireClip);

            FireRay();

            Fired?.Invoke();
            AmmoChanged?.Invoke();
            return true;
        }

        void FireRay()
        {
            Ray ray = aimCamera.ViewportPointToRay(ScreenCentre);
            Vector3 direction = ApplySpread(ray.direction);

            // Triggers (pickups, trigger zones) are ignored; so are the shooter's own colliders.
            // The ray may go through ONE open portal (D-143); the hit point is then where the target
            // really is, so the Impact noise is made there, not at the portal.
            if (!PortalRaycast.Raycast(ray.origin, direction, data.range, hitMask, QueryTriggerInteraction.Ignore,
                                       ownerRoot, out PortalRayHit shot))
                return; // hit nothing within range

            HitResult result = ResolveHit(shot.hit, shot.direction);
            NoiseSystem.Emit(shot.hit.point, data.impactNoiseRadius, NoiseType.Impact, owner);
            if (result != HitResult.None) HitLanded?.Invoke(result);
        }

        HitResult ResolveHit(in RaycastHit hit, Vector3 direction)
        {
            HitResult result = HitResult.None;
            Collider collider = hit.collider;

            IDamageable damageable = collider.GetComponentInParent<IDamageable>();
            if (damageable != null && damageable.IsAlive)
            {
                damageable.TakeDamage(new DamageInfo(data.damage, owner, hit.point, direction, data));
                result |= HitResult.Damaged;
                if (!damageable.IsAlive) result |= HitResult.Killed;
            }

            IShootable shootable = collider.GetComponentInParent<IShootable>();
            if (shootable != null)
            {
                var info = new ShotHitInfo(hit.point, hit.normal, direction, data.hitForce, data, owner, collider);
                if (shootable.OnShot(info)) result |= HitResult.Triggered;
            }

            Rigidbody body = collider.attachedRigidbody;
            if (body != null && !body.isKinematic && data.hitForce > 0f)
                body.AddForceAtPosition(direction * data.hitForce, hit.point, ForceMode.Impulse);

            return result;
        }

        Vector3 ApplySpread(Vector3 direction)
        {
            if (data.spreadDegrees <= 0f) return direction;
            Vector2 offset = UnityEngine.Random.insideUnitCircle * data.spreadDegrees;
            Transform cam = aimCamera.transform;
            return Quaternion.AngleAxis(offset.x, cam.up) * Quaternion.AngleAxis(offset.y, cam.right) * direction;
        }

        void DryFire()
        {
            nextFireTime = Time.time + data.fireInterval;
            PlayClip(data.dryFireClip); // no noise event on purpose (D-119)
        }

        /// <summary>Start a reload if the magazine isn't full and there is reserve ammo. Returns true if started.</summary>
        public bool StartReload()
        {
            if (data == null || reloading || !isActiveAndEnabled) return false;
            if (loadedRounds >= data.magazineSize || ReserveRounds <= 0) return false;

            reloading = true;
            reloadStartTime = Time.time;
            reloadEndTime = Time.time + data.reloadTime;
            PlayClip(data.reloadClip);
            return true;
        }

        /// <summary>Stop a reload without loading anything (weapon switch, death).</summary>
        public void CancelReload()
        {
            reloading = false;
        }

        void FinishReload()
        {
            reloading = false;
            int needed = data.magazineSize - loadedRounds;
            int taken = inventory != null ? inventory.Remove(data.ammoItem, needed) : 0;
            loadedRounds += taken;
            AmmoChanged?.Invoke();
        }

        void PlayClip(AudioClip clip)
        {
            if (clip != null && audioSource != null) audioSource.PlayOneShot(clip);
        }
    }
}
