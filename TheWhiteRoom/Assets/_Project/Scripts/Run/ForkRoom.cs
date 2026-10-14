using UnityEngine;
using TWR.Puzzles;

namespace TWR.Run
{
    /// <summary>
    /// The fork room's two doors (D-149, D-150, D-166): the Escape door with the EXIT sign and the black Discover
    /// door with the BASEMENT plate. Both are ordinary unlocked doors: opening one commits nothing. The path is
    /// committed by the RunManager (ChoosePath) when the player walks through a door into its SectionExit; this
    /// component then closes and permanently locks the other door. No per-frame work.
    /// </summary>
    public class ForkRoom : MonoBehaviour
    {
        [Header("Escape (EXIT sign)")]
        [SerializeField] SectionExit escapeExit;
        [SerializeField] Door escapeDoor;
        [SerializeField] DoorLock escapeLock;

        [Header("Discover (black BASEMENT door)")]
        [SerializeField] SectionExit discoverExit;
        [SerializeField] Door discoverDoor;
        [SerializeField] DoorLock discoverLock;

        void OnEnable()
        {
            if (escapeExit != null) escapeExit.Entered += HandleExitEntered;
            if (discoverExit != null) discoverExit.Entered += HandleExitEntered;
        }

        void OnDisable()
        {
            if (escapeExit != null) escapeExit.Entered -= HandleExitEntered;
            if (discoverExit != null) discoverExit.Entered -= HandleExitEntered;
        }

        void HandleExitEntered(SectionExit exit)
        {
            bool tookEscape = exit == escapeExit;
            Door otherDoor = tookEscape ? discoverDoor : escapeDoor;
            DoorLock otherLock = tookEscape ? discoverLock : escapeLock;
            if (otherDoor != null) otherDoor.CloseInstantly();
            if (otherLock != null) otherLock.LockPermanently();
            Debug.Log($"[Run] Fork: went through the {(tookEscape ? "EXIT (Escape)" : "BASEMENT (Discover)")} door; the other door is now locked (D-166).", this);
        }
    }
}
