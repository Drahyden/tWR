using UnityEngine;

namespace TWR.Audio
{
    /// <summary>
    /// Gives the AudioReverbZone on this object the values of its kind of space from the ReverbPresets asset (D-224),
    /// once when the scene loads, so tuning the asset changes every zone without rebuilding scenes. The zone's size
    /// (min/max distance) comes from the room and is set by the run-scene builder. Sound effects get this reverb; the 2D
    /// hum does not (reverbZoneMix 0, D-223). Both sides of a portal pair use the same space (D-224).
    /// </summary>
    [RequireComponent(typeof(AudioReverbZone))]
    public class ReverbZoneSetup : MonoBehaviour
    {
        [SerializeField] ReverbPresets presets;
        [SerializeField] ReverbSpace space = ReverbSpace.MediumRoom;

        public ReverbSpace Space => space;
        public ReverbPresets Presets => presets;

        void Awake() => Apply();

        /// <summary>Copy the preset into the zone (also called by the builder so the saved scene matches).</summary>
        public void Apply()
        {
            if (presets == null) return;
            ReverbPresets.Apply(GetComponent<AudioReverbZone>(), presets.Get(space));
        }

#if UNITY_EDITOR
        /// <summary>Editor/builder only: set the asset and the space.</summary>
        public void Configure(ReverbPresets presetAsset, ReverbSpace kind)
        {
            presets = presetAsset;
            space = kind;
            Apply();
        }
#endif
    }
}
