#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.InputSystem;

namespace TWR.Audio
{
    /// <summary>
    /// Minimal F1 music-only debug view (D-235) for scenes without the RunDebugOverlay (the MainMenu, or a test scene with
    /// no RunManager). Added to the MusicDirector in the editor and development builds only; text not localized (D-159).
    /// It stays completely passive while a RunDebugOverlay exists (MusicDebugPanel.HasHost), so F1 is never handled
    /// twice: there the Music section sits inside the run overlay. Fixed key via Keyboard.current, no rebinding (D-038).
    /// While open it frees the cursor and gives the old cursor state back when closed.
    /// </summary>
    [DisallowMultipleComponent]
    public class MusicDebugOverlay : MonoBehaviour
    {
        bool open;
        GUIStyle rich;
        Vector2 scroll;
        CursorLockMode previousLock;
        bool previousVisible;

        void Update()
        {
            if (MusicDebugPanel.HasHost)
            {
                if (open) SetOpen(false); // the run overlay took over
                return;
            }
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame) SetOpen(!open);
        }

        void SetOpen(bool value)
        {
            if (open == value) return;
            open = value;
            if (open)
            {
                previousLock = Cursor.lockState;
                previousVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = previousLock;
                Cursor.visible = previousVisible;
            }
        }

        void OnGUI()
        {
            if (!open || MusicDebugPanel.HasHost) return;
            if (rich == null) rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            GUILayout.BeginArea(new Rect(10f, 10f, 520f, Mathf.Min(Screen.height - 20f, 520f)), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("<b>MUSIC DEBUG</b>   (F1 close; the full run menu appears in Core)", rich);
            MusicDebugPanel.Draw(rich);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void OnDisable() => SetOpen(false);
    }
}
#endif
