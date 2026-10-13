using UnityEngine;

namespace TWR.Puzzles
{
    /// <summary>
    /// The tunable rules of one switch puzzle (D-124; D-019, D-036, D-105). The scene's
    /// PuzzleController reads this; the asset itself holds no state (state lives only in the scene,
    /// D-129). Separate assets = puzzle variants, so randomized layouts later (D-058) can swap
    /// the data without new code. Create via: Project window > right-click > Create > TWR > Puzzle Data.
    /// </summary>
    [CreateAssetMenu(fileName = "Puzzle_", menuName = "TWR/Puzzle Data")]
    public class PuzzleData : ScriptableObject
    {
        [Header("Identity - D-080, D-104")]
        [Tooltip("Stable ID sent with every PuzzleEvents call (voice triggers, run stats), e.g. \"test.switches\".")]
        public string puzzleId = "puzzle";
        [Tooltip("Optional puzzles count toward 'puzzle completeness' (D-104, D-029). Main-path puzzles: off.")]
        public bool isOptional = false;

        [Header("Rules - D-124")]
        [Tooltip("AllActive = turn every switch on, any order. Sequence = in the order listed on the PuzzleController; a wrong one resets all.")]
        public PuzzleSolveMode solveMode = PuzzleSolveMode.AllActive;
        [Tooltip("Latch = a switch stays on once hit. Toggle = every hit flips it.")]
        public SwitchBehaviour switchBehaviour = SwitchBehaviour.Latch;

        [Header("Noise radii (m) - D-128 (D-079, D-114)")]
        [Tooltip("Mechanism noise at a switch when it changes state. Credited to whoever hit it.")]
        [Min(0f)] public float switchNoiseRadius = 4f;
        [Tooltip("Mechanism noise at the wrong switch when a Sequence puzzle resets.")]
        [Min(0f)] public float resetNoiseRadius = 4f;
    }
}
