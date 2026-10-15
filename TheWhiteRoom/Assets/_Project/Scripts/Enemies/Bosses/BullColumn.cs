using UnityEngine;
using UnityEngine.AI;

namespace TWR.Enemies
{
    /// <summary>
    /// A column in the Bull arena (D-180, D-181, D-190). The Bull is stunned whenever its charge ends in a column (or a
    /// wall). Unbreakable columns never change. Breakable ones look cracked from the start and break on their
    /// hitsToBreak-th charge hit (BullData, D-190: the 2nd): the solid column and its collider disappear, low rubble
    /// remains (no collider, so it never stuns), and the carving NavMeshObstacle is switched off so the Bull can walk
    /// through the gap. "Risk rises over the fight" (D-181): fewer columns to stun it against.
    /// Breakable columns are left out of the NavMesh bake (NavMeshModifier) and carve with their obstacle instead.
    /// </summary>
    public class BullColumn : MonoBehaviour
    {
        [Tooltip("Stable id for logs and validation, e.g. 'bull.column.07'.")]
        [SerializeField] string columnId = "";
        [SerializeField] bool breakable;
        [Tooltip("The standing column (renderer + collider). Hidden when it breaks.")]
        [SerializeField] GameObject solid;
        [Tooltip("Rubble shown after breaking (no colliders).")]
        [SerializeField] GameObject rubble;
        [Tooltip("Crack marks shown more strongly after the first hit.")]
        [SerializeField] GameObject extraCracks;
        [Tooltip("Carving obstacle of a breakable column (switched off when it breaks).")]
        [SerializeField] NavMeshObstacle obstacle;

        int hits;

        public string ColumnId => string.IsNullOrEmpty(columnId) ? name : columnId;
        public bool Breakable => breakable;
        public bool IsBroken { get; private set; }
        public int Hits => hits;

        void Awake()
        {
            if (rubble != null) rubble.SetActive(false);
            if (extraCracks != null) extraCracks.SetActive(false);
        }

        /// <summary>A charge ended in this column. Returns true if this hit broke it.</summary>
        public bool RegisterChargeHit(int hitsToBreak)
        {
            if (IsBroken) return false;
            hits++;
            if (!breakable) return false;
            if (hits < Mathf.Max(1, hitsToBreak))
            {
                if (extraCracks != null) extraCracks.SetActive(true);
                return false;
            }
            Break();
            return true;
        }

        void Break()
        {
            IsBroken = true;
            if (solid != null) solid.SetActive(false);
            if (extraCracks != null) extraCracks.SetActive(false);
            if (rubble != null) rubble.SetActive(true);
            if (obstacle != null) obstacle.enabled = false;
            Debug.Log($"[Boss] Column '{ColumnId}' broke (D-190).", this);
        }
    }
}
