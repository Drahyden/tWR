using System.Collections.Generic;
using UnityEngine;
using TWR.Enemies;
using TWR.Puzzles;

namespace TWR.Run
{
    /// <summary>
    /// Unlocks a section's exit door when the section's exit condition is met (D-165). The condition comes from the
    /// current SectionData (exitCondition), so it is data, not scene wiring; the serialized fallback is only used when
    /// the scene runs outside a run. None = unlocked quietly at load (combat, stealth, silent, impossible geometry;
    /// combat exits never lock on kills). MainPuzzlesSolved = every non-optional PuzzleController in this scene is
    /// solved (optional puzzles never gate, D-104). BossDefeated = the boss placeholder of this scene is beaten.
    /// Sits on the exit door; it only drives the DoorLock (D-127), the transition is SectionExit's job.
    /// </summary>
    public class SectionExitGate : MonoBehaviour
    {
        [SerializeField] DoorLock exitLock;
        [Tooltip("Used only when no run is active or the current section is another scene. The SectionData wins otherwise.")]
        [SerializeField] SectionExitCondition fallbackCondition = SectionExitCondition.None;

        readonly List<PuzzleController> mainPuzzles = new List<PuzzleController>(4);
        SectionExitCondition condition;
        bool open;

        public SectionExitCondition Condition => condition;
        public SectionExitCondition FallbackCondition => fallbackCondition;
        public bool IsOpen => open;

        void OnEnable()
        {
            PuzzleEvents.Solved += HandlePuzzleSolved;
            BossEvents.Defeated += HandleBossDefeated;
        }

        void OnDisable()
        {
            PuzzleEvents.Solved -= HandlePuzzleSolved;
            BossEvents.Defeated -= HandleBossDefeated;
        }

        void Start()
        {
            if (exitLock == null)
            {
                Debug.LogError($"[Run] Exit gate '{name}' has no DoorLock. Run TWR > Run > Build Run Scenes.", this);
                return;
            }

            condition = ResolveCondition(out string source);
            switch (condition)
            {
                case SectionExitCondition.MainPuzzlesSolved:
                    CollectMainPuzzles();
                    if (mainPuzzles.Count == 0)
                    {
                        Debug.LogWarning($"[Run] Exit gate '{name}': condition MainPuzzlesSolved ({source}) but no main-path puzzle in this scene. Opening.", this);
                        Open(true);
                    }
                    else
                    {
                        Debug.Log($"[Run] Exit locked until {mainPuzzles.Count} main puzzle(s) are solved ({source}, D-165).", this);
                        CheckPuzzles();
                    }
                    break;

                case SectionExitCondition.BossDefeated:
                    Debug.Log($"[Run] Exit locked until the boss is defeated ({source}, D-165).", this);
                    break;

                default:
                    Open(true); // D-165: exits without a condition are open from the start, quietly
                    break;
            }
        }

        SectionExitCondition ResolveCondition(out string source)
        {
            RunManager manager = RunManager.Instance;
            SectionData section = manager != null ? manager.CurrentSection : null;
            if (section != null && section.sceneName == gameObject.scene.name)
            {
                source = "SectionData '" + section.DisplayId + "'";
                return section.exitCondition;
            }
            source = "fallback";
            return fallbackCondition;
        }

        void CollectMainPuzzles()
        {
            mainPuzzles.Clear();
            // Once per section load, not per frame (D-100).
            foreach (PuzzleController puzzle in FindObjectsByType<PuzzleController>(FindObjectsSortMode.None))
                if (puzzle.gameObject.scene == gameObject.scene && puzzle.Data != null && !puzzle.Data.isOptional)
                    mainPuzzles.Add(puzzle);
        }

        void HandlePuzzleSolved(string puzzleId, bool isOptional)
        {
            if (open || isOptional || condition != SectionExitCondition.MainPuzzlesSolved) return;
            CheckPuzzles();
        }

        void CheckPuzzles()
        {
            for (int i = 0; i < mainPuzzles.Count; i++)
                if (mainPuzzles[i] != null && !mainPuzzles[i].IsSolved) return;
            Open(false);
        }

        void HandleBossDefeated(GameObject boss, string bossId)
        {
            if (open || condition != SectionExitCondition.BossDefeated) return;
            if (boss == null || boss.scene != gameObject.scene) return;
            Open(false);
        }

        void Open(bool quietly)
        {
            if (open) return;
            open = true;
            if (exitLock == null) return;
            if (quietly) exitLock.UnlockQuietly();
            else exitLock.Unlock(null); // audible: red -> green + Mechanism noise (D-127, D-128)
            Debug.Log($"[Run] Exit '{name}' unlocked ({condition}).", this);
        }
    }
}
