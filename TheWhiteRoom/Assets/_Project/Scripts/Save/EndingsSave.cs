using System;
using System.Collections.Generic;
using UnityEngine;
using TWR.Run;

namespace TWR.Save
{
    /// <summary>
    /// The real endings record (D-201, D-205, D-212): which endings were found, stored in the save file. Implements
    /// IEndingsRecord, so the RunManager uses the curated seed-0 first run only while nothing is saved (D-154, D-159) and
    /// random seeds afterwards. RecordFound writes the file at once (D-205: quitting during the ending still counts);
    /// ClearAll is the F1 debug "clear saved endings". Inner-voice hook (D-039, D-201): SeenEndingCount, HasFound(id)
    /// and the Changed event. Meta only, no gameplay effect (D-039). One shared instance, loaded from disk on first use
    /// in each Play session.
    /// </summary>
    public sealed class EndingsSave : IEndingsRecord
    {
        static EndingsSave shared;

        /// <summary>The game's endings record (loaded from SaveFile.DefaultPath on first use).</summary>
        public static EndingsSave Shared => shared ?? (shared = new EndingsSave(SaveFile.DefaultPath));

        readonly string path;
        readonly SaveData data;
        readonly HashSet<string> found;

        public event Action Changed;

        public EndingsSave(string path)
        {
            this.path = path;
            data = SaveFile.Load(path);
            found = new HashSet<string>(data.foundEndings);
            Debug.Log($"[Save] Endings save: {found.Count} ending(s) found ({path}).");
        }

        /// <summary>Where the file lives (shown in the F1 overlay).</summary>
        public string FilePath => path;
        /// <summary>Found ids in the order they were found.</summary>
        public IReadOnlyList<string> FoundIds => data.foundEndings;

        public int SeenEndingCount => found.Count;

        public bool HasFound(string endingId) => !string.IsNullOrEmpty(endingId) && found.Contains(endingId);

        public bool RecordFound(string endingId)
        {
            if (string.IsNullOrEmpty(endingId) || !found.Add(endingId)) return false;
            data.foundEndings.Add(endingId);
            SaveFile.Write(path, data);
            Changed?.Invoke();
            return true;
        }

        public void ClearAll()
        {
            found.Clear();
            data.foundEndings.Clear();
            SaveFile.Write(path, data); // keeps the file (later it also holds settings, D-212)
            Debug.Log("[Save] Saved endings cleared (F1 debug, D-201). The next run is the curated first run (D-154).");
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => shared = null; // re-read the file each Play session
    }
}
