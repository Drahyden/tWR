namespace TWR.Save
{
    /// <summary>
    /// Settings in the D-212 save file (D-226): Load reads them (a missing file or a version 1 file = defaults), Save
    /// writes them with read-modify-write, so the found endings and unknown ids in the same file are kept. Called when a
    /// settings screen is left (D-225), never per frame.
    /// </summary>
    public static class SettingsSave
    {
        /// <summary>The settings on disk now (a copy; changing it changes nothing until Save).</summary>
        public static SettingsData Load()
        {
            SaveData data = SaveFile.Load(SaveFile.DefaultPath);
            return data.settings != null ? data.settings : new SettingsData();
        }

        /// <summary>Write these settings into the save file. False (and an error log) if writing failed.</summary>
        public static bool Save(SettingsData settings)
        {
            if (settings == null) return false;
            SettingsData copy = settings.Clone();
            copy.Sanitize();
            return SaveFile.Update(SaveFile.DefaultPath, file => file.settings = copy);
        }
    }
}
