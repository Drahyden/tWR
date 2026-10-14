using System;
using UnityEngine;
using TWR.Core;

namespace TWR.Puzzles
{
    /// <summary>
    /// The lock of a door (D-127), kept separate from the door's movement and from where it leads.
    /// It is a puzzle output (IPuzzleOutput): when the puzzle is solved it unlocks, emits a Mechanism noise
    /// (~8 m, D-128) and turns its lock light from red to green (D-065: light, not UI).
    /// The Door asks IsLocked before opening. State is scene-only; a reload locks it again (D-129).
    /// "Permanently locked (no puzzle)" mode (proposed D-145): the lobby's black entry door and the reserved
    /// path doors (D-021, D-060) stay locked whatever calls Unlock; F still shows "Locked" and raises the
    /// interaction event (D-112) through the Door as usual.
    /// </summary>
    public class DoorLock : MonoBehaviour, IPuzzleOutput
    {
        [SerializeField] DoorData data;
        [SerializeField] bool startsLocked = true;
        [Tooltip("Never unlocks, not even from a puzzle (proposed D-145). Forces startsLocked on.")]
        [SerializeField] bool permanentlyLocked;

        [Header("Lock light - D-065")]
        [Tooltip("Small emissive block that shows the lock state.")]
        [SerializeField] Renderer lockLight;
        [Tooltip("Optional point light that takes the same colour.")]
        [SerializeField] Light lockLamp;
        [SerializeField] Color lockedColour = new Color(1f, 0.08f, 0.05f);
        [SerializeField] Color unlockedColour = new Color(0.15f, 1f, 0.3f);
        [SerializeField] float emission = 2.5f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        MaterialPropertyBlock block;
        AudioSource audioSource;
        bool locked;

        public bool IsLocked => locked;
        public bool IsPermanentlyLocked => permanentlyLocked;

        /// <summary>Raised once when the lock opens.</summary>
        public event Action<DoorLock> Unlocked;

        void Awake()
        {
            locked = startsLocked || permanentlyLocked;
            audioSource = GetComponent<AudioSource>();
            ApplyVisual();
        }

        public void OnPuzzleSolved(PuzzleController puzzle, GameObject solver) => Unlock(solver);

        /// <summary>Unlock the door. source = who caused it (credited for the noise).</summary>
        public void Unlock(GameObject source)
        {
            if (!locked || permanentlyLocked) return;
            locked = false;
            ApplyVisual();
            if (data != null)
            {
                NoiseSystem.Emit(transform.position, data.unlockNoiseRadius, NoiseType.Mechanism, source);
                if (data.unlockClip != null && audioSource != null) audioSource.PlayOneShot(data.unlockClip);
            }
            Unlocked?.Invoke(this);
        }

        void ApplyVisual()
        {
            Color colour = locked ? lockedColour : unlockedColour;
            if (lockLight != null)
            {
                if (block == null) block = new MaterialPropertyBlock();
                lockLight.GetPropertyBlock(block);
                block.SetColor(BaseColorId, colour);
                block.SetColor(EmissionColorId, colour * emission);
                lockLight.SetPropertyBlock(block);
            }
            if (lockLamp != null) lockLamp.color = colour;
        }
    }
}
