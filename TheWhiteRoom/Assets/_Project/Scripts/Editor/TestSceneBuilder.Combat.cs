using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Localization;
using UnityEngine.Rendering;
using UnityEngine.UI;
using TWR.Combat;
using TWR.Items;
using TWR.Player;
using TWR.UI;

namespace TWR.EditorTools
{
    /// <summary>
    /// Step 2 (pistol, D-107) part of "TWR/Build Test Scene". Creates the Ammo item (D-050) and Pistol GunData
    /// (D-119) assets, upgrades the Player prefab with PlayerHealth (D-034), Inventory (D-037),
    /// WeaponInventory (D-035, D-088) and a primitive placeholder pistol on the Weapon layer under the camera
    /// (D-070: gun only, no hands; D-123), then adds test targets, ammo pickups (D-121) and
    /// the HUD (D-122). Every reference is wired here, so nothing needs assigning by hand.
    /// </summary>
    public static partial class TestSceneBuilder
    {
        const string AmmoItemPath = Root + "/Data/Items/Item_Ammo.asset";
        const string PistolDataPath = Root + "/Data/Combat/Gun_Pistol.asset";
        const string WeaponHolderName = "WeaponHolder";
        const string PistolName = "Pistol";
        const string IgnoreRaycastLayerName = "Ignore Raycast";

        // Gun sits right-down-forward of the camera, in camera space (metres).
        static readonly Vector3 WeaponHolderPosition = new Vector3(0.16f, -0.15f, 0.3f);

        struct CombatAssets
        {
            public int weaponLayer;
            public int ignoreRaycastLayer;
            public ItemData ammo;
            public GunData pistol;
        }

        // ---------- assets ----------

        static bool PrepareCombatAssets(out CombatAssets assets)
        {
            assets = default;
            assets.weaponLayer = LayerMask.NameToLayer(WeaponLayerName);
            assets.ignoreRaycastLayer = LayerMask.NameToLayer(IgnoreRaycastLayerName);
            if (assets.weaponLayer < 0 || assets.ignoreRaycastLayer < 0)
            {
                Debug.LogError("[TWR] 'Weapon' or 'Ignore Raycast' layer missing. Run TWR > Setup Project.");
                return false;
            }

            EnsureFolder(Root + "/Data/Items");
            EnsureFolder(Root + "/Data/Combat");

            assets.ammo = LoadOrCreate<ItemData>(AmmoItemPath, item =>
            {
                item.itemId = "ammo";
                item.maxStack = Inventory.MaxStack; // D-037: 99
                item.pickupAmount = 12;             // D-120
                item.pickupNoiseRadius = 2f;
            });
            assets.pistol = LoadOrCreate<GunData>(PistolDataPath, gun => gun.gunType = GunType.Pistol);
            if (assets.ammo == null || assets.pistol == null)
            {
                Debug.LogError("[TWR] Could not create " + AmmoItemPath + " / " + PistolDataPath);
                return false;
            }

            // Fill links that may be empty (also repairs older copies of the assets).
            if (assets.ammo.displayName == null || assets.ammo.displayName.IsEmpty)
            {
                assets.ammo.displayName = new LocalizedString("UI", "item.ammo.name");
                EditorUtility.SetDirty(assets.ammo);
            }
            if (assets.pistol.displayName == null || assets.pistol.displayName.IsEmpty)
            {
                assets.pistol.displayName = new LocalizedString("UI", "gun.pistol.name");
                EditorUtility.SetDirty(assets.pistol);
            }
            if (assets.pistol.ammoItem == null)
            {
                assets.pistol.ammoItem = assets.ammo; // D-050: one shared ammo type
                EditorUtility.SetDirty(assets.pistol);
            }
            AssetDatabase.SaveAssets();
            return true;
        }

        static T LoadOrCreate<T>(string path, System.Action<T> initialise) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;

            asset = ScriptableObject.CreateInstance<T>();
            initialise?.Invoke(asset);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Debug.Log("[TWR] Created " + path);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        /// Bullets hit everything except the held gun (Weapon) and trigger volumes / pickups (Ignore Raycast).
        /// Interactable stays included so buttons and switches stop bullets (D-118).
        static int ShootMask(CombatAssets assets) => ~((1 << assets.weaponLayer) | (1 << assets.ignoreRaycastLayer));

        // ---------- player prefab ----------

        /// Opens the Player prefab, adds whatever step-2 parts are missing, re-wires references, saves.
        static void UpgradePlayerPrefab(CombatAssets assets, PlayerTuning tuning, InputActionAsset input)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                EnsurePlayerCombat(contents, assets, tuning, input);
                AssignSharedAssets(contents, tuning, input);
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// Adds health, inventory, weapon slots and the pistol to a player hierarchy if missing,
        /// then (re)assigns every reference. Safe to run on a player that already has them.
        static void EnsurePlayerCombat(GameObject root, CombatAssets assets, PlayerTuning tuning, InputActionAsset input)
        {
            var reader = root.GetComponent<PlayerInputReader>();
            var look = root.GetComponent<PlayerLook>();
            Camera cam = root.GetComponentInChildren<Camera>(true);
            if (reader == null || look == null || cam == null)
            {
                Debug.LogError("[TWR] Player prefab is missing its input reader, look or camera. Use TWR > Rebuild Player Prefab.");
                return;
            }

            // Step 4: the player's capsule lives on the Player layer, so noise muffling and enemy sight never
            // treat the player's own body as a wall (D-131). Only the root: children have no colliders.
            int playerLayer = LayerMask.NameToLayer(PlayerLayerName);
            if (playerLayer >= 0) root.layer = playerLayer;

            var inventory = GetOrAdd<Inventory>(root);
            var health = GetOrAdd<PlayerHealth>(root);
            var weapons = GetOrAdd<WeaponInventory>(root);

            // A1 (D-219): the player's one AudioSource (footsteps, jump, land and the gun) goes to SFXState. 2D, but it
            // still picks up the room's reverb zone (D-077, D-224).
            RouteAudio(root.GetComponent<AudioSource>(), TWR.Audio.AudioBus.SFX);

            Transform holder = cam.transform.Find(WeaponHolderName);
            if (holder == null)
            {
                holder = new GameObject(WeaponHolderName).transform;
                holder.SetParent(cam.transform, false); // child of the camera: inherits lean + head bob (D-111)
            }
            holder.localPosition = WeaponHolderPosition;
            holder.localRotation = Quaternion.identity;

            Transform pistolTransform = holder.Find(PistolName);
            Gun pistol = pistolTransform != null ? pistolTransform.GetComponent<Gun>() : null;
            if (pistol == null)
            {
                if (pistolTransform != null) Object.DestroyImmediate(pistolTransform.gameObject);
                pistol = BuildPlaceholderPistol(holder);
            }
            SetLayerRecursively(holder.gameObject, assets.weaponLayer);

            Assign(pistol, "data", assets.pistol);
            AssignLayerMask(pistol, "hitMask", ShootMask(assets));

            Assign(health, "tuning", tuning);
            Assign(weapons, "tuning", tuning);
            Assign(weapons, "input", reader);
            Assign(weapons, "look", look);
            Assign(weapons, "inventory", inventory);
            Assign(weapons, "aimCamera", cam);

            var so = new SerializedObject(weapons);
            SerializedProperty slots = so.FindProperty("slots");
            slots.arraySize = WeaponInventory.SlotCount;
            slots.GetArrayElementAtIndex(0).objectReferenceValue = pistol; // slot 1 = pistol (D-035)
            so.ApplyModifiedPropertiesWithoutUndo();

            // Step 5: portal crossing + camera layer setup (TestSceneBuilder.Portal.cs, D-140 / D-142).
            EnsurePlayerPortalParts(root, cam);
        }

        /// Primitive pistol: frame, slide, grip, a separate magazine child (slides out on reload,
        /// D-117) and a muzzle with a cheap flash. No colliders, no shadows, no hands (D-070).
        static Gun BuildPlaceholderPistol(Transform holder)
        {
            Material metal = GetOrCreateMaterial("M_GunMetal", new Color(0.12f, 0.12f, 0.13f), Color.black);
            Material magMat = GetOrCreateMaterial("M_GunMagazine", new Color(0.32f, 0.32f, 0.35f), Color.black);
            Material flashMat = GetOrCreateUnlitMaterial("M_MuzzleFlash", new Color(4f, 3.2f, 1.8f, 1f));

            var pistolGO = new GameObject(PistolName);
            pistolGO.transform.SetParent(holder, false);

            Transform model = new GameObject("Model").transform;
            model.SetParent(pistolGO.transform, false);

            Transform slide = GunPart("Slide", model, new Vector3(0f, 0.032f, 0.03f), new Vector3(0.032f, 0.032f, 0.19f), Vector3.zero, metal);
            GunPart("Frame", model, new Vector3(0f, 0.002f, 0.015f), new Vector3(0.03f, 0.028f, 0.16f), Vector3.zero, metal);
            GunPart("Grip", model, new Vector3(0f, -0.055f, -0.045f), new Vector3(0.03f, 0.095f, 0.045f), new Vector3(12f, 0f, 0f), metal);
            Transform magazine = GunPart("Magazine", model, new Vector3(0f, -0.068f, -0.048f), new Vector3(0.024f, 0.1f, 0.034f), new Vector3(12f, 0f, 0f), magMat);

            Transform muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(model, false);
            muzzle.localPosition = new Vector3(0f, 0.032f, 0.13f);

            Transform flashQuad = GunPart("Flash", muzzle, new Vector3(0f, 0f, 0.03f), Vector3.one * 0.09f, Vector3.zero, flashMat, PrimitiveType.Quad);
            var lightGO = new GameObject("Flash Light");
            lightGO.transform.SetParent(muzzle, false);
            lightGO.transform.localPosition = new Vector3(0f, 0f, 0.05f);
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 5f;
            light.intensity = 2f;
            light.color = new Color(1f, 0.85f, 0.6f);
            light.shadows = LightShadows.None;
            light.enabled = false;

            var flash = muzzle.gameObject.AddComponent<MuzzleFlash>();
            Assign(flash, "flashRenderer", flashQuad.GetComponent<Renderer>());
            Assign(flash, "flashLight", light);

            var gun = pistolGO.AddComponent<Gun>();
            Assign(gun, "muzzle", muzzle);
            Assign(gun, "muzzleFlash", flash);

            var view = pistolGO.AddComponent<GunViewModel>();
            Assign(view, "gun", gun);
            Assign(view, "model", model);
            Assign(view, "magazine", magazine);
            Assign(view, "slide", slide);
            return gun;
        }

        static Transform GunPart(string name, Transform parent, Vector3 position, Vector3 scale, Vector3 euler,
                                 Material material, PrimitiveType shape = PrimitiveType.Cube)
        {
            GameObject go = GameObject.CreatePrimitive(shape);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>()); // the gun must never block rays or movement
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        static Material GetOrCreateUnlitMaterial(string name, Color colour)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", colour); // HDR (> 1) so Bloom makes it glow (D-064)
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
        }

        // ---------- test props ----------

        static void BuildCombatTestProps(CombatAssets assets)
        {
            Material dummyMat = GetOrCreateMaterial("M_Dummy", new Color(0.8f, 0.72f, 0.65f), Color.black);
            Material targetMat = GetOrCreateMaterial("M_ShootTarget", new Color(0.2f, 0.5f, 1f), new Color(0.1f, 0.25f, 0.5f));
            Material ammoMat = GetOrCreateMaterial("M_AmmoBox", new Color(0.35f, 0.45f, 0.2f), new Color(0.1f, 0.15f, 0.05f));
            Material zoneMat = GetOrCreateMaterial("M_DamageZone", new Color(0.9f, 0.2f, 0.2f), new Color(0.4f, 0.05f, 0.05f));

            Transform group = new GameObject("Combat Test").transform;

            // Damage dummies (IDamageable): 4 pistol shots each, then dissolve placeholder + respawn.
            Dummy("Damage Dummy A", new Vector3(-2f, 1f, 8f), dummyMat, group);
            Dummy("Damage Dummy B", new Vector3(1.5f, 1f, 8.5f), dummyMat, group);

            // Shootable targets (IShootable) on the north wall.
            GameObject anyGun = Block("Shootable Target (any gun)", new Vector3(5f, 1.6f, 9.94f), new Vector3(0.6f, 0.6f, 0.1f), targetMat, group);
            GameObjectUtility.SetStaticEditorFlags(anyGun, 0);
            anyGun.AddComponent<TestShootableTarget>(); // default requirement = any gun

            // Gun filter demo: only a Revolver triggers this one, so the pistol is ignored (no hit marker).
            // A pistol-only switch would be the same with requiredGun = Pistol.
            GameObject revolverOnly = Block("Shootable Target (Revolver only)", new Vector3(7f, 1.6f, 9.94f), new Vector3(0.6f, 0.6f, 0.1f), targetMat, group);
            GameObjectUtility.SetStaticEditorFlags(revolverOnly, 0);
            var filtered = revolverOnly.AddComponent<TestShootableTarget>();
            var so = new SerializedObject(filtered);
            so.FindProperty("requirement.requireSpecificGun").boolValue = true;
            so.FindProperty("requirement.requiredGun").enumValueIndex = (int)GunType.Revolver;
            SerializedProperty colours = so.FindProperty("colours");
            colours.arraySize = 2;
            colours.GetArrayElementAtIndex(0).colorValue = new Color(0.45f, 0.45f, 0.48f);
            colours.GetArrayElementAtIndex(1).colorValue = new Color(0.7f, 0.2f, 0.9f);
            so.ApplyModifiedPropertiesWithoutUndo();

            // Walk-over ammo pickups (D-121), +12 each (D-120).
            AmmoPickup("Ammo Pickup A", new Vector3(-1f, 0f, -3f), assets, ammoMat, group);
            AmmoPickup("Ammo Pickup B", new Vector3(1f, 0f, -3f), assets, ammoMat, group);

            // Test-only damage pad to try the HP bar and death restart (D-034) without a debug key.
            GameObject zone = TriggerVolume("Damage Zone (test)", new Vector3(-6f, 0f, -7f), new Vector3(2f, 2f, 2f), assets, group);
            zone.AddComponent<TestDamageZone>();
            Transform pad = GunPart("Pad", zone.transform, new Vector3(0f, 0.011f, 0f), new Vector3(2f, 0.02f, 2f), Vector3.zero, zoneMat);
            pad.gameObject.layer = assets.ignoreRaycastLayer;
        }

        static void Dummy(string name, Vector3 position, Material material, Transform parent)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<TestDamageDummy>();
        }

        static ItemPickup AmmoPickup(string name, Vector3 position, CombatAssets assets, Material material, Transform parent)
        {
            GameObject root = TriggerVolume(name, position, new Vector3(0.8f, 1f, 0.8f), assets, parent);
            var pickup = root.AddComponent<ItemPickup>();
            Assign(pickup, "item", assets.ammo);
            Transform box = GunPart("Box", root.transform, new Vector3(0f, 0.09f, 0f), new Vector3(0.3f, 0.18f, 0.2f), new Vector3(0f, 25f, 0f), material);
            box.gameObject.layer = assets.ignoreRaycastLayer;
            box.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
            return pickup; // R3: the run scenes put it into a PickupGroup (D-177)
        }

        /// Trigger box (feet at "position") on Ignore Raycast, with a kinematic Rigidbody so the
        /// CharacterController reliably fires OnTrigger events. Never blocks movement, bullets or interaction.
        static GameObject TriggerVolume(string name, Vector3 position, Vector3 size, CombatAssets assets, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.layer = assets.ignoreRaycastLayer;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            box.center = new Vector3(0f, size.y * 0.5f, 0f);
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            return go;
        }

        // ---------- HUD ----------

        /// HP bar (bottom left), gun name + ammo (bottom right), hit marker (centre). D-122.
        static void BuildCombatHud(GameObject canvas, GameObject player)
        {
            var health = player.GetComponent<PlayerHealth>();
            var weapons = player.GetComponent<WeaponInventory>();

            // HP bar: background + fill whose right edge HealthBarUI moves.
            RectTransform bar = UiRect("HP Bar", canvas.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 40f), new Vector2(320f, 18f));
            var background = bar.gameObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.15f);
            background.raycastTarget = false;
            RectTransform fill = UiRect("Fill", bar, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = new Vector2(2f, 2f);
            fill.offsetMax = new Vector2(-2f, -2f);
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.raycastTarget = false;
            var healthBar = bar.gameObject.AddComponent<HealthBarUI>();
            Assign(healthBar, "health", health);
            Assign(healthBar, "fill", fill);
            Assign(healthBar, "fillImage", fillImage);

            // Gun name + ammo, bottom right. Text comes from localization at runtime (D-084).
            TMP_Text ammo = HudLabel("Ammo", canvas.transform, new Vector2(-40f, 40f), 44f);
            TMP_Text gunName = HudLabel("Gun Name", canvas.transform, new Vector2(-40f, 92f), 26f);
            var weaponHud = canvas.AddComponent<WeaponHudUI>();
            Assign(weaponHud, "weapons", weapons);
            Assign(weaponHud, "gunNameLabel", gunName);
            Assign(weaponHud, "ammoLabel", ammo);

            // Hit marker: an X of four short bars around the crosshair dot (D-091).
            RectTransform marker = UiRect("Hit Marker", canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));
            var group = marker.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            var bars = new Graphic[4];
            Vector2[] corners = { new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(-1f, -1f), new Vector2(1f, -1f) };
            for (int i = 0; i < 4; i++)
            {
                RectTransform piece = UiRect("Bar " + i, marker, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), corners[i] * 9f, new Vector2(3f, 11f));
                piece.localRotation = Quaternion.Euler(0f, 0f, corners[i].x * corners[i].y > 0f ? -45f : 45f);
                var image = piece.gameObject.AddComponent<Image>();
                image.raycastTarget = false;
                image.color = new Color(0.1f, 0.1f, 0.1f, 1f);
                bars[i] = image;
            }
            var hitMarker = marker.gameObject.AddComponent<HitMarkerUI>();
            Assign(hitMarker, "weapons", weapons);
            Assign(hitMarker, "group", group);
            var markerSo = new SerializedObject(hitMarker);
            SerializedProperty barsProperty = markerSo.FindProperty("bars");
            barsProperty.arraySize = bars.Length;
            for (int i = 0; i < bars.Length; i++) barsProperty.GetArrayElementAtIndex(i).objectReferenceValue = bars[i];
            markerSo.ApplyModifiedPropertiesWithoutUndo();
        }

        static RectTransform UiRect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        static TMP_Text HudLabel(string name, Transform parent, Vector2 position, float fontSize)
        {
            RectTransform rect = UiRect(name, parent, new Vector2(1f, 0f), new Vector2(1f, 0f), position, new Vector2(500f, fontSize * 1.3f));
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = string.Empty;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.BottomRight;
            label.color = new Color(0.12f, 0.12f, 0.12f);
            label.raycastTarget = false;
            return label;
        }
    }
}
