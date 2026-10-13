using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using TWR.Combat;

namespace TWR.UI
{
    /// <summary>
    /// HUD ammo and gun name (D-086, D-122), both localized (D-084).
    /// Gun name = the equipped GunData's displayName (e.g. UI/gun.pistol.name).
    /// Ammo = the localized pattern UI/hud.ammo ("{0} / {1}" in English) filled with magazine and reserve.
    /// We use TextMeshPro's SetText(pattern, a, b) instead of Smart Strings or string.Format: same {0}/{1}
    /// placeholders translators know, but zero garbage per shot (D-100). Only updates on events.
    /// </summary>
    public class WeaponHudUI : MonoBehaviour
    {
        [SerializeField] WeaponInventory weapons;
        [SerializeField] TMP_Text gunNameLabel;
        [SerializeField] TMP_Text ammoLabel;
        [Tooltip("Pattern with {0} = rounds in the magazine, {1} = reserve rounds. Table 'UI', key 'hud.ammo'.")]
        [SerializeField] LocalizedString ammoFormat = new LocalizedString("UI", "hud.ammo");

        const string FallbackAmmoFormat = "{0} / {1}"; // numbers only, used if localization is missing

        LocalizedTextBinding gunNameBinding;
        LocalizedTextBinding ammoFormatBinding;
        string ammoPattern = FallbackAmmoFormat;

        void Awake()
        {
            if (weapons == null) weapons = FindFirstObjectByType<WeaponInventory>(); // once, not in Update
            gunNameBinding = new LocalizedTextBinding(this, HandleGunName);
            ammoFormatBinding = new LocalizedTextBinding(this, HandleAmmoFormat, FallbackAmmoFormat);
        }

        void OnEnable()
        {
            ammoFormatBinding.Set(ammoFormat);
            if (weapons == null) return;
            weapons.CurrentGunChanged += HandleGunChanged;
            weapons.AmmoChanged += RefreshAmmo;
            HandleGunChanged(weapons.Current);
        }

        void OnDisable()
        {
            if (weapons != null)
            {
                weapons.CurrentGunChanged -= HandleGunChanged;
                weapons.AmmoChanged -= RefreshAmmo;
            }
            gunNameBinding.Clear();
            ammoFormatBinding.Clear();
        }

        void HandleGunChanged(Gun gun)
        {
            gunNameBinding.Set(gun != null && gun.Data != null ? gun.Data.displayName : null);
            RefreshAmmo();
        }

        void HandleGunName(string value)
        {
            if (gunNameLabel != null) gunNameLabel.text = value;
        }

        void HandleAmmoFormat(string value)
        {
            ammoPattern = string.IsNullOrEmpty(value) ? FallbackAmmoFormat : value;
            RefreshAmmo();
        }

        void RefreshAmmo()
        {
            if (ammoLabel == null) return;
            Gun gun = weapons != null ? weapons.Current : null;
            if (gun == null)
            {
                ammoLabel.text = string.Empty;
                return;
            }
            ammoLabel.SetText(ammoPattern, gun.LoadedRounds, gun.ReserveRounds); // no allocation
        }
    }
}
