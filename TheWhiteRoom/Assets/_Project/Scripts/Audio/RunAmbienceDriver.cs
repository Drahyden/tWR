using UnityEngine;
using TWR.Run;

namespace TWR.Audio
{
    /// <summary>
    /// Switches the ambient bed's distant office one-shots per section (D-223). Every section transition turns them off
    /// (so they never play over the white fade or into an ending scene); when a section starts they turn on if its
    /// SectionData has "officeAmbience" ticked, or for the lobby if RunConfig.lobbyOfficeAmbience is on. Silent rooms and
    /// boss arenas have the flag off (the builder enforces it); ending scenes never raise SectionStarted, so they stay off.
    /// Lives next to the AmbientBed in Core; event-driven, no per-frame work.
    /// </summary>
    [RequireComponent(typeof(AmbientBed))]
    public class RunAmbienceDriver : MonoBehaviour
    {
        [SerializeField] AmbientBed bed;
        [Tooltip("The RunManager in Core (same scene).")]
        [SerializeField] RunManager manager;

        SectionLoader loader;

        void OnEnable()
        {
            if (bed == null) bed = GetComponent<AmbientBed>();
            if (manager == null)
            {
                Debug.LogWarning("[Audio] RunAmbienceDriver has no RunManager: office one-shots stay as they are. Run TWR > Run > Build Run Scenes.", this);
                return;
            }
            manager.SectionStarted += HandleSectionStarted;
            loader = manager.GetComponent<SectionLoader>();
            if (loader != null) loader.TransitionStarted += HandleTransitionStarted;
        }

        void OnDisable()
        {
            if (manager != null) manager.SectionStarted -= HandleSectionStarted;
            if (loader != null) loader.TransitionStarted -= HandleTransitionStarted;
            loader = null;
        }

        void HandleTransitionStarted()
        {
            if (bed != null) bed.SetOfficeSounds(false);
        }

        void HandleSectionStarted(int stopIndex, SectionData section)
        {
            if (bed == null) return;
            bool lobbyOn = manager.Config == null || manager.Config.lobbyOfficeAmbience;
            bool on = !manager.IsInEnding && (section != null ? section.officeAmbience : lobbyOn);
            bed.SetOfficeSounds(on);
        }
    }
}
