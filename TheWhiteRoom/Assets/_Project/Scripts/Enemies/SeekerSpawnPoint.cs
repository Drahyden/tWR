using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace TWR.Enemies
{
    /// <summary>
    /// Where a Seeker appears and which waypoints it patrols (D-043, D-135). At Start it creates the
    /// Seeker from its prefab on the nearest NavMesh point and hands it the patrol route. Spawning at runtime
    /// (instead of placing Seekers in the scene) is what lets seeded randomization (D-058, D-176) choose
    /// which spawn points are used. Nothing persists: a scene reload spawns a fresh Seeker.
    /// R3 (D-172, D-176): every point has a stable spotId (rolls sort by it, never by hierarchy order). isDefault = part
    /// of the authored layout used by seed 0 and the sandbox; allowInRolls = may be picked by a seeded roll (only points
    /// that pass the D-176 placement rules). The SectionRoller calls SetChosen before Start; if nothing does (sandbox,
    /// or a scene played outside a run), the point spawns exactly when isDefault is on, so the fallback is always the
    /// authored layout and never a double spawn.
    /// </summary>
    public class SeekerSpawnPoint : MonoBehaviour
    {
        enum Selection { NotRolled, Chosen, NotChosen }

        [SerializeField] Seeker seekerPrefab;
        [Tooltip("Waypoints in walking order. Empty = the Seeker guards the spawn point.")]
        [SerializeField] Transform[] patrolRoute = new Transform[0];
        [Tooltip("Off = something else calls Spawn() by hand.")]
        [SerializeField] bool spawnOnStart = true;

        [Header("Seeded placement - D-172, D-176")]
        [Tooltip("Stable id, unique in this scene, e.g. 'combat.enemy.nw'. Rolls sort candidates by it. Empty = object name.")]
        [SerializeField] string spotId = "";
        [Tooltip("Part of the authored layout: used by seed 0 (curated) and when no roll is applied (sandbox).")]
        [SerializeField] bool isDefault = true;
        [Tooltip("May be picked by a seeded roll. Only for points >= 15 m from the entry spawn and out of the entry vestibule's sight (D-176).")]
        [SerializeField] bool allowInRolls = true;

        Selection selection = Selection.NotRolled;

        public Seeker Prefab => seekerPrefab;
        public Transform[] PatrolRoute => patrolRoute;
        public Seeker SpawnedSeeker { get; private set; }
        public string SpotId => string.IsNullOrEmpty(spotId) ? name : spotId;
        public bool IsDefault => isDefault;
        public bool AllowInRolls => allowInRolls;
        /// <summary>True once Spawn() ran (a later SetChosen has no effect).</summary>
        public bool HasSpawned { get; private set; }
        /// <summary>Will this point spawn at Start? Rolled choice, or isDefault when nothing was rolled.</summary>
        public bool WillSpawn => selection == Selection.Chosen || (selection == Selection.NotRolled && isDefault);

        /// <summary>
        /// The section roll's decision (D-176). Must be called before Start (the RunManager does it from sceneLoaded).
        /// Returns false (and changes nothing) if the Seeker already spawned.
        /// </summary>
        public bool SetChosen(bool chosen)
        {
            if (HasSpawned) return false;
            selection = chosen ? Selection.Chosen : Selection.NotChosen;
            return true;
        }

        void Start()
        {
            if (spawnOnStart && WillSpawn) Spawn();
        }

        /// <summary>Create the Seeker here. Returns null (and logs) if there is no prefab.</summary>
        public Seeker Spawn()
        {
            if (seekerPrefab == null)
            {
                Debug.LogError($"[SeekerSpawnPoint] '{name}' has no Seeker prefab. Run TWR > Build Test Scene.", this);
                return null;
            }

            HasSpawned = true;
            Vector3 position = transform.position;
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                position = hit.position;
            else
                Debug.LogError($"[SeekerSpawnPoint] '{name}' is not on a NavMesh (is it baked?). The Seeker can't walk.", this);

            SpawnedSeeker = Instantiate(seekerPrefab, position, transform.rotation);
            SpawnedSeeker.name = seekerPrefab.name + " (" + SpotId + ")";
            // Instantiate puts new objects in the ACTIVE scene, which is Core during a run (D-161). Move the Seeker
            // into this spawn point's own section scene, so it is unloaded together with that section.
            if (SpawnedSeeker.gameObject.scene != gameObject.scene)
                SceneManager.MoveGameObjectToScene(SpawnedSeeker.gameObject, gameObject.scene);
            SpawnedSeeker.SetPatrolRoute(patrolRoute); // before the Seeker's Start, which begins the patrol
            return SpawnedSeeker;
        }

        void OnDrawGizmos()
        {
            // Default points: warm white; roll-only points: blue; curated-only points: orange.
            Gizmos.color = !allowInRolls ? new Color(1f, 0.6f, 0.2f) : isDefault ? new Color(0.9f, 0.8f, 0.7f) : new Color(0.4f, 0.6f, 1f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 1f, 0.4f);
            Gizmos.DrawLine(transform.position + Vector3.up * 1.6f, transform.position + Vector3.up * 1.6f + transform.forward * 0.8f);

            if (patrolRoute == null || patrolRoute.Length == 0) return;
            Gizmos.color = new Color(0.6f, 0.6f, 0.6f);
            for (int i = 0; i < patrolRoute.Length; i++)
            {
                Transform a = patrolRoute[i];
                Transform b = patrolRoute[(i + 1) % patrolRoute.Length];
                if (a == null || b == null) continue;
                Gizmos.DrawSphere(a.position + Vector3.up * 0.1f, 0.15f);
                Gizmos.DrawLine(a.position + Vector3.up * 0.1f, b.position + Vector3.up * 0.1f);
            }
        }
    }
}
