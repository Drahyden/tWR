using UnityEngine;

namespace TWR.Items
{
    /// <summary>
    /// A place where a walk-over pickup may be put by a seeded roll (D-177, D-172). Only a marker with a stable id
    /// (rolls sort candidates by it, never by hierarchy order) and an isDefault flag: default spots are where the
    /// pickups stand in the authored layout (seed 0 and the sandbox). The PickupGroup that lists this spot moves its
    /// pickups here. No logic, no per-frame cost.
    /// </summary>
    [DisallowMultipleComponent]
    public class PickupSpot : MonoBehaviour
    {
        [Tooltip("Stable id, unique in this scene, e.g. 'puzzle.ammo.ne'. Empty = object name.")]
        [SerializeField] string spotId = "";
        [Tooltip("A pickup stands here in the authored layout (seed 0 / sandbox).")]
        [SerializeField] bool isDefault;

        public string SpotId => string.IsNullOrEmpty(spotId) ? name : spotId;
        public bool IsDefault => isDefault;

        void OnDrawGizmos()
        {
            Gizmos.color = isDefault ? new Color(0.4f, 1f, 0.4f) : new Color(0.4f, 0.8f, 1f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * 0.15f, new Vector3(0.4f, 0.3f, 0.4f));
        }
    }
}
