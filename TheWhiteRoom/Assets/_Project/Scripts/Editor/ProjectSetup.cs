using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace TWR.EditorTools
{
    /// <summary>
    /// Editor menu "TWR/Setup Project": one-click project setup so nothing has to be clicked by hand.
    /// Imports TMP Essentials, creates/activates Localization Settings, the English locale (project locale
    /// + Specific Locale Selector), and the "UI" string table with its starter entries (D-084).
    /// Through TestSceneBuilder.PrepareSharedAssets it also creates the layers and sets up the
    /// draw-the-gun-on-top renderer features (WeaponRenderSetup, D-123).
    /// Safe to run any time: it only creates or fixes what is missing. "TWR/Build Test Scene" runs it first.
    /// </summary>
    public static class ProjectSetup
    {
        const string LocalizationFolder = "Assets/_Project/Data/Localization";
        const string SettingsPath = LocalizationFolder + "/Localization Settings.asset";
        const string LocaleCode = "en";
        const string UiTable = "UI";

        /// Starter English strings for the "UI" table. Existing translations are never overwritten.
        static readonly (string key, string english)[] UiEntries =
        {
            ("interact.generic", "Press F to interact"),
            // Step 2, pistol (D-086 HUD, D-119 / D-122).
            ("gun.pistol.name", "Pistol"),
            ("hud.ammo", "{0} / {1}"),          // {0} = magazine, {1} = reserve. Plain pattern, not a Smart String.
            ("item.ammo.name", "Ammo"),         // inventory grid later (D-087)
            // Step 3, switch puzzle exit door (D-127).
            ("door.locked", "Locked"),
            ("door.open", "Press F to open"),
            // Run Structure R2: fork room door texts (D-150, D-166) and the placeholder end screen (D-092, D-167).
            ("item.health.name", "Health"),     // health pickup (D-171), inventory grid later
            ("sign.exit", "EXIT"),
            ("sign.basement", "BASEMENT"),
            ("run.complete", "Run complete"),
        };

        const string SubtitlesTable = "Subtitles";

        /// Placeholder inner-voice subtitles (D-080, D-089). Keys are final; the English is a stand-in until the
        /// developer writes and records the real lines (D-031).
        static readonly (string key, string english)[] SubtitleEntries =
        {
            ("fork.exit_door", "(placeholder) An exit sign. Too easy?"),
            ("fork.basement_door", "(placeholder) A black door. Basement. Of course there's a basement."),
        };

        [MenuItem("TWR/Setup Project", priority = 0)]
        public static void SetupFromMenu() => Run(() =>
        {
            // Also the Interactable layer, PlayerTuning asset and input-asset check used by the scene builder.
            if (TestSceneBuilder.PrepareSharedAssets(out _, out _, out _))
                Debug.Log("[TWR] Project setup complete.");
        });

        /// Runs setup, then calls onComplete. If TMP Essentials must be imported first, onComplete runs
        /// after that import finishes. Returns false if setup failed (an error is logged).
        public static bool Run(Action onComplete)
        {
            if (!HasTmpEssentials())
            {
                ImportTmpEssentialsThen(() => { if (SetupLocalization()) onComplete?.Invoke(); });
                return true;
            }
            if (!SetupLocalization()) return false;
            onComplete?.Invoke();
            return true;
        }

        // ---------- TextMeshPro ----------

        static bool HasTmpEssentials() => AssetDatabase.FindAssets("t:TMP_Settings").Length > 0;

        static void ImportTmpEssentialsThen(Action next)
        {
            AssetDatabase.ImportPackageCallback done = null;
            AssetDatabase.ImportPackageFailedCallback failed = null;
            AssetDatabase.ImportPackageCallback cancelled = null;
            void Unsubscribe()
            {
                AssetDatabase.importPackageCompleted -= done;
                AssetDatabase.importPackageFailed -= failed;
                AssetDatabase.importPackageCancelled -= cancelled;
            }
            done = _ => { Unsubscribe(); EditorApplication.delayCall += () => next(); };
            failed = (_, error) => { Unsubscribe(); Debug.LogError("[TWR] TMP Essentials import failed: " + error); };
            cancelled = _ => { Unsubscribe(); Debug.LogWarning("[TWR] TMP Essentials import was cancelled."); };
            AssetDatabase.importPackageCompleted += done;
            AssetDatabase.importPackageFailed += failed;
            AssetDatabase.importPackageCancelled += cancelled;

            Debug.Log("[TWR] Importing TMP Essential Resources...");
            TMP_PackageResourceImporter.ImportResources(true, false, false);
        }

        // ---------- Localization (D-084) ----------

        static bool SetupLocalization()
        {
            EnsureFolder(LocalizationFolder);

            LocalizationSettings settings = GetOrCreateSettings();
            if (settings == null)
            {
                Debug.LogError("[TWR] Could not create Localization Settings at " + SettingsPath);
                return false;
            }

            Locale english = GetOrCreateLocale();
            SetProjectLocaleAndSelector(settings, english);
            SetupStringTable(english, UiTable, UiEntries);
            SetupStringTable(english, SubtitlesTable, SubtitleEntries);

            AssetDatabase.SaveAssets();
            return true;
        }

        static LocalizationSettings GetOrCreateSettings()
        {
            LocalizationSettings settings = LocalizationEditorSettings.ActiveLocalizationSettings;
            if (settings == null)
            {
                string guid = AssetDatabase.FindAssets("t:" + nameof(LocalizationSettings)).FirstOrDefault();
                if (guid != null)
                    settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(AssetDatabase.GUIDToAssetPath(guid));
            }
            if (settings == null)
            {
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<LocalizationSettings>(), SettingsPath);
                AssetDatabase.SaveAssets();
                settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(SettingsPath);
                Debug.Log("[TWR] Created " + SettingsPath);
            }
            if (settings != null && LocalizationEditorSettings.ActiveLocalizationSettings != settings)
                LocalizationEditorSettings.ActiveLocalizationSettings = settings;
            return settings;
        }

        static Locale GetOrCreateLocale()
        {
            Locale locale = LocalizationEditorSettings.GetLocale(LocaleCode);
            if (locale != null) return locale;

            // An English locale asset may exist but not be registered yet.
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(Locale)))
            {
                var candidate = AssetDatabase.LoadAssetAtPath<Locale>(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate != null && candidate.Identifier.Code == LocaleCode)
                {
                    locale = candidate;
                    break;
                }
            }
            if (locale == null)
            {
                locale = Locale.CreateLocale(LocaleCode);
                string path = LocalizationFolder + "/English (en).asset";
                AssetDatabase.CreateAsset(locale, path);
                Debug.Log("[TWR] Created locale " + path);
            }
            LocalizationEditorSettings.AddLocale(locale);
            return locale;
        }

        static void SetProjectLocaleAndSelector(LocalizationSettings settings, Locale english)
        {
            var so = new SerializedObject(settings);
            SerializedProperty projectCode = so.FindProperty("m_ProjectLocaleIdentifier.m_Code");
            if (projectCode != null && projectCode.stringValue != english.Identifier.Code)
            {
                projectCode.stringValue = english.Identifier.Code;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // A Specific Locale Selector pointing at English, checked first at startup.
            var selectors = settings.GetStartupLocaleSelectors();
            var specific = selectors.OfType<SpecificLocaleSelector>().FirstOrDefault();
            if (specific == null)
            {
                specific = new SpecificLocaleSelector();
                selectors.Insert(0, specific);
            }
            specific.LocaleId = english.Identifier;
            EditorUtility.SetDirty(settings);
        }

        /// Creates the string table collection if missing and adds the English entries that are missing or empty.
        static void SetupStringTable(Locale english, string tableName, (string key, string english)[] entries)
        {
            StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection(tableName);
            if (collection == null)
            {
                collection = LocalizationEditorSettings.CreateStringTableCollection(tableName, LocalizationFolder);
                Debug.Log("[TWR] Created string table '" + tableName + "' in " + LocalizationFolder);
            }

            var table = collection.GetTable(english.Identifier) as StringTable;
            if (table == null)
                table = collection.AddNewTable(english.Identifier) as StringTable;
            if (table == null)
            {
                Debug.LogError("[TWR] Could not create the English '" + tableName + "' table.");
                return;
            }

            foreach (var (key, englishText) in entries)
            {
                StringTableEntry entry = table.GetEntry(key);
                if (entry == null)
                    table.AddEntry(key, englishText);
                else if (string.IsNullOrEmpty(entry.Value))
                    entry.Value = englishText;
            }
            EditorUtility.SetDirty(table);
            EditorUtility.SetDirty(collection.SharedData);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
