using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using TWR.Core;

namespace TWR.Puzzles
{
    /// <summary>
    /// The brain of one switch puzzle in a scene (D-124; D-019, D-105). It owns ALL puzzle state:
    /// which switches are on, sequence progress, started and solved. Switches only report hits here and
    /// show what they are told. Rules come from a PuzzleData asset: AllActive or Sequence, Latch or Toggle.
    /// On solve it tells every output (IPuzzleOutput, e.g. the exit door lock) and raises PuzzleEvents
    /// (D-126). State is never saved: reloading the scene or dying resets it (D-129).
    /// R3 (D-173 to D-178): "switches" are the puzzle's slots. Without a roll the slots marked defaultActive are used,
    /// in list order, with the PuzzleData rules (the sandbox and seed 0 look exactly as before). A seeded run calls
    /// ApplyVariant before the scene starts: rules from the variant pool, 3 active slots in a rolled order (= the
    /// Sequence order), the other slots switched off completely. Solved is final (D-174): afterwards every hit is
    /// ignored and the outputs stay unlocked. On Started it raises the variant family's hint as a voice trigger (D-178);
    /// Sequence puzzles show tally dots per slot and the PuzzleSequenceClue light-up (D-175).
    /// </summary>
    public class PuzzleController : MonoBehaviour
    {
        [SerializeField] PuzzleData data;
        [Tooltip("The switch slots of this puzzle. Without a roll: the defaultActive ones, and in Sequence mode their list order IS the required order.")]
        [SerializeField] ShootableSwitch[] switches = new ShootableSwitch[0];
        [Tooltip("Optional trigger volume at the puzzle entrance. Entering it raises PuzzleEvents.Started once.")]
        [SerializeField] PuzzleAreaTrigger startArea;
        [Tooltip("Components that react to the solve. Each must implement IPuzzleOutput (e.g. DoorLock).")]
        [SerializeField] MonoBehaviour[] outputs = new MonoBehaviour[0];

        [Header("Seeded variants - D-173, D-178 (empty = always the PuzzleData rules)")]
        [Tooltip("Rule sets a seeded run may roll, plus the family hint lines. Leave empty in the sandbox.")]
        [SerializeField] PuzzleVariantPool variantPool;
        [Tooltip("World-space box of the puzzle room (D-130, D-177): ammo for this puzzle must lie inside it. Size 0 = not set.")]
        [SerializeField] Bounds puzzleArea = new Bounds(Vector3.zero, Vector3.zero);

        readonly List<ShootableSwitch> active = new List<ShootableSwitch>(4);
        bool[] switchOn = new bool[0];
        IPuzzleOutput[] cachedOutputs;
        int sequenceProgress;
        bool started;
        bool solved;
        bool initialised;
        string puzzleId;
        PuzzleSolveMode mode;
        SwitchBehaviour behaviour;
        string variantLabel = "default";

        public PuzzleData Data => data;
        public string PuzzleId => puzzleId;
        /// <summary>Puzzle id that also works in edit mode (validation, determinism check).</summary>
        public string StableId => data != null && !string.IsNullOrEmpty(data.puzzleId) ? data.puzzleId : name;
        public bool IsStarted => started;
        public bool IsSolved => solved;
        public PuzzleVariantPool VariantPool => variantPool;
        public IReadOnlyList<ShootableSwitch> Slots => switches;
        /// <summary>The switches in use, in order (Sequence order).</summary>
        public IReadOnlyList<ShootableSwitch> ActiveSwitches => active;
        public PuzzleSolveMode SolveMode => mode;
        public SwitchBehaviour Behaviour => behaviour;
        /// <summary>"default" or the rolled variant's family, for logs.</summary>
        public string VariantLabel => variantLabel;
        public bool HasArea => puzzleArea.size.sqrMagnitude > 0.0001f;
        public Bounds Area => puzzleArea;

        /// <summary>This puzzle started (player entered, or first switch hit). For the sequence clue.</summary>
        public event Action<PuzzleController> StartedHere;
        /// <summary>A Sequence attempt failed and everything turned off. For the sequence clue (replay).</summary>
        public event Action<PuzzleController> ResetHere;

        void Awake()
        {
            if (data == null)
            {
                Debug.LogError($"[PuzzleController] '{name}' has no PuzzleData. Run TWR > Build Test Scene.", this);
                enabled = false;
                return;
            }
            puzzleId = string.IsNullOrEmpty(data.puzzleId) ? name : data.puzzleId;

            if (startArea != null) startArea.Bind(this);

            // Unity can't serialize interface fields, so outputs are MonoBehaviours cast once here.
            cachedOutputs = new IPuzzleOutput[outputs.Length];
            for (int i = 0; i < outputs.Length; i++)
            {
                cachedOutputs[i] = outputs[i] as IPuzzleOutput;
                if (outputs[i] != null && cachedOutputs[i] == null)
                    Debug.LogError($"[PuzzleController] '{name}': output '{outputs[i].name}' does not implement IPuzzleOutput.", this);
            }

            initialised = true;
            ApplyDefaultVariant(); // authored layout; a seeded run replaces it before Start (D-172)
        }

        // ---------- variants (D-173) ----------

        /// <summary>The authored layout: PuzzleData rules, the defaultActive slots in list order.</summary>
        public void ApplyDefaultVariant()
        {
            if (data == null) return;
            var defaults = new List<ShootableSwitch>(switches.Length);
            for (int i = 0; i < switches.Length; i++)
            {
                if (switches[i] == null)
                {
                    Debug.LogError($"[PuzzleController] '{name}': switch slot {i} is empty.", this);
                    continue;
                }
                if (switches[i].DefaultActive) defaults.Add(switches[i]);
            }
            Configure(data.solveMode, data.switchBehaviour, defaults, "default");
        }

        /// <summary>
        /// Use a rolled variant (D-173). activeInOrder = the switches to use, in Sequence order; every other slot is
        /// switched off. Only allowed before the puzzle started. Returns false (and changes nothing) otherwise.
        /// </summary>
        public bool ApplyVariant(PuzzleSolveMode solveMode, SwitchBehaviour switchBehaviour, IReadOnlyList<ShootableSwitch> activeInOrder)
        {
            if (!initialised || data == null) return false;
            if (started || solved)
            {
                Debug.LogError($"[PuzzleController] '{name}': a variant arrived after the puzzle started; keeping the current one.", this);
                return false;
            }
            if (!PuzzleVariantPool.IsAllowed(solveMode, switchBehaviour) || activeInOrder == null || activeInOrder.Count == 0)
            {
                Debug.LogError($"[PuzzleController] '{name}': variant {solveMode}+{switchBehaviour} with {activeInOrder?.Count ?? 0} switches is not allowed (D-173).", this);
                return false;
            }
            Configure(solveMode, switchBehaviour, activeInOrder, solveMode + "+" + switchBehaviour);
            return true;
        }

        void Configure(PuzzleSolveMode solveMode, SwitchBehaviour switchBehaviour, IReadOnlyList<ShootableSwitch> use, string label)
        {
            mode = solveMode;
            behaviour = switchBehaviour;
            variantLabel = label;
            active.Clear();
            for (int i = 0; i < use.Count; i++)
                if (use[i] != null && !active.Contains(use[i])) active.Add(use[i]);

            // Unused slots: unbound and hidden (D-173: not visible, not shootable). Used slots: bound to their place.
            for (int i = 0; i < switches.Length; i++)
            {
                ShootableSwitch sw = switches[i];
                if (sw == null) continue;
                int place = active.IndexOf(sw);
                sw.Bind(place >= 0 ? this : null, place);
                sw.SetSlotEnabled(place >= 0);
                sw.ShowState(false);
                sw.ShowOrderMark(place >= 0 && mode == PuzzleSolveMode.Sequence ? place + 1 : 0); // tally dots (D-175)
            }

            switchOn = new bool[active.Count];
            sequenceProgress = 0;
        }

        // ---------- play ----------

        /// <summary>Called by PuzzleAreaTrigger when the player walks into the puzzle area.</summary>
        public void NotifyPlayerEntered(GameObject player) => MarkStarted();

        /// <summary>True if hitting this switch right now could change anything. Never after the solve (D-174).</summary>
        public bool CanPress(int index)
        {
            if (!enabled || solved || index < 0 || index >= switchOn.Length) return false;
            return !(switchOn[index] && behaviour == SwitchBehaviour.Latch);
        }

        /// <summary>Is the active switch at this index on?</summary>
        public bool IsSwitchOn(int index) => index >= 0 && index < switchOn.Length && switchOn[index];

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

            if (mode == PuzzleSolveMode.Sequence)
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
            ShootableSwitch sw = active[index];
            if (sw == null) return;

            sw.ShowState(on);
            NoiseSystem.Emit(sw.transform.position, data.switchNoiseRadius, NoiseType.Mechanism, activator);
            if (on) PuzzleEvents.RaiseSwitchActivated(puzzleId, sw.SwitchId, activator);
            else PuzzleEvents.RaiseSwitchDeactivated(puzzleId, sw.SwitchId, activator);
        }

        /// Wrong switch in Sequence mode: everything off, progress back to zero (D-124; the noise makes trial and
        /// error risky, D-175).
        void ResetAll(int wrongIndex)
        {
            if (solved) return; // D-174: never undo a solve
            for (int i = 0; i < switchOn.Length; i++)
            {
                switchOn[i] = false;
                if (active[i] != null) active[i].ShowState(false);
            }
            sequenceProgress = 0;

            ShootableSwitch wrong = active[wrongIndex];
            Vector3 position = wrong != null ? wrong.transform.position : transform.position;
            NoiseSystem.Emit(position, data.resetNoiseRadius, NoiseType.Mechanism, null);
            PuzzleEvents.RaiseReset(puzzleId);
            ResetHere?.Invoke(this);
        }

        void MarkStarted()
        {
            if (started || !enabled) return;
            started = true;
            PuzzleEvents.RaiseStarted(puzzleId);

            // D-178: generic hint for this rule family (voice trigger, placeholder subtitle, no audio yet).
            LocalizedString hint = variantPool != null ? variantPool.HintFor(mode, behaviour) : null;
            if (hint != null && !hint.IsEmpty) VoiceEvents.RaisePuzzleHint(puzzleId, hint);

            StartedHere?.Invoke(this);
        }

        void Solve(GameObject solver)
        {
            if (solved) return;
            solved = true; // D-174: final. Switches freeze (CanPress is false from now on), outputs stay unlocked.
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

        void OnDrawGizmosSelected()
        {
            if (!HasArea) return;
            Gizmos.color = new Color(1f, 0.85f, 0.35f, 0.6f);
            Gizmos.DrawWireCube(puzzleArea.center, puzzleArea.size);
        }
    }
}
