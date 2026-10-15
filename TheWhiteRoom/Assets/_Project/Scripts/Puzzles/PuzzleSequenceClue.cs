using System.Collections;
using UnityEngine;

namespace TWR.Puzzles
{
    /// <summary>
    /// In-world order clue for Sequence puzzles (D-175): when the puzzle starts, and again after every wrong-order
    /// reset, the active switches glow one after another in the required order (amber, distinct from off-red and
    /// on-green; no text). Together with the tally dots under each switch (PuzzleController/ShootableSwitch) the order
    /// is readable without words, and trial and error still works through the D-124 reset and its noise. Does nothing
    /// for AllActive puzzles. Timing and colour come from the PuzzleVariantPool (defaults if none).
    /// Sits next to the PuzzleController. Runs as a short coroutine, never per frame.
    /// </summary>
    [RequireComponent(typeof(PuzzleController))]
    public class PuzzleSequenceClue : MonoBehaviour
    {
        PuzzleController controller;
        Coroutine running;
        WaitForSeconds startWait, stepWait, gapWait;
        Color colour = new Color(1f, 0.85f, 0.35f);

        void Awake()
        {
            controller = GetComponent<PuzzleController>();
            PuzzleVariantPool pool = controller.VariantPool;
            // Cached once: no allocation when the clue plays.
            startWait = new WaitForSeconds(pool != null ? pool.clueStartDelay : 0.8f);
            stepWait = new WaitForSeconds(pool != null ? pool.clueStepSeconds : 0.6f);
            gapWait = new WaitForSeconds(pool != null ? pool.clueGapSeconds : 0.25f);
            if (pool != null) colour = pool.clueColour;
        }

        void OnEnable()
        {
            controller.StartedHere += HandleCue;
            controller.ResetHere += HandleCue;
        }

        void OnDisable()
        {
            controller.StartedHere -= HandleCue;
            controller.ResetHere -= HandleCue;
            StopClue();
        }

        void HandleCue(PuzzleController puzzle)
        {
            if (puzzle.SolveMode != PuzzleSolveMode.Sequence || puzzle.IsSolved) return;
            StopClue();
            running = StartCoroutine(PlayClue());
        }

        IEnumerator PlayClue()
        {
            yield return startWait;
            var switches = controller.ActiveSwitches;
            for (int i = 0; i < switches.Count; i++)
            {
                if (controller.IsSolved) break;
                ShootableSwitch sw = switches[i];
                if (sw == null) continue;
                sw.SetClueGlow(true, colour);
                yield return stepWait;
                sw.SetClueGlow(false, colour);
                yield return gapWait;
            }
            running = null;
        }

        void StopClue()
        {
            if (running != null) StopCoroutine(running);
            running = null;
            if (controller == null) return;
            var switches = controller.ActiveSwitches;
            for (int i = 0; i < switches.Count; i++)
                if (switches[i] != null) switches[i].SetClueGlow(false, colour);
        }
    }
}
