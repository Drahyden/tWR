using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using TWR.Audio;
using TWR.Enemies;
using TWR.Items;
using TWR.Player;
using TWR.Portals;
using TWR.Puzzles;
using TWR.Run;
using TWR.UI;

namespace TWR.EditorTools
{
    /// <summary>
    /// Validation for the R2 run scenes (D-161 to D-171), used by "TWR/Run/Build Run Scenes" and
    /// "TWR/Run/Validate Run Setup". Checks: Core is first in Build Settings and every SectionData scene (and the lobby)
    /// is in it; Core holds the player, HUD-side pieces, RunManager + SectionLoader, PortalRenderManager, ambient bed,
    /// fader, event system and no content; each content scene has exactly one SectionEntry with a permanently locked
    /// door behind it, at least one SectionExit, a gate that matches its SectionData, the fork room both path exits,
    /// boss arenas a BossDefeated stub, portal pairs at least 200 m apart (D-144), and a baked NavMesh wherever Seekers
    /// spawn (stealth: at least 2 patrols, D-169). Opens each scene additively and closes it again. Logs with [TWR].
    /// </summary>
    public static partial class TestSceneBuilder
    {
        static bool ValidateRunScenes()
        {
            bool ok = true;
            var config = AssetDatabase.LoadAssetAtPath<RunConfig>(RunConfigPath);
            if (config == null)
            {
                Debug.LogError("[TWR] Run scenes: no RunConfig at " + RunConfigPath + ".");
                return false;
            }

            ok &= ValidateRunBuildSettings(config);

            // Which SectionData uses which scene.
            var sectionsByScene = new Dictionary<string, SectionData>();
            foreach (var (asset, _) in SectionScenes)
            {
                var section = AssetDatabase.LoadAssetAtPath<SectionData>(RunSectionsFolder + "/" + asset + ".asset");
                if (section != null && !string.IsNullOrEmpty(section.sceneName)) sectionsByScene[section.sceneName] = section;
            }

            ok &= ValidateRunScene(CoreSceneName, null);
            foreach (string sceneName in RunContentScenes)
            {
                sectionsByScene.TryGetValue(sceneName, out SectionData section);
                ok &= ValidateRunScene(sceneName, section);
            }
            return ok;
        }

        static bool ValidateRunBuildSettings(RunConfig config)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            var enabledNames = new HashSet<string>(scenes.Where(s => s.enabled).Select(s => Path.GetFileNameWithoutExtension(s.path)));
            bool coreFirst = scenes.Length > 0 && scenes[0].enabled && scenes[0].path == RunScenePath(CoreSceneName);
            bool ok = coreFirst;

            var report = new StringBuilder();
            report.AppendLine($"[TWR] Run Build Settings: Core first: {(coreFirst ? "yes" : "NO")} ({scenes.Length} scenes)");
            var required = new List<(string what, string scene)>
            {
                ("core scene", config.coreSceneName),
                ("lobby scene", config.lobbySceneName),
            };
            foreach (var (asset, _) in SectionScenes)
            {
                var section = AssetDatabase.LoadAssetAtPath<SectionData>(RunSectionsFolder + "/" + asset + ".asset");
                if (section != null) required.Add(("section " + section.DisplayId, section.sceneName));
            }
            foreach (var (what, scene) in required)
            {
                bool listed = !string.IsNullOrEmpty(scene) && enabledNames.Contains(scene);
                report.AppendLine($"  {what}: '{scene}' {(listed ? "in Build Settings" : "MISSING from Build Settings (or empty)")}");
                ok &= listed;
            }
            if (ok) Debug.Log(report.ToString());
            else Debug.LogError(report.ToString());
            return ok;
        }

        static bool ValidateRunScene(string sceneName, SectionData section)
        {
            string path = RunScenePath(sceneName);
            if (!File.Exists(path))
            {
                Debug.LogError($"[TWR] Run scene {path} does not exist. Run TWR > Run > Build Run Scenes.");
                return false;
            }

            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                openedHere = true;
            }
            try
            {
                return sceneName == CoreSceneName ? InspectCoreScene(scene) : InspectContentScene(scene, section);
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
        }

        static List<T> InScene<T>(Scene scene) where T : Component
        {
            var list = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects())
                list.AddRange(root.GetComponentsInChildren<T>(true));
            return list;
        }

        // ---------- Core ----------

        static bool InspectCoreScene(Scene scene)
        {
            var report = new StringBuilder();
            report.AppendLine($"[TWR] Run scene 'Core' (persistent, D-161)");
            bool ok = true;

            List<PlayerController> players = InScene<PlayerController>(scene);
            List<RunManager> managers = InScene<RunManager>(scene);
            List<SectionLoader> loaders = InScene<SectionLoader>(scene);
            List<PortalRenderManager> portalManagers = InScene<PortalRenderManager>(scene);
            int ambient = InScene<AmbientBed>(scene).Count;
            int faders = InScene<ScreenFader>(scene).Count;
            int eventSystems = InScene<EventSystem>(scene).Count;
            int globalVolumes = InScene<Volume>(scene).Count(v => v.isGlobal);
            int content = InScene<SectionEntry>(scene).Count + InScene<SectionExit>(scene).Count + InScene<NavMeshSurface>(scene).Count;

            ok &= Check(report, "player (PlayerController)", players.Count == 1, players.Count.ToString());
            ok &= Check(report, "RunManager with RunConfig", managers.Count == 1 && managers[0].Config != null,
                        managers.Count == 1 ? (managers[0].Config != null ? managers[0].Config.name : "no config") : managers.Count + " found");
            if (loaders.Count == 1)
            {
                var so = new SerializedObject(loaders[0]);
                Object player = so.FindProperty("player").objectReferenceValue;
                Object fader = so.FindProperty("fader").objectReferenceValue;
                bool sameObject = managers.Count == 1 && loaders[0].gameObject == managers[0].gameObject;
                ok &= Check(report, "SectionLoader (player, fader, on the RunManager)", player != null && fader != null && sameObject,
                            $"player {(player != null ? "OK" : "MISSING")}, fader {(fader != null ? "OK" : "MISSING")}, same object {(sameObject ? "yes" : "NO")}");
            }
            else ok &= Check(report, "SectionLoader", false, loaders.Count + " found");

            if (portalManagers.Count == 1)
            {
                Object cam = new SerializedObject(portalManagers[0]).FindProperty("playerCamera").objectReferenceValue;
                ok &= Check(report, "PortalRenderManager with the player camera", cam != null, cam != null ? cam.name : "MISSING");
            }
            else ok &= Check(report, "PortalRenderManager", false, portalManagers.Count + " found");

            ok &= Check(report, "ambient bed (keeps playing across loads, D-167)", ambient == 1, ambient.ToString());
            ok &= Check(report, "screen fader (D-167)", faders == 1, faders.ToString());
            ok &= Check(report, "event system", eventSystems == 1, eventSystems.ToString());
            ok &= Check(report, "global volume (Bloom, D-141)", globalVolumes >= 1, globalVolumes.ToString());
            ok &= Check(report, "no section content in Core", content == 0, content + " entries/exits/NavMesh surfaces");

            if (ok) Debug.Log(report.ToString());
            else Debug.LogError(report.ToString());
            return ok;
        }

        // ---------- content scenes ----------

        static bool InspectContentScene(Scene scene, SectionData section)
        {
            bool isLobby = scene.name == LobbySceneName;
            var report = new StringBuilder();
            report.AppendLine(section != null
                ? $"[TWR] Run scene '{scene.name}' ({section.DisplayId}, {section.type}, path {section.path}, exit condition {section.exitCondition})"
                : $"[TWR] Run scene '{scene.name}'{(isLobby ? " (hub lobby, D-163)" : " - NO SectionData uses this scene")}");
            bool ok = isLobby || section != null;

            List<SectionEntry> entries = InScene<SectionEntry>(scene);
            List<SectionExit> exits = InScene<SectionExit>(scene);
            List<SectionExitGate> gates = InScene<SectionExitGate>(scene);
            List<Portal> portals = InScene<Portal>(scene);
            List<SeekerSpawnPoint> spawns = InScene<SeekerSpawnPoint>(scene);
            List<NavMeshSurface> surfaces = InScene<NavMeshSurface>(scene);
            List<BossPlaceholder> bosses = InScene<BossPlaceholder>(scene);
            int coreOnly = InScene<PlayerController>(scene).Count + InScene<RunManager>(scene).Count + InScene<AmbientBed>(scene).Count
                           + InScene<PortalRenderManager>(scene).Count;

            ok &= Check(report, "exactly one SectionEntry", entries.Count == 1, entries.Count.ToString());
            if (entries.Count == 1) ok &= CheckEntryDoor(report, scene, entries[0]);
            ok &= Check(report, "at least one SectionExit", exits.Count >= 1,
                        string.Join(", ", exits.Select(e => $"'{e.ExitId}' (path {e.Path})")));
            ok &= Check(report, "no Core-only objects (player, RunManager, ambient bed, portal manager)", coreOnly == 0, coreOnly.ToString());

            SectionType type = section != null ? section.type : SectionType.Puzzle;
            if (!isLobby && type == SectionType.Fork)
            {
                bool escape = exits.Any(e => e.Path == RunPath.Escape), discover = exits.Any(e => e.Path == RunPath.Discover);
                ok &= Check(report, "fork: Escape and Discover exits (D-166)", escape && discover, $"Escape {(escape ? "yes" : "NO")}, Discover {(discover ? "yes" : "NO")}");
                int forkRooms = InScene<ForkRoom>(scene).Count;
                ok &= Check(report, "fork: ForkRoom controller", forkRooms == 1, forkRooms.ToString());
                int texts = InScene<LocalizedWorldText>(scene).Count;
                ok &= Check(report, "fork: localized EXIT / BASEMENT texts (D-150)", texts >= 2, texts.ToString());
            }
            else if (!isLobby)
            {
                ok &= Check(report, "exits have no path (only the fork room has)", exits.All(e => e.Path == RunPath.None), "");
                ok &= Check(report, "exit gate (D-165)", gates.Count >= 1,
                            string.Join(", ", gates.Select(g => $"'{g.name}' fallback {g.FallbackCondition}")));
            }

            if (section != null)
            {
                switch (section.exitCondition)
                {
                    case SectionExitCondition.MainPuzzlesSolved:
                        int mainPuzzles = InScene<PuzzleController>(scene).Count(p => p.Data != null && !p.Data.isOptional);
                        ok &= Check(report, "main-path puzzle for MainPuzzlesSolved", mainPuzzles >= 1, mainPuzzles.ToString());
                        break;
                    case SectionExitCondition.BossDefeated:
                        bool marked = bosses.Count >= 1 && bosses.All(b => b.GetComponent<BossMarker>() != null);
                        ok &= Check(report, "boss-defeated stub with BossMarker (D-158, D-165)", marked, bosses.Count.ToString());
                        break;
                }
                if (section.type == SectionType.Silent)
                    ok &= Check(report, "SilentSection (D-168)", InScene<SilentSection>(scene).Count == 1, "");
                if (section.type == SectionType.Stealth)
                {
                    int patrols = spawns.Count(s => s.PatrolRoute != null && s.PatrolRoute.Length >= 2);
                    ok &= Check(report, "stealth: at least 2 patrolling Seekers (D-169)", patrols >= 2, patrols.ToString());
                }
                if (section.type == SectionType.Combat || section.type == SectionType.Stealth || section.type == SectionType.Fork)
                {
                    int healthPickups = InScene<ItemPickup>(scene).Count(p => p.Item is HealthItemData);
                    if (healthPickups == 0) report.AppendLine("  WARNING: no health pickup here (D-171 places one in combat, stealth and fork).");
                    else report.AppendLine($"  health pickups (D-171): {healthPickups}");
                }
            }

            // Portals (D-144): linked inside this scene and far enough apart.
            foreach (Portal portal in portals)
            {
                Portal destination = portal.Destination;
                bool linked = destination != null && destination != portal && destination.gameObject.scene == scene;
                float distance = linked ? Vector3.Distance(portal.transform.position, destination.transform.position) : 0f;
                bool yawOnly = linked && Portal.DiffersOnlyByYaw(portal, destination, out _);
                ok &= Check(report, $"portal '{portal.PortalId}' -> '{(destination != null ? destination.PortalId : "none")}' >= {MinPortalSeparation} m apart, yaw-only",
                            linked && distance >= MinPortalSeparation && yawOnly, $"{distance:0.0} m");
            }

            // NavMesh wherever Seekers spawn (D-135, D-161).
            if (spawns.Count > 0)
            {
                bool baked = surfaces.Count > 0 && surfaces.All(s => s.navMeshData != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(s.navMeshData)));
                ok &= Check(report, $"baked + saved NavMesh for {spawns.Count} Seeker spawn point(s)", baked, surfaces.Count + " surface(s)");
                foreach (SeekerSpawnPoint spawn in spawns)
                {
                    bool onMesh = NavMesh.SamplePosition(spawn.transform.position, out _, 1f, NavMesh.AllAreas);
                    bool prefab = spawn.Prefab != null;
                    ok &= Check(report, $"spawn '{spawn.transform.parent.name}' on the NavMesh with a prefab", onMesh && prefab,
                                $"on mesh {(onMesh ? "yes" : "NO")}, prefab {(prefab ? "yes" : "NO")}, {spawn.PatrolRoute.Length} waypoints");
                }
            }
            else report.AppendLine($"  NavMesh: {surfaces.Count} surface(s), no Seekers here");

            if (ok) Debug.Log(report.ToString());
            else Debug.LogError(report.ToString());
            return ok;
        }

        /// One-way entry (D-165): a permanently locked door within 3 m BEHIND the entry spawn.
        static bool CheckEntryDoor(StringBuilder report, Scene scene, SectionEntry entry)
        {
            Transform t = entry.transform;
            bool found = false;
            foreach (DoorLock doorLock in InScene<DoorLock>(scene))
            {
                if (!doorLock.IsPermanentlyLocked && !new SerializedObject(doorLock).FindProperty("permanentlyLocked").boolValue) continue;
                Vector3 offset = doorLock.transform.position - t.position;
                offset.y = 0f;
                if (offset.magnitude <= 3f && Vector3.Dot(offset, t.forward) < 0f) { found = true; break; }
            }
            return Check(report, "entry spawn with a permanently locked door behind it (one-way)", found,
                         $"spawn at {t.position}, facing {t.forward}");
        }

        static bool Check(StringBuilder report, string what, bool passed, string detail)
        {
            report.AppendLine($"  {what}: {(passed ? "OK" : "PROBLEM")}{(string.IsNullOrEmpty(detail) ? "" : " - " + detail)}");
            return passed;
        }
    }
}
