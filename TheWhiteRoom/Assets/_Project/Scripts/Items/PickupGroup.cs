using System.Collections.Generic;
using UnityEngine;

namespace TWR.Items
{
    /// <summary>
    /// A fixed set of walk-over pickups that a seeded roll may move between candidate spots (D-177, D-172), e.g.
    /// "the ammo box of the puzzle room" or "the health pickup of the combat room". Only POSITIONS are rolled: how many
    /// pickups there are and how much each gives never change. The SectionRoller picks pickups.Length spots and calls
    /// PlaceAt before anything can be collected; seed 0 and the sandbox leave the pickups where they were authored (on
    /// the default spots). puzzleAreaId = the puzzle whose area every spot must lie in (D-130: ammo for a gun-gated
    /// main puzzle stays inside its room, in front of the exit door); checked by the editor validation and at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public class PickupGroup : MonoBehaviour
    {
        [Tooltip("Stable id, unique in this scene, e.g. 'combat.health'.")]
        [SerializeField] string groupId = "";
        [Tooltip("The pickups this group places. Their number never changes (D-177).")]
        [SerializeField] ItemPickup[] pickups = new ItemPickup[0];
        [Tooltip("Where they may go. Needs at least as many spots as pickups; the default spots are their authored positions.")]
        [SerializeField] PickupSpot[] spots = new PickupSpot[0];
        [Tooltip("Optional: puzzle id whose area all spots must be inside (D-130, D-177). Empty = anywhere.")]
        [SerializeField] string puzzleAreaId = "";

        public string GroupId => string.IsNullOrEmpty(groupId) ? name : groupId;
        public IReadOnlyList<ItemPickup> Pickups => pickups;
        public IReadOnlyList<PickupSpot> Spots => spots;
        public string PuzzleAreaId => puzzleAreaId;

        /// <summary>Number of pickups that still exist (all of them at load time).</summary>
        public int PickupCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < pickups.Length; i++) if (pickups[i] != null) count++;
                return count;
            }
        }

        /// <summary>
        /// Move pickup i to chosen[i] (position and yaw). Called once per section load, before Start. Pickups beyond the
        /// chosen list stay where they are.
        /// </summary>
        public void PlaceAt(IReadOnlyList<PickupSpot> chosen)
        {
            int next = 0;
            for (int i = 0; i < pickups.Length && next < chosen.Count; i++)
            {
                if (pickups[i] == null) continue;
                PickupSpot spot = chosen[next++];
                if (spot == null) continue;
                Transform t = pickups[i].transform;
                t.SetPositionAndRotation(spot.transform.position, Quaternion.Euler(0f, spot.transform.eulerAngles.y, 0f));
            }
        }

        /// <summary>Remember the authored positions, so a failed roll can put everything back (D-130 fallback).</summary>
        public void CaptureAuthoredPositions()
        {
            if (authored != null) return;
            authored = new Pose[pickups.Length];
            for (int i = 0; i < pickups.Length; i++)
                if (pickups[i] != null) authored[i] = new Pose(pickups[i].transform.position, pickups[i].transform.rotation);
        }

        /// <summary>Put every pickup back on its authored (default) position.</summary>
        public void RestoreAuthoredPositions()
        {
            if (authored == null) return;
            for (int i = 0; i < pickups.Length; i++)
                if (pickups[i] != null) pickups[i].transform.SetPositionAndRotation(authored[i].position, authored[i].rotation);
        }

        Pose[] authored;

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 1f);
            for (int i = 0; i < spots.Length; i++)
                if (spots[i] != null) Gizmos.DrawLine(transform.position, spots[i].transform.position);
        }
    }
}
