using UnityEngine;
using TWR.Core;

namespace TWR.Puzzles
{
    /// <summary>
    /// The brain of one switch puzzle in a scene (D-124; D-019, D-105). It owns ALL puzzle state:
    /// which switches are on, sequence progress, started and solved. Switches only report hits here and
    /// show what they are told. Rules come from a PuzzleData asset: AllActive or Sequence, Latch or Toggle.
    /// On solve it tells every output (IPuzzleOutput, e.g. the exit door lock) and raises PuzzleEvents
    /// (D-126). State is never saved: reloading the scene or dying resets it (D-129).
    /// Sequence order = the order of the "switches" list.
    /// </summary>
    public class PuzzleController : MonoBehaviour
    {
        [SerializeField] PuzzleData data;
        [Tooltip("The switches of this puzzle. In Sequence mode this list IS the required order.")]
        [SerializeField] ShootableSwitch[] switches = new ShootableSwitch[0];
        [Tooltip("Optional trigger volume at the puzzle entrance. Entering it raises PuzzleEvents.Started once.")]
        [SerializeField] PuzzleAreaTrigger startArea;
        [Tooltip("Components that react to the solve. Each must implement IPuzzleOutput (e.g. DoorLock).")]
        [SerializeField] MonoBehaviour[] outputs = new MonoBehaviour[0];

        bool[] switchOn;
        IPuzzleOutput[] cachedOutputs;
        int sequenceProgress;
        bool started;
        bool solved;
        string puzzleId;

        public PuzzleData Data => data;
        public string PuzzleId => puzzleId;
        public bool IsStarted => started;
        public bool IsSolved => solved;

        void Awake()
        {
            if (data == null)
            {
                Debug.LogError($"[PuzzleController] '{name}' has no PuzzleData. Run TWR > Build Test Scene.", this);
                enabled = false;
                return;
            }
            puzzleId = string.IsNullOrEmpty(data.puzzleId) ? name : data.puzzleId;

            switchOn = new bool[switches.Length];
            for (int i = 0; i < switches.Length; i++)
            {
                if (switches[i] == null)
                {
                    Debug.LogError($"[PuzzleController] '{name}': switch slot {i} is empty.", this);
                    continue;
                }
                switches[i].Bind(this, i);
                switches[i].ShowState(false);
            }

            if (startArea != null) startArea.Bind(this);

            // Unity can't serialize interface fields, so outputs are MonoBehaviours cast once here.
            cachedOutputs = new IPuzzleOutput[outputs.Length];
            for (int i = 0; i < outputs.Length; i++)
            {
                cachedOutputs[i] = outputs[i] as IPuzzleOutput;
                if (outputs[i] != null && cachedOutputs[i] == null)
                    Debug.LogError($"[PuzzleController] '{name}': output '{outputs[i].name}' does not implement IPuzzleOutput.", this);
            }
        }

        /// <summary>Called by PuzzleAreaTrigger when the player walks into the puzzle area.</summary>
        public void NotifyPlayerEntered(GameObject player) => MarkStarted();

        /// <summary>True if hitting this switch right now could change anything.</summary>
        public bool CanPress(int index)
        {
            if (!enabled || solved || switchOn == null || index < 0 || index >= switchOn.Length) return false;
            return !(switchOn[index] && data.switchBehaviour == SwitchBehaviour.Latch);
        }

        /// <summary>
        /// A switch was triggered (shot, or F if the switch allows it). Applies the puzzle rules.
        /// Returns true if the puzzle state changed.
        /// </summary>
        public bool HandleSwitchTriggered(int index, GameObject activator)
        {
            if (!CanPress(index)) return false;

            // Shooting a switch from outside the area still counts as starting the puzzle,
            // so listeners always see Started before Solved.
            MarkStarted();

            if (data.solveMode == PuzzleSolveMode.Sequence)
                return HandleSequence(index, activator);
            return HandleAllActive(index, activator);
        }

        bool HandleAllActive(int index, GameObject activator)
        {
            SetSwitch(index, !switchOn[index], activator); // Latch only ever gets here while off (see CanPress)

            for (int i = 0; i < switchOn.Length; i++)
                if (!switchOn[i]) return true;
            Solve(activator);
            return true;
        }

        bool HandleSequence(int index, GameObject activator)
        {
            bool isNext = index == sequenceProgress && !switchOn[index];
            if (!isNext)
            {
                ResetAll(index);
                return true;
            }

            SetSwitch(index, true, activator);
            sequenceProgress++;
            if (sequenceProgress >= switchOn.Length) Solve(activator);
            return true;
        }

        void SetSwitch(int index, bool on, GameObject activator)
        {
            switchOn[index] = on;
            ShootableSwitch sw = switches[index];
            if (sw == null) return;

            sw.ShowState(on);
            NoiseSystem.Emit(sw.transform.position, data.switchNoiseRadius, NoiseType.Mechanism, activator);
            if (on) PuzzleEvents.RaiseSwitchActivated(puzzleId, sw.SwitchId, activator);
            else PuzzleEvents.RaiseSwitchDeactivated(puzzleId, sw.SwitchId, activator);
        }

        /// Wrong switch in Sequence mode: everything off, progress back to zero.
        void ResetAll(int wrongIndex)
        {
            for (int i = 0; i < switchOn.Length; i++)
            {
                switchOn[i] = false;
                if (switches[i] != null) switches[i].ShowState(false);
            }
            sequenceProgress = 0;

            ShootableSwitch wrong = switches[wrongIndex];
            Vector3 position = wrong != null ? wrong.transform.position : transform.position;
            NoiseSystem.Emit(position, data.resetNoiseRadius, NoiseType.Mechanism, null);
            PuzzleEvents.RaiseReset(puzzleId);
        }

        void MarkStarted()
        {
            if (started || !enabled) return;
            started = true;
            PuzzleEvents.RaiseStarted(puzzleId);
        }

        void Solve(GameObject solver)
        {
            if (solved) return;
            solved = true;
            PuzzleEvents.RaiseSolved(puzzleId, data.isOptional);
            for (int i = 0; i < cachedOutputs.Length; i++)
                cachedOutputs[i]?.OnPuzzleSolved(this, solver);
        }

        void OnValidate()
        {
            if (outputs == null) return;
            for (int i = 0; i < outputs.Length; i++)
                if (outputs[i] != null && !(outputs[i] is IPuzzleOutput))
                    Debug.LogWarning($"[PuzzleController] '{outputs[i].name}' ({outputs[i].GetType().Name}) is not an IPuzzleOutput and will be ignored.", this);
        }
    }
}
