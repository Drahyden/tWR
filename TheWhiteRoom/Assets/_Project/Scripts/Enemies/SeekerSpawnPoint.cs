using UnityEngine;
using UnityEngine.AI;

namespace TWR.Enemies
{
    /// <summary>
    /// Where a Seeker appears and which waypoints it patrols (D-043, D-135). At Start it creates the
    /// Seeker from its prefab on the nearest NavMesh point and hands it the patrol route. Spawning at runtime
    /// (instead of placing Seekers in the scene) is what later lets seeded randomization (D-058) choose
    /// which spawn points are used. Nothing persists: a scene reload spawns a fresh Seeker.
    /// </summary>
    public class SeekerSpawnPoint : MonoBehaviour
    {
        [SerializeField] Seeker seekerPrefab;
        [Tooltip("Waypoints in walking order. Empty = the Seeker guards the spawn point.")]
        [SerializeField] Transform[] patrolRoute = new Transform[0];
        [Tooltip("Off = something else (e.g. the D-058 randomizer) calls Spawn().")]
        [SerializeField] bool spawnOnStart = true;

        public Seeker Prefab => seekerPrefab;
        public Transform[] PatrolRoute => patrolRoute;
        public Seeker SpawnedSeeker { get; private set; }

        void Start()
        {
            if (spawnOnStart) Spawn();
        }

        /// <summary>Create the Seeker here. Returns null (and logs) if there is no prefab.</summary>
        public Seeker Spawn()
        {
            if (seekerPrefab == null)
            {
                Debug.LogError($"[SeekerSpawnPoint] '{name}' has no Seeker prefab. Run TWR > Build Test Scene.", this);
                return null;
            }

            Vector3 position = transform.position;
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                position = hit.position;
            else
                Debug.LogError($"[SeekerSpawnPoint] '{name}' is not on a NavMesh (is it baked?). The Seeker can't walk.", this);

            SpawnedSeeker = Instantiate(seekerPrefab, position, transform.rotation);
            SpawnedSeeker.name = seekerPrefab.name + " (" + name + ")";
            SpawnedSeeker.SetPatrolRoute(patrolRoute); // before the Seeker's Start, which begins the patrol
            return SpawnedSeeker;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.9f, 0.8f, 0.7f);
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
