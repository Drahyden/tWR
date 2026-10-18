using System;
using System.IO;
using UnityEngine;

namespace TWR.Save
{
    /// <summary>
    /// Reads and writes the save file (D-212): one versioned JSON file in Application.persistentDataPath.
    /// Load never throws: a missing, empty or unreadable file counts as "no endings" (curated first run, D-154); an
    /// unreadable file is copied to "*.corrupt" once so it can be looked at. Write goes to a temp file first and then
    /// replaces the real file, so a crash mid-write can't destroy the old save. Only called when an ending is recorded
    /// or cleared (D-211) or settings are saved (D-226), never per frame.
    /// A1 (D-226): format version 2 adds the settings; a version 1 file (or missing values) gets default settings.
    /// Writers use Update (read-modify-write): read the file fresh, change only their part, write it back, so the found
    /// endings, unknown ids and the settings never overwrite each other.
    /// </summary>
    public static class SaveFile
    {
        public const string FileName = "twr_save.json";

        /// <summary>Full path of the save file on this machine.</summary>
        public static string DefaultPath => Path.Combine(Application.persistentDataPath, FileName);

        public static SaveData Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new SaveData();
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return new SaveData();

                SaveData data = JsonUtility.FromJson<SaveData>(json);
                if (data == null) throw new FormatException("empty JSON object");
                return Sanitize(data);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] Could not read {path} ({e.Message}). Treating it as 'no endings saved' (D-212).");
                TryKeepCorruptCopy(path);
                return new SaveData();
            }
        }

        /// <summary>
        /// Read-modify-write (D-226): load the file as it is on disk now, let modify change its part, write it back.
        /// False (and an error log) if writing failed.
        /// </summary>
        public static bool Update(string path, Action<SaveData> modify)
        {
            SaveData data = Load(path);
            modify?.Invoke(data);
            return Write(path, data);
        }

        /// <summary>Write via temp file + replace (D-212). False (and an error log) if the disk refused.</summary>
        public static bool Write(string path, SaveData data)
        {
            string temp = path + ".tmp";
            try
            {
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                data.version = SaveData.CurrentVersion;
                File.WriteAllText(temp, JsonUtility.ToJson(data, true));

                if (!File.Exists(path))
                {
                    File.Move(temp, path);
                    return true;
                }
                try
                {
                    File.Replace(temp, path, null); // atomic swap where the OS supports it
                }
                catch (Exception replaceFailed) when (replaceFailed is IOException || replaceFailed is PlatformNotSupportedException ||
                                                      replaceFailed is UnauthorizedAccessException)
                {
                    File.Copy(temp, path, true);
                    File.Delete(temp);
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Could not write {path}: {e.Message}");
                return false;
            }
        }

        /// Older or newer versions: keep every id (D-212). Drop only empty entries and duplicates.
        static SaveData Sanitize(SaveData data)
        {
            if (data.foundEndings == null) data.foundEndings = new System.Collections.Generic.List<string>();
            for (int i = data.foundEndings.Count - 1; i >= 0; i--)
            {
                string id = data.foundEndings[i];
                if (string.IsNullOrEmpty(id) || data.foundEndings.IndexOf(id) != i) data.foundEndings.RemoveAt(i);
            }
            if (data.version > SaveData.CurrentVersion)
                Debug.LogWarning($"[Save] Save file version {data.version} is newer than this game ({SaveData.CurrentVersion}); reading what we know.");

            // Version 1 -> 2 (D-226): version 1 had no settings, so they start at the defaults.
            if (data.version < 2 || data.settings == null)
            {
                if (data.version < 2) Debug.Log($"[Save] Save file version {data.version} -> {SaveData.CurrentVersion}: settings start at the defaults (D-226).");
                data.settings = new SettingsData();
            }
            data.settings.Sanitize();
            return data;
        }

        static void TryKeepCorruptCopy(string path)
        {
            try
            {
                if (File.Exists(path)) File.Copy(path, path + ".corrupt", true);
            }
            catch (Exception)
            {
                // Best effort only.
            }
        }
    }
}
