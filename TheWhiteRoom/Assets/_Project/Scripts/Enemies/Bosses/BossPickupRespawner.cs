using UnityEngine;
using TWR.Items;

namespace TWR.Enemies
{
    /// <summary>
    /// Boss-arena ammo respawn (D-188, extends D-130 to boss fights): while the arena's boss is alive, an ammo pickup that
    /// was emptied comes back at the same (rolled) spot after BossData.ammoRespawnSeconds (about 20 s), so the fight can
    /// always be finished. Once the boss is defeated nothing respawns any more. The health pickup is NOT listed here
    /// (D-194: no respawn). The pickups are told to hide instead of destroying themselves (ItemPickup.KeepWhenEmpty).
    /// Checks a handful of timers per frame; no searches (D-100).
    /// </summary>
    public class BossPickupRespawner : MonoBehaviour
    {
        [SerializeField] BossEncounter boss;
        [Tooltip("The arena's ammo pickups (D-188: 4).")]
        [SerializeField] ItemPickup[] pickups = new ItemPickup[0];

        float[] respawnAt;
        System.Action<ItemPickup> onDepleted;

        public int PickupCount => pickups.Length;

        void Awake()
        {
            respawnAt = new float[pickups.Length];
            onDepleted = HandleDepleted;
            for (int i = 0; i < pickups.Length; i++)
            {
                respawnAt[i] = -1f;
                if (pickups[i] == null) continue;
                pickups[i].KeepWhenEmpty = true; // hide when empty, so it can come back
                pickups[i].Depleted += onDepleted;
            }
        }

        void OnDestroy()
        {
            for (int i = 0; i < pickups.Length; i++)
                if (pickups[i] != null) pickups[i].Depleted -= onDepleted;
        }

        void HandleDepleted(ItemPickup pickup)
        {
            if (boss != null && boss.IsDefeated) return;
            float seconds = boss != null && boss.Data != null ? boss.Data.ammoRespawnSeconds : 20f;
            for (int i = 0; i < pickups.Length; i++)
                if (pickups[i] == pickup) respawnAt[i] = Time.time + seconds;
            Debug.Log($"[Boss] Ammo pickup '{pickup.name}' taken: back in {seconds:0} s while the boss lives (D-188).", this);
        }

        void Update()
        {
            bool bossAlive = boss == null || !boss.IsDefeated;
            float now = Time.time;
            for (int i = 0; i < respawnAt.Length; i++)
            {
                if (respawnAt[i] < 0f || now < respawnAt[i]) continue;
                respawnAt[i] = -1f;
                if (!bossAlive || pickups[i] == null) continue;
                pickups[i].Refill();
            }
        }
    }
}
