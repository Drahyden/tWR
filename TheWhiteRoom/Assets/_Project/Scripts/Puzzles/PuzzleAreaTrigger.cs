using UnityEngine;

namespace TWR.Puzzles
{
    /// <summary>
    /// Trigger volume at a puzzle's entrance (D-126). When the player walks in, it tells its
    /// PuzzleController, which raises PuzzleEvents.Started once per scene load (D-080 "start of puzzle"
    /// voice hook). The controller links itself to this trigger, so only the controller needs a reference.
    /// Setup (done by TWR > Build Test Scene): trigger collider on Ignore Raycast + kinematic Rigidbody.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PuzzleAreaTrigger : MonoBehaviour
    {
        [Tooltip("Tag on the player's collider (the Player root).")]
        [SerializeField] string playerTag = "Player";

        PuzzleController controller;

        void Awake()
        {
            var trigger = GetComponent<Collider>();
            if (!trigger.isTrigger)
            {
                Debug.LogWarning($"[PuzzleAreaTrigger] '{name}': collider was not a trigger; fixing it.", this);
                trigger.isTrigger = true;
            }
        }

        /// <summary>Called by the owning PuzzleController.</summary>
        public void Bind(PuzzleController owner) => controller = owner;

        void OnTriggerEnter(Collider other)
        {
            if (controller == null || !other.CompareTag(playerTag)) return;
            controller.NotifyPlayerEntered(other.gameObject);
        }
    }
}
