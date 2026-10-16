using System;
using System.Collections.Generic;

namespace TWR.Save
{
    /// <summary>
    /// What the one local save file holds (D-099, D-201, D-212), as JSON: a format version and the ids of every ending
    /// found (EndingData.id). Ids the game doesn't know (e.g. from a newer version) are kept, never dropped. Settings
    /// (D-090) will be added here later as new fields; bump CurrentVersion and migrate in SaveFile.Load when the
    /// format changes. Meta only: nothing in here may give a gameplay advantage (D-039).
    /// </summary>
    [Serializable]
    public class SaveData
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public List<string> foundEndings = new List<string>();
    }
}
