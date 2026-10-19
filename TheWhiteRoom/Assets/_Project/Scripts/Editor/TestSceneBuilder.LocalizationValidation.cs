using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.SceneManagement;

namespace TWR.EditorTools
{
    /// <summary>
    /// [TWR] localization validation (D-084), menu "TWR/Validate Localization", also run by "TWR/Setup Project",
    /// "TWR/Run/Validate Run Setup", "TWR/Run/Build Run Scenes" and "TWR/Run/Build Endings + Main Menu". It checks:
    /// - the UI and Subtitles string table collections exist once each, have an English table, and are in Addressables
    ///   (the runtime loads tables through Addressables, so a table outside it shows only keys);
    /// - EVERY key in those tables has English text (a hand-made key with no text is reported);
    /// - every starter key the builders add (ProjectSetup) is present with English;
    /// - every LocalizedString used in an enabled Build Settings scene, a prefab or a data asset under Assets/_Project
    ///   points at an existing table + key with English text (empty LocalizedStrings are skipped: they mean "no text");
    /// - the Addressables Play Mode Script: "Use Existing Build" shows old or missing strings in Play mode until
    ///   Addressables are rebuilt, so it is reported (warning) with the fix.
    /// Opens scenes additively and closes them again. Read only: the builders (ProjectSetup) add missing keys.
    /// </summary>
    public static partial class TestSceneBuilder
    {
        static readonly string[] LocalizedTables = { "UI", "Subtitles" };

        [MenuItem("TWR/Validate Localization")]
        public static void ValidateLocalizationMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (ValidateLocalization()) Debug.Log("[TWR] Localization is valid (D-084).");
            else Debug.LogError("[TWR] Localization has problems (see the [TWR] Localization report above).");
        }

        /// <summary>Tables only (no scene scan): quick check after TWR > Setup Project.</summary>
        internal static bool ValidateLocalizationTables()
        {
            var report = new StringBuilder();
            report.AppendLine("[TWR] Localization tables (D-084)");
            bool ok = ValidateTables(report);
            CheckAddressablesPlayMode(report);
            Log(report, ok);
            return ok;
        }

        static bool ValidateLocalization()
        {
            var report = new StringBuilder();
            report.AppendLine("[TWR] Localization (D-084)");
            bool ok = ValidateTables(report);
            ok &= ValidateLocalizedStringsInBuildScenes(report);
            ok &= ValidateLocalizedStringsInAssets(report);
            CheckAddressablesPlayMode(report);
            Log(report, ok);
            return ok;
        }

        static void Log(StringBuilder report, bool ok)
        {
            if (ok) Debug.Log(report.ToString());
            else Debug.LogError(report.ToString());
        }

        // ---------- tables ----------

        static bool ValidateTables(StringBuilder report)
        {
            bool ok = true;
            var english = new LocaleIdentifier(ProjectSetup.EnglishCode);
            AddressableAssetSettings addressables = AddressableAssetSettingsDefaultObject.Settings;

            foreach (string tableName in LocalizedTables)
            {
                int count = LocalizationEditorSettings.GetStringTableCollections().Count(c => c != null && c.TableCollectionName == tableName);
                ok &= Check(report, $"string table '{tableName}' exists once", count == 1,
                            count == 0 ? "missing (run TWR > Setup Project)" :
                            count > 1 ? $"{count} collections share this name; delete the extra one(s) so every builder and the game use the same table" : "");
                StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection(tableName);
                if (collection == null || collection.SharedData == null) continue;

                var table = collection.GetTable(english) as StringTable;
                ok &= Check(report, $"'{tableName}' has an English (en) table", table != null,
                            table != null ? "" : "missing (run TWR > Setup Project, it adds one)");

                if (addressables != null)
                {
                    bool inAddressables = IsAddressable(addressables, collection.SharedData) && (table == null || IsAddressable(addressables, table));
                    ok &= Check(report, $"'{tableName}' shared data and English table are in Addressables", inAddressables,
                                inAddressables ? "" : "not addressable: the game cannot load it and shows keys. Fix: open Window > Asset Management > " +
                                                      "Localization Tables, select the table, and re-add it (or delete it and run TWR > Setup Project)");
                }

                if (table == null) continue;
                var noEnglish = new List<string>();
                foreach (SharedTableData.SharedTableEntry shared in collection.SharedData.Entries)
                {
                    StringTableEntry entry = table.GetEntry(shared.Id);
                    if (entry == null || string.IsNullOrEmpty(entry.Value)) noEnglish.Add(shared.Key);
                }
                ok &= Check(report, $"'{tableName}': all {collection.SharedData.Entries.Count} key(s) have English text", noEnglish.Count == 0,
                            noEnglish.Count == 0 ? "" : "no English text: " + string.Join(", ", noEnglish) +
                                                        " (type the text in Window > Asset Management > Localization Tables, or add the key to ProjectSetup and run TWR > Setup Project)");
            }

            var missingStarter = ProjectSetup.StarterKeys().Where(k => !ProjectSetup.TableHasEnglish(k.table, k.key))
                                                           .Select(k => k.table + "/" + k.key).ToList();
            ok &= Check(report, "every key the builders add is present with English text", missingStarter.Count == 0,
                        missingStarter.Count == 0 ? "" : "missing: " + string.Join(", ", missingStarter) + " (run TWR > Setup Project)");
            return ok;
        }

        static bool IsAddressable(AddressableAssetSettings settings, Object asset)
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
            return !string.IsNullOrEmpty(guid) && settings.FindAssetEntry(guid) != null;
        }

        /// "Use Existing Build" plays the last Addressables build: strings added since then are missing in Play mode.
        static void CheckAddressablesPlayMode(StringBuilder report)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                report.AppendLine("  Addressables settings: WARNING - none found (the Localization package creates them; run TWR > Setup Project)");
                return;
            }
            var builder = settings.ActivePlayModeDataBuilder;
            string name = builder != null ? builder.Name : "(none)";
            bool existingBuild = builder != null && builder.GetType().Name == "BuildScriptPackedPlayMode";
            report.AppendLine(existingBuild
                ? $"  Addressables Play Mode Script: WARNING - '{name}': Play mode uses the last Addressables build, so new or changed strings show as keys. " +
                  "Fix: Window > Asset Management > Addressables > Groups > Play Mode Script > Use Asset Database (fastest)"
                : $"  Addressables Play Mode Script: OK - '{name}'");
        }

        // ---------- LocalizedString references ----------

        static bool ValidateLocalizedStringsInBuildScenes(StringBuilder report)
        {
            bool ok = true;
            foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes)
            {
                if (!entry.enabled || !File.Exists(entry.path)) continue;
                Scene scene = SceneManager.GetSceneByPath(entry.path);
                bool openedHere = false;
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    scene = EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Additive);
                    openedHere = true;
                }
                try
                {
                    var problems = new List<string>();
                    int checkedCount = 0;
                    foreach (MonoBehaviour behaviour in InScene<MonoBehaviour>(scene))
                        if (behaviour != null) checkedCount += CheckLocalizedStrings(behaviour, HierarchyPath(behaviour.transform), problems);
                    ok &= Check(report, $"scene '{scene.name}': {checkedCount} localized text(s) have a key with English", problems.Count == 0,
                                problems.Count == 0 ? "" : "\n      " + string.Join("\n      ", problems));
                }
                finally
                {
                    if (openedHere) EditorSceneManager.CloseScene(scene, true);
                }
            }
            return ok;
        }

        static bool ValidateLocalizedStringsInAssets(StringBuilder report)
        {
            var problems = new List<string>();
            int checkedCount = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                foreach (MonoBehaviour behaviour in prefab.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour != null) checkedCount += CheckLocalizedStrings(behaviour, Path.GetFileName(path) + " > " + HierarchyPath(behaviour.transform), problems);
            }
            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith(Root + "/Data/Localization")) continue; // the tables themselves
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is ScriptableObject so && so != null) checkedCount += CheckLocalizedStrings(so, Path.GetFileName(path), problems);
            }
            return Check(report, $"prefabs + data assets: {checkedCount} localized text(s) have a key with English", problems.Count == 0,
                         problems.Count == 0 ? "" : "\n      " + string.Join("\n      ", problems));
        }

        /// Checks every serialized LocalizedString field of this object (arrays and nested classes included). Returns how many were checked.
        static int CheckLocalizedStrings(Object target, string where, List<string> problems)
        {
            int count = 0;
            var so = new SerializedObject(target);
            SerializedProperty it = so.GetIterator();
            bool enterChildren = true;
            while (it.Next(enterChildren))
            {
                enterChildren = true;
                if (it.propertyType != SerializedPropertyType.Generic || it.type != nameof(LocalizedString)) continue;
                enterChildren = false; // its children are the references read below

                string tableName = it.FindPropertyRelative("m_TableReference.m_TableCollectionName")?.stringValue;
                string key = it.FindPropertyRelative("m_TableEntryReference.m_Key")?.stringValue;
                long keyId = it.FindPropertyRelative("m_TableEntryReference.m_KeyId")?.longValue ?? 0;
                if (string.IsNullOrEmpty(tableName) && string.IsNullOrEmpty(key) && keyId == 0) continue; // empty = no text on purpose
                count++;

                string label = $"{where} ({target.GetType().Name}.{it.propertyPath})";
                string problem = LocalizedStringProblem(tableName, key, keyId, out string shown);
                if (problem != null) problems.Add($"{label}: {shown} - {problem}");
            }
            return count;
        }

        /// Null if fine; otherwise what is wrong. tableName may be a name or "GUID:..." (a reference picked in the Inspector).
        static string LocalizedStringProblem(string tableName, string key, long keyId, out string shown)
        {
            shown = $"{tableName}/{(string.IsNullOrEmpty(key) ? "id " + keyId : key)}";
            if (string.IsNullOrEmpty(tableName)) return "no table set";
            const string GuidPrefix = "GUID:";
            TableReference tableReference;
            if (tableName.StartsWith(GuidPrefix))
            {
                if (!System.Guid.TryParse(tableName.Substring(GuidPrefix.Length), out System.Guid guid)) return "the table reference is broken";
                tableReference = guid;
            }
            else tableReference = tableName;
            StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection(tableReference);
            if (collection == null || collection.SharedData == null) return "the table does not exist";

            SharedTableData.SharedTableEntry shared = keyId != 0 ? collection.SharedData.GetEntry(keyId) : collection.SharedData.GetEntry(key);
            if (shared == null) return "the key is not in the table (run TWR > Setup Project)";
            shown = $"{collection.TableCollectionName}/{shared.Key}";

            var table = collection.GetTable(new LocaleIdentifier(ProjectSetup.EnglishCode)) as StringTable;
            StringTableEntry entry = table != null ? table.GetEntry(shared.Id) : null;
            return entry == null || string.IsNullOrEmpty(entry.Value) ? "no English text" : null;
        }
    }
}
