using UnityEngine;
using UnityEngine.Localization;

namespace TWR.Interaction
{
    /// <summary>
    /// Anything the player can use with F (D-032, D-038). The prompt is a LocalizedString so all
    /// player-facing text goes through localization tables (D-084, D-091).
    /// The object's collider must be on the "Interactable" layer to be found by PlayerInteractor.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Localized prompt shown while looking at it (e.g. "Press F to interact").</summary>
        LocalizedString Prompt { get; }

        /// <summary>Stable ID for voice triggers and puzzles (D-080), e.g. "lobby.test_button".</summary>
        string InteractionId { get; }

        /// <summary>False hides the prompt and blocks interaction (e.g. a switch already used).</summary>
        bool CanInteract { get; }

        /// <summary>Do the thing. "interactor" is the player GameObject.</summary>
        void Interact(GameObject interactor);
    }
}
