using System;
using UnityEngine;

namespace TWR.Run
{
    /// <summary>
    /// Trigger volume just BEHIND a section's exit door (D-165): walking through the open door into it ends the section
    /// and the RunManager loads the next stop (D-167). It is deliberately separate from the door and its lock (D-127):
    /// the lock decides WHEN you can pass, this decides WHAT happens when you do. In the fork room each door has its
    /// own exit with a path (Escape / Discover): passing through commits the path (D-166). Fires once.
    /// Setup (done by TWR > Run > Build Run Scenes): trigger collider on Ignore Raycast + kinematic Rigidbody.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SectionExit : MonoBehaviour
    {
        [Tooltip("None for a normal exit. Fork room doors: Escape (EXIT sign) or Discover (BASEMENT door), D-166.")]
        [SerializeField] RunPath path = RunPath.None;
        [Tooltip("Stable id for logs and voice triggers (D-080).")]
        [SerializeField] string exitId = "exit";
        [Tooltip("Tag on the player's collider (the Player root).")]
        [SerializeField] string playerTag = "Player";

        bool used;

        public RunPath Path => path;
        public string ExitId => exitId;

        /// <summary>The player went through this exit and the transition started.</summary>
        public event Action<SectionExit> Entered;

        void Awake()
        {
            var trigger = GetComponent<Collider>();
            if (!trigger.isTrigger)
            {
                Debug.LogWarning($"[SectionExit] '{name}': collider was not a trigger; fixing it.", this);
                trigger.isTrigger = true;
            }
        }

        void OnTriggerEnter(Collider other)
        {
            if (used || !other.CompareTag(playerTag)) return;

            RunManager manager = RunManager.Instance;
            if (manager == null)
            {
                Debug.LogWarning($"[Run] Exit '{exitId}' reached, but there is no RunManager. Play from the Core scene (D-161).", this);
                return;
            }
            if (!manager.HandleSectionExit(this)) return; // e.g. a transition is already running

            used = true;
            Entered?.Invoke(this);
        }
    }
}
