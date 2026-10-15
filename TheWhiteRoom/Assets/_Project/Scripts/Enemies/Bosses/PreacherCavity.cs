using UnityEngine;

namespace TWR.Enemies
{
    /// <summary>
    /// A dark cavity the Preacher can teleport to (D-065 "dark cavities signal danger", D-183, D-184, D-187): a black
    /// recessed booth standing on a platform (or the floor). Only data: a stable id (the Preacher sorts cavities by it, so
    /// the boss stream always sees them in the same order, D-172/D-189) and the stand point where it appears, in front of
    /// the booth, facing out. No logic, no per-frame cost.
    /// </summary>
    [DisallowMultipleComponent]
    public class PreacherCavity : MonoBehaviour
    {
        [Tooltip("Stable id, unique in the arena, e.g. 'preacher.cavity.l1.sw'.")]
        [SerializeField] string cavityId = "";
        [Tooltip("Where the Preacher stands when it appears here (feet). Its forward = facing out of the cavity.")]
        [SerializeField] Transform standPoint;

        public string CavityId => string.IsNullOrEmpty(cavityId) ? name : cavityId;
        public Transform StandPoint => standPoint != null ? standPoint : transform;
        /// <summary>Feet position of the Preacher at this cavity.</summary>
        public Vector3 Position => StandPoint.position;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.6f, 0.1f, 0.8f);
            Vector3 p = Position;
            Gizmos.DrawWireCube(p + Vector3.up * 1.85f, new Vector3(1.4f, 3.7f, 1.2f));
            Gizmos.DrawLine(p + Vector3.up * 2.5f, p + Vector3.up * 2.5f + StandPoint.forward * 1.5f);
        }
    }
}
