using UnityEngine;

namespace TWR.Combat
{
    /// <summary>
    /// Procedural first-person gun motion, with no hands or arms (D-070). Moves only the gun's Model, its
    /// slide and its magazine:
    /// - recoil (D-123): the gun kicks back and flips up on each shot, then springs back;
    /// - reload (D-117): the gun dips and tilts, the magazine slides down out of view and back up,
    ///   then the gun returns. All timings come from GunData and follow Gun.ReloadProgress;
    /// - switching: the gun rises from below while Gun.EquipProgress goes 0 to 1.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class GunViewModel : MonoBehaviour
    {
        [SerializeField] Gun gun;
        [Tooltip("Pivot holding all gun meshes. Recoil and reload move this.")]
        [SerializeField] Transform model;
        [Tooltip("Magazine mesh (child of Model). Slides out during reload.")]
        [SerializeField] Transform magazine;
        [Tooltip("Optional slide mesh that blows back on each shot.")]
        [SerializeField] Transform slide;

        Vector3 modelRestPosition;
        Quaternion modelRestRotation;
        Vector3 magazineRestPosition;
        Vector3 slideRestPosition;
        bool captured;

        float kickBack;   // metres
        float kickPitch;  // degrees
        float slideKick;  // metres

        void Awake()
        {
            if (gun == null) gun = GetComponent<Gun>();
            CaptureRestPose();
        }

        void CaptureRestPose()
        {
            if (captured) return;
            if (model != null) { modelRestPosition = model.localPosition; modelRestRotation = model.localRotation; }
            if (magazine != null) magazineRestPosition = magazine.localPosition;
            if (slide != null) slideRestPosition = slide.localPosition;
            captured = true;
        }

        void OnEnable()
        {
            kickBack = kickPitch = slideKick = 0f;
            if (gun != null) gun.Fired += HandleFired;
        }

        void OnDisable()
        {
            if (gun != null) gun.Fired -= HandleFired;
        }

        void HandleFired()
        {
            GunData data = gun.Data;
            kickBack += data.kickBackDistance;
            kickPitch += data.kickPitchDegrees;
            slideKick = data.slideKickDistance;
        }

        void LateUpdate()
        {
            if (gun == null || gun.Data == null || model == null) return;
            GunData data = gun.Data;

            // Spring back: exponential decay, frame-rate independent.
            float decay = 1f - Mathf.Exp(-data.kickReturnSharpness * Time.deltaTime);
            kickBack = Mathf.Lerp(kickBack, 0f, decay);
            kickPitch = Mathf.Lerp(kickPitch, 0f, decay);
            slideKick = Mathf.Lerp(slideKick, 0f, decay * 1.5f);

            float reload = gun.ReloadProgress;
            float lowered = gun.IsReloading ? LoweredWeight(reload, data) : 0f;
            float magazineOut = gun.IsReloading ? MagazineOutWeight(reload, data) : 0f;
            float equipDrop = 1f - Smooth(gun.EquipProgress);

            model.localPosition = modelRestPosition + new Vector3(
                0f,
                -data.reloadDipDistance * lowered - data.equipDropDistance * equipDrop,
                -kickBack);
            model.localRotation = modelRestRotation * Quaternion.Euler(
                -kickPitch + data.reloadTiltDegrees.x * lowered + 30f * equipDrop,
                0f,
                data.reloadTiltDegrees.y * lowered);

            if (magazine != null)
            {
                // Slide along the magazine's own "down" so it leaves the grip in a straight line.
                Vector3 down = magazine.localRotation * Vector3.down;
                magazine.localPosition = magazineRestPosition + down * (data.magazineDropDistance * magazineOut);
            }
            if (slide != null)
                slide.localPosition = slideRestPosition + Vector3.back * slideKick;
        }

        /// 0 -> 1 while lowering, 1 while the magazine is swapped, 1 -> 0 while raising.
        static float LoweredWeight(float t, GunData d)
        {
            if (t < d.reloadLowerEnd) return Smooth(SafeDivide(t, d.reloadLowerEnd));
            if (t > d.reloadMagInEnd) return Smooth(1f - SafeDivide(t - d.reloadMagInEnd, 1f - d.reloadMagInEnd));
            return 1f;
        }

        /// 0 -> 1 magazine sliding out, 1 while out of view, 1 -> 0 sliding back in.
        static float MagazineOutWeight(float t, GunData d)
        {
            if (t <= d.reloadLowerEnd || t >= d.reloadMagInEnd) return 0f;
            if (t < d.reloadMagOutEnd) return Smooth(SafeDivide(t - d.reloadLowerEnd, d.reloadMagOutEnd - d.reloadLowerEnd));
            if (t < d.reloadMagInStart) return 1f;
            return Smooth(1f - SafeDivide(t - d.reloadMagInStart, d.reloadMagInEnd - d.reloadMagInStart));
        }

        static float SafeDivide(float a, float b) => b > 0.0001f ? Mathf.Clamp01(a / b) : 1f;
        static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
