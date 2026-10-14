using UnityEngine;
using UnityEngine.Localization;
using TWR.Core;

namespace TWR.Run
{
    /// <summary>
    /// Inner-voice hook for a fork door (D-166, D-150, D-080): the first time the player walks into the area in front
    /// of the door, it raises VoiceEvents.ForkDoorSeen with a placeholder localized subtitle key (D-084). "Seen" is a
    /// trigger volume for now; the shared is-observed check (D-045, D-053) can replace it later. No audio yet.
    /// Setup (done by TWR > Run > Build Run Scenes): trigger collider on Ignore Raycast + kinematic Rigidbody.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ForkDoorSeenTrigger : MonoBehaviour
    {
        [Tooltip("Stable id, e.g. 'fork.exit' or 'fork.basement'.")]
        [SerializeField] string doorId = "fork.door";
        [Tooltip("Placeholder subtitle in table 'Subtitles' (D-089). Shown once subtitles exist.")]
        [SerializeField] LocalizedString subtitle = new LocalizedString();
        [SerializeField] string playerTag = "Player";

        bool fired;

        public string DoorId => doorId;
        public LocalizedString Subtitle => subtitle;

        /// <summary>Set id and subtitle (used by the scene builder).</summary>
        public void Configure(string id, LocalizedString subtitleKey)
        {
            doorId = id;
            subtitle = subtitleKey;
        }

        void Awake()
        {
            var trigger = GetComponent<Collider>();
            if (!trigger.isTrigger) trigger.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (fired || !other.CompareTag(playerTag)) return;
            fired = true;
            VoiceEvents.RaiseForkDoorSeen(doorId, subtitle);
        }
    }
}
