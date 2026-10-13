using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using TWR.Combat;

namespace TWR.Player
{
    /// <summary>
    /// The player's HP (D-034). Max HP comes from PlayerTuning. TakeDamage lowers it and raises Changed
    /// (HP bar, D-086, D-122); at 0 it raises Died and CombatEvents.Killed, stops player input, and
    /// after a short pause reloads the active scene: death = full restart of the run, no checkpoints (D-034).
    /// Later the low-HP heartbeat (D-079) and inner-voice mood (D-030) can listen to Changed.
    /// </summary>
    public class PlayerHealth : MonoBehaviour, IDamageable
    {
        [SerializeField] PlayerTuning tuning;

        float current;

        /// <summary>(current HP, max HP).</summary>
        public event Action<float, float> Changed;
        /// <summary>The killing blow.</summary>
        public event Action<DamageInfo> Died;

        public float Current => current;
        public float Max => tuning != null ? Mathf.Max(1f, tuning.maxHealth) : 100f;
        public float Normalized => current / Max;
        public bool IsAlive => current > 0f;

        void Awake()
        {
            current = Max;
        }

        void Start()
        {
            Changed?.Invoke(current, Max); // HUD gets the starting value
        }

        public void TakeDamage(in DamageInfo damage)
        {
            if (!IsAlive || damage.Amount <= 0f) return;

            current = Mathf.Max(0f, current - damage.Amount);
            Changed?.Invoke(current, Max);
            if (current <= 0f) Die(damage);
        }

        /// <summary>Restore HP (medkits later). Does nothing once dead.</summary>
        public void Heal(float amount)
        {
            if (!IsAlive || amount <= 0f) return;
            current = Mathf.Min(Max, current + amount);
            Changed?.Invoke(current, Max);
        }

        void Die(in DamageInfo killingBlow)
        {
            Died?.Invoke(killingBlow);
            CombatEvents.RaiseKilled(gameObject, killingBlow);

            // Freeze the player during the short pause before the restart.
            var input = GetComponent<PlayerInputReader>();
            if (input != null) input.enabled = false;

            StartCoroutine(RestartRun());
        }

        IEnumerator RestartRun()
        {
            float delay = tuning != null ? tuning.deathRestartDelay : 1.5f;
            yield return new WaitForSeconds(delay);

            Scene scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
            else SceneManager.LoadScene(scene.name); // only works if the scene is in Build Settings
        }
    }
}
