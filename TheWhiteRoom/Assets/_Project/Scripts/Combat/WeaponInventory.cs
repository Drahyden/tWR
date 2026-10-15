using System;
using System.Collections.Generic;
using UnityEngine;
using TWR.Core;
using TWR.Items;
using TWR.Player;

namespace TWR.Combat
{
    /// <summary>
    /// The player's 5 gun slots (D-035) and the only script that turns weapon input into gun actions.
    /// Keys 1-5 pick a slot, the mouse wheel steps through slots, skipping empty ones and wrapping (D-088).
    /// LMB fires (held for automatic guns), R reloads (D-110). Switching hides the old gun, which cancels
    /// its reload (D-117); loaded rounds stay with each gun. Also gives the starting reserve ammo
    /// (D-120), adds the camera kick through PlayerLook (D-123) and relays HUD events.
    /// Guns, magazines and reserve ammo carry over between sections (D-162); ResetForNewRun (IRunResettable) puts
    /// everything back to the run-start state: inventory emptied, every magazine full, starting reserve, slot 1.
    /// </summary>
    [DefaultExecutionOrder(40)]
    public class WeaponInventory : MonoBehaviour, IRunResettable
    {
        public const int SlotCount = 5;

        [SerializeField] PlayerTuning tuning;
        [SerializeField] PlayerInputReader input;
        [SerializeField] PlayerLook look;
        [SerializeField] Inventory inventory;
        [Tooltip("Shots aim from the centre of this camera.")]
        [SerializeField] Camera aimCamera;
        [Tooltip("Guns in slots 1-5 (children of the WeaponHolder). Empty slots are skipped.")]
        [SerializeField] Gun[] slots = new Gun[SlotCount];

        int currentIndex = -1;
        Action onGunAmmoChanged;
        Action onGunFired;
        Action<HitResult> onGunHit;

        /// <summary>The equipped gun changed (null = none).</summary>
        public event Action<Gun> CurrentGunChanged;
        /// <summary>Loaded or reserve ammo of the equipped gun may have changed.</summary>
        public event Action AmmoChanged;
        /// <summary>The equipped gun's bullet did something (hit marker, D-091).</summary>
        public event Action<HitResult> HitLanded;

        public Gun Current => currentIndex >= 0 ? slots[currentIndex] : null;
        public int CurrentSlot => currentIndex;
        public Inventory Inventory => inventory;

        void Awake()
        {
            if (input == null) input = GetComponent<PlayerInputReader>();
            if (look == null) look = GetComponent<PlayerLook>();
            if (inventory == null) inventory = GetComponent<Inventory>();
            if (aimCamera == null) aimCamera = GetComponentInChildren<Camera>();
            if (slots == null || slots.Length != SlotCount) Array.Resize(ref slots, SlotCount);

            onGunAmmoChanged = () => AmmoChanged?.Invoke(); // cached delegates: no garbage when switching
            onGunFired = HandleGunFired;
            onGunHit = r => HitLanded?.Invoke(r);

            for (int i = 0; i < SlotCount; i++)
            {
                if (slots[i] == null) continue;
                slots[i].Bind(gameObject, aimCamera, inventory);
                slots[i].gameObject.SetActive(false);
            }
        }

        void Start()
        {
            ResetForNewRun(); // the run-start loadout (D-120); safe to run again later
        }

        /// <summary>
        /// Adds the GunData of every gun the player starts a run with (D-120; slots are fixed until gun pickups exist,
        /// D-035). Seeded puzzle variants may only require these guns (D-130, D-173). Section load only, not per frame.
        /// </summary>
        public void CollectRunStartGuns(List<GunData> result)
        {
            if (result == null || slots == null) return;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && slots[i].Data != null && !result.Contains(slots[i].Data)) result.Add(slots[i].Data);
        }

        /// <summary>
        /// Run-start loadout (D-120, D-162): the inventory is emptied (D-037 items found in the run are gone), every
        /// gun gets a full magazine, the starting reserve goes into the inventory and slot 1 is equipped. Calling it
        /// twice gives the same result (never double ammo). Gun slots are fixed until gun pickups exist (D-035).
        /// </summary>
        public void ResetForNewRun()
        {
            for (int i = 0; i < SlotCount; i++)
                if (slots[i] != null) slots[i].ResetForNewRun();

            if (inventory != null)
            {
                inventory.Clear();
                Gun first = FirstGun();
                if (first != null && first.Data != null && tuning != null)
                    inventory.Add(first.Data.ammoItem, tuning.startingReserveAmmo);
            }

            for (int i = 0; i < SlotCount; i++)
            {
                if (slots[i] == null) continue;
                if (i == currentIndex)
                {
                    CurrentGunChanged?.Invoke(slots[i]); // already equipped: just refresh the HUD
                    AmmoChanged?.Invoke();
                }
                else Equip(i);
                break;
            }
        }

        void OnEnable()
        {
            if (inventory != null) inventory.Changed += HandleInventoryChanged;
        }

        void OnDisable()
        {
            if (inventory != null) inventory.Changed -= HandleInventoryChanged;
        }

        void Update()
        {
            if (input == null) return;

            int slot = input.WeaponSlotPressedThisFrame;
            if (slot >= 0) Equip(slot);

            int scroll = input.WeaponScrollStep;
            if (scroll != 0) EquipNext(scroll);

            Gun gun = Current;
            if (gun == null) return;

            bool trigger = gun.Data.automatic ? input.ShootHeld : input.ShootPressedThisFrame;
            if (trigger) gun.TryFire();
            if (input.ReloadPressedThisFrame) gun.StartReload();
        }

        /// <summary>Switch to a slot (0-4). Ignored if the slot is empty or already equipped.</summary>
        public void Equip(int index)
        {
            if (index < 0 || index >= SlotCount || slots[index] == null || index == currentIndex) return;

            Gun old = Current;
            if (old != null)
            {
                old.AmmoChanged -= onGunAmmoChanged;
                old.Fired -= onGunFired;
                old.HitLanded -= onGunHit;
                old.gameObject.SetActive(false); // OnDisable cancels its reload
            }

            currentIndex = index;
            Gun gun = slots[index];
            gun.AmmoChanged += onGunAmmoChanged;
            gun.Fired += onGunFired;
            gun.HitLanded += onGunHit;
            gun.gameObject.SetActive(true);

            CurrentGunChanged?.Invoke(gun);
            AmmoChanged?.Invoke();
        }

        /// <summary>Step +1/-1 through the slots, skipping empty ones and wrapping around (D-088).</summary>
        public void EquipNext(int direction)
        {
            int step = direction > 0 ? 1 : -1;
            int start = currentIndex < 0 ? 0 : currentIndex;
            for (int i = 1; i <= SlotCount; i++)
            {
                int index = ((start + step * i) % SlotCount + SlotCount) % SlotCount;
                if (slots[index] != null)
                {
                    Equip(index);
                    return;
                }
            }
        }

        void HandleGunFired()
        {
            GunData data = Current != null ? Current.Data : null;
            if (data != null && look != null)
                look.AddViewKick(data.cameraKickDegrees, data.cameraKickRecoverSharpness);
        }

        void HandleInventoryChanged(ItemData item, int count)
        {
            Gun gun = Current;
            if (gun != null && gun.Data.ammoItem == item) AmmoChanged?.Invoke();
        }

        Gun FirstGun()
        {
            for (int i = 0; i < SlotCount; i++)
                if (slots[i] != null) return slots[i];
            return null;
        }
    }
}
