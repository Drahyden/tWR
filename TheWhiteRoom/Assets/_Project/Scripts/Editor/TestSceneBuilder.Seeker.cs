using System.Linq;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using TWR.Core;
using TWR.Enemies;
using TWR.Stats;

namespace TWR.EditorTools
{
    /// <summary>
    /// Step 4 (Seeker, D-107; D-043) part of "TWR/Build Test Scene". Creates the NoiseSettings asset in a
    /// Resources folder (D-131 wall muffling), the SeekerData and SeekerSoundSet assets, and rebuilds the
    /// Seeker prefab (pale capsule with a black eye and a state-colour band, D-067; D-132 confirmed, D-133 to D-138).
    /// In the scene it adds a Seeker spawn point with a 4-waypoint patrol in the puzzle room, two extra noise
    /// test listeners (puzzle room + behind the exit door) and two floor marks for the muffling test, RunStats
    /// (D-137), and bakes + SAVES the NavMesh (D-135) next to the scene. Validation logs what
    /// every spawn point is wired to and errors if the NavMesh is missing, unsaved or doesn't join both rooms.
    /// Like steps 2 and 3, every asset is loaded/created AFTER EditorSceneManager.NewScene (see BuildTestSceneAfterSetup).
    /// </summary>
    public static partial class TestSceneBuilder
    {
        const string SeekerDataPath = Root + "/Data/Enemies/Seeker_Default.asset";
        const string SeekerSoundSetPath = Root + "/Data/Enemies/SeekerSounds_Placeholder.asset";
        const string SeekerPrefabPath = Root + "/Prefabs/Seeker.prefab";
        const string NoiseSettingsPath = Root + "/Data/Resources/NoiseSettings.asset";
        const string NavMeshFolder = Root + "/Scenes/Test_PlayerController";
        const string NavMeshAssetPath = NavMeshFolder + "/NavMesh.asset";
        const string PlayerLayerName = GameLayers.Player;
        const string EnemyLayerName = GameLayers.Enemy;

        // Spots used by the validation: one in each room (feet height).
        static readonly Vector3 WhiteRoomProbe = new Vector3(0f, 0f, -6f);
        static readonly Vector3 PuzzleRoomProbe = new Vector3(16f, 0f, 0f);

        struct SeekerAssets
        {
            public SeekerData data;
            public SeekerSoundSet sounds;
            public Seeker prefab;
            public int enemyLayer;
        }

        // ---------- assets ----------

        /// Default + Interactable: what counts as a wall for noise muffling and Seeker sight.
        static int ObstacleLayersMask()
        {
            int mask = 1 << 0; // Default
            int interactable = LayerMask.NameToLayer(InteractableLayerName);
            if (interactable >= 0) mask |= 1 << interactable;
            return mask;
        }

        /// The one NoiseSettings asset, in a Resources folder so NoiseSystem finds it by itself (D-131).
        /// Also called from PrepareSharedAssets, so TWR > Setup Project creates it too.
        internal static NoiseSettings EnsureNoiseSettings()
        {
            EnsureFolder(Root + "/Data/Resources");
            NoiseSettings settings = LoadOrCreate<NoiseSettings>(NoiseSettingsPath, s => s.occluderMask = ObstacleLayersMask());
            if (settings == null) return null;

            // Repair: the mask must contain the obstacle layers and none of the body/helper layers.
            int wanted = ObstacleLayersMask();
            int current = settings.occluderMask;
            int cleaned = current & ~ActorLayersMask();
            if (cleaned == 0) cleaned = wanted;
            if (cleaned != current)
            {
                settings.occluderMask = cleaned;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                Debug.Log("[TWR] Repaired NoiseSettings occluder mask (Default + Interactable, no Player/Enemy/Weapon/Ignore Raycast).");
            }
            return settings;
        }

        /// Player + Enemy + Weapon + Ignore Raycast, from the CURRENT layer names (editor-time, not cached).
        static int ActorLayersMask()
        {
            int mask = 0;
            foreach (string layerName in new[] { PlayerLayerName, EnemyLayerName, WeaponLayerName, IgnoreRaycastLayerName })
            {
                int layer = LayerMask.NameToLayer(layerName);
                if (layer >= 0) mask |= 1 << layer;
            }
            return mask;
        }

        static bool PrepareSeekerAssets(out SeekerAssets assets)
        {
            assets = default;
            assets.enemyLayer = LayerMask.NameToLayer(EnemyLayerName);
            if (assets.enemyLayer < 0 || LayerMask.NameToLayer(PlayerLayerName) < 0)
            {
                Debug.LogError("[TWR] 'Enemy' or 'Player' layer missing. Run TWR > Setup Project.");
                return false;
            }

            EnsureFolder(Root + "/Data/Enemies");
            if (EnsureNoiseSettings() == null)
            {
                Debug.LogError("[TWR] Could not create " + NoiseSettingsPath);
                return false;
            }

            assets.data = LoadOrCreate<SeekerData>(SeekerDataPath, d => d.sightBlockers = ObstacleLayersMask());
            assets.sounds = LoadOrCreate<SeekerSoundSet>(SeekerSoundSetPath, null);
            if (assets.data == null || assets.sounds == null)
            {
                Debug.LogError("[TWR] Could not create " + SeekerDataPath + " / " + SeekerSoundSetPath);
                return false;
            }

            // Repair an empty or wrong sight mask on older copies.
            int sight = assets.data.sightBlockers & ~ActorLayersMask();
            if (sight == 0) sight = ObstacleLayersMask();
            if (sight != assets.data.sightBlockers)
            {
                assets.data.sightBlockers = sight;
                EditorUtility.SetDirty(assets.data);
                AssetDatabase.SaveAssets();
            }

            assets.prefab = BuildSeekerPrefab(assets.data, assets.sounds, assets.enemyLayer);
            if (assets.prefab == null)
            {
                Debug.LogError("[TWR] Could not build " + SeekerPrefabPath);
                return false;
            }
            return true;
        }

        /// Rebuilt from scratch on every scene build (keeps its GUID), so code changes always reach the prefab.
        static Seeker BuildSeekerPrefab(SeekerData data, SeekerSoundSet sounds, int enemyLayer)
        {
            Material skin = GetOrCreateMaterial("M_SeekerSkin", new Color(0.93f, 0.86f, 0.78f), Color.black); // D-067 fleshy beige-white
            Material eyeMat = GetOrCreateMaterial("M_SeekerEye", new Color(0.01f, 0.01f, 0.01f), Color.black);  // pitch-black marble eye
            eyeMat.SetFloat("_Smoothness", 0.92f);
            EditorUtility.SetDirty(eyeMat);
            Material band = GetOrCreateMaterial("M_SeekerBand", Color.white, Color.white * 1.5f); // emissive: state colour

            var root = new GameObject("Seeker");
            try
            {
                var agent = root.AddComponent<NavMeshAgent>();
                agent.agentTypeID = 0; // Humanoid, baked with radius 0.4 / height 2 (D-135)
                agent.radius = data.agentRadius;
                agent.height = data.agentHeight;
                agent.baseOffset = 0f;
                agent.speed = data.patrolSpeed;
                agent.angularSpeed = data.angularSpeed;
                agent.acceleration = data.acceleration;
                agent.stoppingDistance = 0.1f;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

                var capsule = root.AddComponent<CapsuleCollider>(); // what bullets hit (Enemy layer is shootable)
                capsule.radius = data.agentRadius;
                capsule.height = data.agentHeight;
                capsule.center = new Vector3(0f, data.agentHeight * 0.5f, 0f);
                var body = root.AddComponent<Rigidbody>(); // kinematic: a moving collider that doesn't get pushed
                body.isKinematic = true;
                body.useGravity = false;

                var audio = root.AddComponent<AudioSource>();
                audio.playOnAwake = false;
                audio.spatialBlend = 1f; // 3D (D-138)
                audio.rolloffMode = AudioRolloffMode.Linear;
                audio.minDistance = 1f;
                audio.maxDistance = 25f;
                audio.dopplerLevel = 0f;

                // Visual body, pivot at the feet so it can lean (telegraph) and tip over (death).
                Transform visual = new GameObject("Body").transform;
                visual.SetParent(root.transform, false);
                Renderer torso = SeekerPart("Torso", PrimitiveType.Capsule, visual, new Vector3(0f, 1f, 0f), new Vector3(0.8f, 1f, 0.8f), skin);
                SeekerPart("Eye (black)", PrimitiveType.Sphere, visual, new Vector3(0f, 1.62f, 0.33f), new Vector3(0.26f, 0.15f, 0.12f), eyeMat);
                Renderer stateBand = SeekerPart("State Band", PrimitiveType.Cylinder, visual, new Vector3(0f, 1.38f, 0f), new Vector3(0.86f, 0.035f, 0.86f), band);

                Transform eyePoint = new GameObject("Eye Point").transform;
                eyePoint.SetParent(root.transform, false);
                eyePoint.localPosition = new Vector3(0f, data.eyeHeight, 0.25f);

                var senses = root.AddComponent<SeekerSenses>();
                var health = root.AddComponent<SeekerHealth>();
                var visuals = root.AddComponent<SeekerVisuals>();
                var seekerAudio = root.AddComponent<SeekerAudio>();
                var melee = root.AddComponent<SeekerMelee>();
                var death = root.AddComponent<SeekerDeath>();
                var seeker = root.AddComponent<Seeker>();

                Assign(senses, "data", data); Assign(senses, "eye", eyePoint);
                Assign(health, "data", data);
                Assign(visuals, "body", visual); Assign(visuals, "skinRenderer", torso); Assign(visuals, "stateBand", stateBand);
                Assign(seekerAudio, "sounds", sounds); Assign(seekerAudio, "data", data); Assign(seekerAudio, "agent", agent);
                Assign(melee, "data", data); Assign(melee, "visuals", visuals); Assign(melee, "sounds", seekerAudio);
                Assign(death, "data", data); Assign(death, "health", health); Assign(death, "visuals", visuals); Assign(death, "sounds", seekerAudio);
                AssignArray(death, "disableOnDeath", new Object[] { seeker, senses, melee });
                Assign(seeker, "data", data); Assign(seeker, "senses", senses); Assign(seeker, "health", health);
                Assign(seeker, "melee", melee); Assign(seeker, "visuals", visuals); Assign(seeker, "sounds", seekerAudio);

                SetLayerRecursively(root, enemyLayer);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, SeekerPrefabPath);
                return saved != null ? saved.GetComponent<Seeker>() : null;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// Collider-less primitive (the root's capsule is the only collider). Casts shadows.
        static Renderer SeekerPart(string name, PrimitiveType shape, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(shape);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            return renderer;
        }

        // ---------- scene ----------

        /// Spawn point + patrol route in the puzzle room, muffling test listeners and floor marks, RunStats.
        static void BuildSeekerTest(SeekerAssets assets)
        {
            Transform group = new GameObject("Seeker Test").transform;

            // Patrol loop in the puzzle room (x 10.2..22.2, z -6..6), clear of the cage (z < -3.3) and the switches.
            // It walks away from the doorway half the time, so you can watch it from the white room first.
            Transform route = new GameObject("Patrol Route").transform;
            route.SetParent(group, false);
            Vector3[] points =
            {
                new Vector3(20f, 0f, -1.5f),
                new Vector3(14f, 0f, -1.5f),
                new Vector3(14f, 0f, 3.5f),
                new Vector3(20f, 0f, 3.5f),
            };
            var waypoints = new Transform[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                waypoints[i] = new GameObject("Waypoint " + (i + 1)).transform;
                waypoints[i].SetParent(route, false);
                waypoints[i].position = points[i];
            }

            var spawnGO = new GameObject("Seeker Spawn Point");
            spawnGO.transform.SetParent(group, false);
            spawnGO.transform.SetPositionAndRotation(new Vector3(20f, 0f, 3.5f), Quaternion.LookRotation(Vector3.back));
            var spawn = spawnGO.AddComponent<SeekerSpawnPoint>();
            Assign(spawn, "seekerPrefab", assets.prefab);
            AssignArray(spawn, "patrolRoute", waypoints);

            // Muffling test (D-131). Both floor marks are ~7.1 m from the puzzle-room listener:
            // blue = a wall in between (muffled x0.5), green = straight through the open doorway (clear).
            Material listenerMat = GetOrCreateMaterial("M_NoiseListener", new Color(0.6f, 0.6f, 0.6f), new Color(0.2f, 0.2f, 0.2f));
            TestListener("Noise Test Listener (puzzle room)", new Vector3(13f, 1.2f, 0f), listenerMat, group);
            TestListener("Noise Test Listener (behind exit door)", new Vector3(24.4f, 1.2f, -2.5f), listenerMat, group);

            Material blue = GetOrCreateMaterial("M_TestMarkBlue", new Color(0.15f, 0.35f, 1f), new Color(0.05f, 0.12f, 0.4f));
            Material green = GetOrCreateMaterial("M_TestMarkGreen", new Color(0.15f, 0.85f, 0.3f), new Color(0.05f, 0.3f, 0.1f));
            FloorMark("Test Mark (blue, behind wall)", new Vector3(8f, 0.005f, 5f), blue, group);
            FloorMark("Test Mark (green, doorway line)", new Vector3(6f, 0.005f, 0f), green, group);

            new GameObject("Run Stats (debug log)").AddComponent<RunStats>().transform.SetParent(group, false);
        }

        static void TestListener(string name, Vector3 position, Material material, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>()); // never blocks walking, bullets or other noise rays
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * 0.4f;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<NoiseTestListener>();
        }

        static void FloorMark(string name, Vector3 position, Material material, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(90f, 0f, 0f));
            go.transform.localScale = new Vector3(1f, 1f, 1f);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ---------- NavMesh (D-135) ----------

        /// Bakes one NavMeshSurface over the whole scene and saves its data as an asset next to the scene,
        /// so it survives reloads. Bodies, held guns, trigger volumes and Interactable things (door panels, the
        /// test button) are left out of the bake; the door panel blocks paths with a carving NavMeshObstacle instead.
        static NavMeshSurface BakeAndSaveNavMesh()
        {
            ConfigureHumanoidAgent(0.4f, 2f);

            var go = new GameObject("NavMesh Surface");
            var surface = go.AddComponent<NavMeshSurface>();
            surface.agentTypeID = 0;
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~(ActorLayersMask() | (1 << LayerMask.NameToLayer(InteractableLayerName)));
            surface.BuildNavMesh();

            if (surface.navMeshData == null)
            {
                Debug.LogError("[TWR] NavMesh bake produced no data.");
                return surface;
            }

            EnsureFolder(NavMeshFolder);
            if (AssetDatabase.LoadAssetAtPath<Object>(NavMeshAssetPath) != null)
                AssetDatabase.DeleteAsset(NavMeshAssetPath);
            surface.navMeshData.name = "NavMesh";
            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshAssetPath);
            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(surface);
            return surface;
        }

        /// The bake uses the built-in "Humanoid" agent type (ID 0). Set it to the Seeker's size.
        static void ConfigureHumanoidAgent(float radius, float height)
        {
            Object projectSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset").FirstOrDefault();
            if (projectSettings == null)
            {
                Debug.LogError("[TWR] Could not open ProjectSettings/NavMeshAreas.asset to set the agent size.");
                return;
            }
            var so = new SerializedObject(projectSettings);
            SerializedProperty list = so.FindProperty("m_Settings");
            for (int i = 0; list != null && i < list.arraySize; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                if (element.FindPropertyRelative("agentTypeID").intValue != 0) continue;
                SerializedProperty r = element.FindPropertyRelative("agentRadius");
                SerializedProperty h = element.FindPropertyRelative("agentHeight");
                if (Mathf.Approximately(r.floatValue, radius) && Mathf.Approximately(h.floatValue, height)) return;
                r.floatValue = radius;
                h.floatValue = height;
                so.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"[TWR] NavMesh agent 'Humanoid' set to radius {radius} m, height {height} m (D-135).");
                return;
            }
        }

        // ---------- validation ----------

        /// Logs the NavMesh state, the noise settings and what every Seeker spawn point (and the prefab it spawns)
        /// is wired to. Returns false if anything the Seeker needs is missing.
        static bool ValidateSeekerSetup()
        {
            bool ok = ValidateNavMesh();
            ok &= ValidateNoiseSettings();

            SeekerSpawnPoint[] spawnPoints = Object.FindObjectsByType<SeekerSpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (spawnPoints.Length == 0)
            {
                Debug.LogError("[TWR] Seeker wiring: no SeekerSpawnPoint in the scene.");
                return false;
            }

            var path = new NavMeshPath();
            foreach (SeekerSpawnPoint spawn in spawnPoints)
            {
                bool spawnOk = true;
                var so = new SerializedObject(spawn);
                var prefab = so.FindProperty("seekerPrefab").objectReferenceValue as Seeker;
                SerializedProperty route = so.FindProperty("patrolRoute");

                var report = new StringBuilder();
                report.AppendLine($"[TWR] Seeker wiring: '{spawn.name}' at {spawn.transform.position}");
                bool spawnOnMesh = NavMesh.SamplePosition(spawn.transform.position, out NavMeshHit spawnHit, 1f, NavMesh.AllAreas);
                report.AppendLine($"  on NavMesh: {(spawnOnMesh ? "yes" : "NO")}");
                spawnOk &= spawnOnMesh;

                report.AppendLine($"  prefab: {(prefab != null ? AssetDatabase.GetAssetPath(prefab) : "MISSING")}");
                if (prefab == null) spawnOk = false;
                else spawnOk &= DescribeSeekerPrefab(prefab, report);

                report.AppendLine($"  patrol route ({route.arraySize} waypoints):");
                for (int i = 0; i < route.arraySize; i++)
                {
                    var waypoint = route.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                    if (waypoint == null) { report.AppendLine($"    [{i}] MISSING"); spawnOk = false; continue; }
                    bool reachable = spawnOnMesh
                        && NavMesh.SamplePosition(waypoint.position, out NavMeshHit wpHit, 1f, NavMesh.AllAreas)
                        && NavMesh.CalculatePath(spawnHit.position, wpHit.position, NavMesh.AllAreas, path)
                        && path.status == NavMeshPathStatus.PathComplete;
                    report.AppendLine($"    [{i}] {waypoint.name} {waypoint.position} reachable: {(reachable ? "yes" : "NO")}");
                    spawnOk &= reachable;
                }

                if (spawnOk) Debug.Log(report.ToString(), spawn);
                else Debug.LogError(report.ToString(), spawn);
                ok &= spawnOk;
            }
            return ok;
        }

        static bool DescribeSeekerPrefab(Seeker prefab, StringBuilder report)
        {
            bool ok = true;
            var so = new SerializedObject(prefab);
            string Field(string field)
            {
                Object value = so.FindProperty(field).objectReferenceValue;
                if (value == null) ok = false;
                return value != null ? value.name : "MISSING";
            }
            report.AppendLine($"  brain: data {Field("data")}, senses {Field("senses")}, health {Field("health")}, " +
                              $"melee {Field("melee")}, visuals {Field("visuals")}, audio {Field("sounds")}");

            var data = so.FindProperty("data").objectReferenceValue as SeekerData;
            if (data != null)
                report.AppendLine($"  SeekerData: {data.maxHealth} HP, melee {data.meleeDamage} dmg / {data.meleeRange} m / wind-up {data.windUpSeconds} s, " +
                                  $"sight {(data.useSight ? "ON" : "off")} ({data.sightConeDegrees} deg; {data.seeMovingDistance}/{data.seeCrouchMovingDistance}/{data.seeStillDistance} m), " +
                                  $"sight blockers: {MaskNames(data.sightBlockers)}");

            SeekerAudio audio = prefab.GetComponent<SeekerAudio>();
            Object sounds = audio != null ? new SerializedObject(audio).FindProperty("sounds").objectReferenceValue : null;
            report.AppendLine($"  sound set: {(sounds != null ? sounds.name : "MISSING")}");
            if (sounds == null) ok = false;

            SeekerDeath death = prefab.GetComponent<SeekerDeath>();
            int disabled = death != null ? new SerializedObject(death).FindProperty("disableOnDeath").arraySize : 0;
            report.AppendLine($"  death: {(death != null ? $"SeekerDeath, switches off {disabled} behaviours" : "MISSING")}");
            if (death == null || disabled == 0) ok = false;

            var agent = prefab.GetComponent<NavMeshAgent>();
            var collider = prefab.GetComponent<CapsuleCollider>();
            int enemyLayer = LayerMask.NameToLayer(EnemyLayerName);
            report.AppendLine($"  layer: {LayerMask.LayerToName(prefab.gameObject.layer)}, " +
                              $"agent: {(agent != null ? $"radius {agent.radius} height {agent.height}" : "MISSING")}, " +
                              $"collider: {(collider != null ? "capsule" : "MISSING")}");
            if (prefab.gameObject.layer != enemyLayer || agent == null || collider == null) ok = false;
            return ok;
        }

        static bool ValidateNavMesh()
        {
            NavMeshSurface[] surfaces = Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None);
            if (surfaces.Length == 0)
            {
                Debug.LogError("[TWR] NavMesh: no NavMeshSurface in the scene. The Seeker can't move.");
                return false;
            }

            bool ok = true;
            foreach (NavMeshSurface surface in surfaces)
            {
                NavMeshData data = surface.navMeshData;
                string assetPath = data != null ? AssetDatabase.GetAssetPath(data) : null;
                if (data == null)
                {
                    Debug.LogError($"[TWR] NavMesh: '{surface.name}' is NOT baked.", surface);
                    ok = false;
                }
                else if (assetPath != NavMeshAssetPath)
                {
                    Debug.LogError($"[TWR] NavMesh: '{surface.name}' data is not saved as {NavMeshAssetPath} (found '{assetPath}'). " +
                                   "It would be lost on reload.", surface);
                    ok = false;
                }
            }

            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            NavMeshBuildSettings agentSettings = NavMesh.GetSettingsByID(0);
            var path = new NavMeshPath();
            bool joined = NavMesh.SamplePosition(WhiteRoomProbe, out NavMeshHit a, 1f, NavMesh.AllAreas)
                          && NavMesh.SamplePosition(PuzzleRoomProbe, out NavMeshHit b, 1f, NavMesh.AllAreas)
                          && NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path)
                          && path.status == NavMeshPathStatus.PathComplete;

            string line = $"[TWR] NavMesh: {triangulation.indices.Length / 3} triangles, saved at {NavMeshAssetPath}, " +
                          $"agent radius {agentSettings.agentRadius} height {agentSettings.agentHeight}, " +
                          $"white room -> doorway -> puzzle room path: {(joined ? "OK" : "MISSING")}";
            if (triangulation.indices.Length == 0 || !joined)
            {
                Debug.LogError(line);
                return false;
            }
            Debug.Log(line);
            return ok;
        }

        static bool ValidateNoiseSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<NoiseSettings>(NoiseSettingsPath);
            var viaResources = Resources.Load<NoiseSettings>(NoiseSettings.ResourcesPath);
            if (settings == null || viaResources == null)
            {
                Debug.LogError($"[TWR] NoiseSettings missing at {NoiseSettingsPath} (NoiseSystem would use built-in defaults).");
                return false;
            }
            bool clean = (settings.occluderMask & ActorLayersMask()) == 0 && settings.occluderMask != 0;
            string line = $"[TWR] NoiseSettings: muffling {(settings.muffleThroughWalls ? "ON" : "off")}, walls = {MaskNames(settings.occluderMask)}, " +
                          $"factors default {settings.defaultWallFactor} / thin {settings.thinWallFactor} / thick {settings.thickWallFactor} / " +
                          $"door {settings.doorPanelFactor}, min {settings.minimumFactor}, max walls {settings.maxWallsCounted}";
            if (clean) Debug.Log(line, settings);
            else Debug.LogError(line + " -> the mask must not be empty or contain Player/Enemy/Weapon/Ignore Raycast.", settings);
            return clean;
        }

        static string MaskNames(int mask)
        {
            var names = new StringBuilder();
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                string layerName = LayerMask.LayerToName(i);
                if (names.Length > 0) names.Append(" + ");
                names.Append(string.IsNullOrEmpty(layerName) ? "#" + i : layerName);
            }
            return names.Length > 0 ? names.ToString() : "(nothing)";
        }
    }
}
