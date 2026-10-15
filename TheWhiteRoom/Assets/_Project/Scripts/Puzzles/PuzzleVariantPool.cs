using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;

namespace TWR.Puzzles
{
    /// <summary>One rule set a seeded run may give a main-path puzzle (D-173), plus its family hint line (D-178).</summary>
    [Serializable]
    public class PuzzleVariant
    {
        public PuzzleSolveMode solveMode = PuzzleSolveMode.AllActive;
        public SwitchBehaviour switchBehaviour = SwitchBehaviour.Latch;
        [Tooltip("Inner-voice hint raised when the puzzle starts (D-056, D-178). Table 'Subtitles', e.g. 'hint.puzzle.allactive_latch'. No audio yet.")]
        public LocalizedString hint = new LocalizedString();

        /// <summary>Short name for logs and the debug overlay, e.g. "Sequence+Latch".</summary>
        public string Family => solveMode + "+" + switchBehaviour;
    }

    /// <summary>
    /// The variants a seeded run may roll for a main-path switch puzzle (R3, D-173 to D-175, D-178). D-173 pool:
    /// AllActive+Latch, Sequence+Latch, AllActive+Toggle. Sequence+Toggle is never allowed (too opaque without text).
    /// activeSwitchCount (3, D-130) switches are picked from the puzzle's authored slots; the rest are switched off
    /// completely. Seed 0 and the sandbox never use this pool's rolls: they keep the PuzzleData rules (AllActive+Latch)
    /// and the default slots; the hint for those rules is still looked up here. Also holds the Sequence clue timing
    /// (D-175). Create via: Project window > right-click > Create > TWR > Puzzle Variant Pool.
    /// </summary>
    [CreateAssetMenu(fileName = "PuzzleVariantPool_", menuName = "TWR/Puzzle Variant Pool")]
    public class PuzzleVariantPool : ScriptableObject
    {
        [Tooltip("Rule sets a seeded run picks from (D-173). Never Sequence+Toggle.")]
        public List<PuzzleVariant> variants = new List<PuzzleVariant>();
        [Tooltip("How many switches are active (D-130: the slice puzzle uses 3).")]
        [Min(1)] public int activeSwitchCount = 3;

        [Header("Sequence clue - D-175 (ordered light-up, no text)")]
        [Tooltip("Pause after the puzzle starts (or resets) before the first switch lights up.")]
        [Min(0f)] public float clueStartDelay = 0.8f;
        [Tooltip("How long each switch glows, in order.")]
        [Min(0.05f)] public float clueStepSeconds = 0.6f;
        [Tooltip("Dark pause between two switches.")]
        [Min(0f)] public float clueGapSeconds = 0.25f;
        [Tooltip("Colour of the clue glow (different from off-red and on-green).")]
        public Color clueColour = new Color(1f, 0.85f, 0.35f);

        public int Count => variants != null ? variants.Count : 0;

        public PuzzleVariant Get(int index) => variants != null && index >= 0 && index < variants.Count ? variants[index] : null;

        /// <summary>D-173: every combination except Sequence+Toggle.</summary>
        public static bool IsAllowed(PuzzleSolveMode mode, SwitchBehaviour behaviour) =>
            !(mode == PuzzleSolveMode.Sequence && behaviour == SwitchBehaviour.Toggle);

        /// <summary>The family hint for these rules (D-178), or null if the pool has none.</summary>
        public LocalizedString HintFor(PuzzleSolveMode mode, SwitchBehaviour behaviour)
        {
            if (variants == null) return null;
            for (int i = 0; i < variants.Count; i++)
            {
                PuzzleVariant v = variants[i];
                if (v != null && v.solveMode == mode && v.switchBehaviour == behaviour) return v.hint;
            }
            return null;
        }

        /// <summary>Editor/runtime check. Adds readable problems; true if none.</summary>
        public bool Validate(List<string> problems)
        {
            int before = problems.Count;
            if (variants == null || variants.Count == 0) problems.Add("variant pool is empty");
            else
                for (int i = 0; i < variants.Count; i++)
                {
                    PuzzleVariant v = variants[i];
                    if (v == null) { problems.Add($"variant {i} is empty"); continue; }
                    if (!IsAllowed(v.solveMode, v.switchBehaviour)) problems.Add($"variant {i} is Sequence+Toggle, which D-173 excludes");
                    if (v.hint == null || v.hint.IsEmpty) problems.Add($"variant {i} ({v.Family}) has no hint subtitle (D-178)");
                    for (int j = 0; j < i; j++)
                        if (variants[j] != null && variants[j].solveMode == v.solveMode && variants[j].switchBehaviour == v.switchBehaviour)
                            problems.Add($"variant {i} ({v.Family}) is listed twice");
                }
            if (activeSwitchCount != 3) problems.Add($"activeSwitchCount is {activeSwitchCount}; D-130/D-173 say 3");
            return problems.Count == before;
        }

        void OnValidate()
        {
            if (variants == null) return;
            for (int i = 0; i < variants.Count; i++)
                if (variants[i] != null && !IsAllowed(variants[i].solveMode, variants[i].switchBehaviour))
                    Debug.LogWarning($"[TWR] {name}: variant {i} is Sequence+Toggle, which D-173 excludes. It will be skipped.", this);
        }
    }
}
