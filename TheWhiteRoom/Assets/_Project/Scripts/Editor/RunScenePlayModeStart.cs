using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TWR.Run;

namespace TWR.EditorTools
{
    /// <summary>
    /// Editor helper for the run scenes (D-161): a section scene can't run on its own (no player, HUD or RunManager;
    /// those live in Core). So when you press Play while a run content scene (Assets/_Project/Scenes/Run, not Core)
    /// is open, Play starts from Core instead (EditorSceneManager.playModeStartScene), and the RunManager jumps the
    /// run straight to the stop that uses that scene (curated plan: every scene is in it). Opening any other scene
    /// (e.g. the Test_PlayerController sandbox) clears the override again, so nothing else changes.
    /// </summary>
    [InitializeOnLoad]
    static class RunScenePlayModeStart
    {
        const string RunScenesFolder = "Assets/_Project/Scenes/Run/";
        const string CorePath = RunScenesFolder + "Core.unity";

        static RunScenePlayModeStart()
        {
            EditorSceneManager.sceneOpened += (scene, mode) => Refresh(false);
            EditorSceneManager.activeSceneChangedInEditMode += (from, to) => Refresh(false);
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.ExitingEditMode) Refresh(true);
            };
            EditorApplication.delayCall += () => Refresh(false);
        }

        static void Refresh(bool aboutToPlay)
        {
            string requested = FindOpenRunContentScene(out bool coreOpen);
            var core = AssetDatabase.LoadAssetAtPath<SceneAsset>(CorePath);

            // Start from Core when a content scene is open without Core (Core open = it loads anyway).
            if (requested != null && !coreOpen && core != null)
                EditorSceneManager.playModeStartScene = core;
            else if (EditorSceneManager.playModeStartScene != null &&
                     AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == CorePath)
                EditorSceneManager.playModeStartScene = null; // only clear what we set ourselves

            if (!aboutToPlay) return;
            if (requested != null && core != null)
            {
                SessionState.SetString(RunManager.EditorStartSceneKey, requested);
                Debug.Log($"[TWR] Play: starting from Core and jumping the run to '{requested}' (D-161).");
            }
            else
            {
                SessionState.EraseString(RunManager.EditorStartSceneKey);
            }
        }

        /// The open run content scene to start in (the active one first), or null. coreOpen = Core is open too.
        static string FindOpenRunContentScene(out bool coreOpen)
        {
            coreOpen = false;
            string found = null;
            Scene active = SceneManager.GetActiveScene();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || string.IsNullOrEmpty(scene.path)) continue;
                if (scene.path == CorePath) { coreOpen = true; continue; }
                if (!scene.path.StartsWith(RunScenesFolder)) continue;
                if (found == null || scene == active) found = scene.name;
            }
            return found;
        }
    }
}
