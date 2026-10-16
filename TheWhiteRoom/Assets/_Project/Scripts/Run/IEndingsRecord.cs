using System;
using System.Collections.Generic;

namespace TWR.Run
{
    /// <summary>
    /// The endings the player has found (D-039, D-099, D-201). The RunManager asks it whether the curated first run
    /// (D-154) is still due: SeenEndingCount == 0 means "no endings saved" (D-159). It records an ending the moment it is
    /// selected (D-205). Meta only: never gives gameplay advantages (D-039).
    /// Inner-voice hook (D-201): later voice code can read SeenEndingCount / HasFound(id) at run start, or listen to Changed.
    /// The real implementation is TWR.Save.EndingsSave (a JSON file, D-212); StubEndingsRecord is an in-memory stand-in.
    /// </summary>
    public interface IEndingsRecord
    {
        /// <summary>How many different ending ids are saved (unknown ids included, D-212).</summary>
        int SeenEndingCount { get; }

        /// <summary>True if this EndingData id was found in an earlier (or this) run.</summary>
        bool HasFound(string endingId);

        /// <summary>Mark an ending as found and save. Returns true if it was new.</summary>
        bool RecordFound(string endingId);

        /// <summary>Forget every found ending (F1 debug, D-201).</summary>
        void ClearAll();

        /// <summary>The found set changed (recorded or cleared).</summary>
        event Action Changed;
    }

    /// <summary>
    /// In-memory endings record (D-159 stub, kept for tests): nothing is written to disk. "PretendEndingsSaved" makes it
    /// report at least one ending, so runs are random instead of curated.
    /// </summary>
    public class StubEndingsRecord : IEndingsRecord
    {
        readonly HashSet<string> found = new HashSet<string>();

        /// <summary>Debug toggle. True = behave as if one ending was saved.</summary>
        public bool PretendEndingsSaved { get; set; }

        public int SeenEndingCount => PretendEndingsSaved && found.Count == 0 ? 1 : found.Count;

        public event Action Changed;

        public bool HasFound(string endingId) => !string.IsNullOrEmpty(endingId) && found.Contains(endingId);

        public bool RecordFound(string endingId)
        {
            if (string.IsNullOrEmpty(endingId) || !found.Add(endingId)) return false;
            Changed?.Invoke();
            return true;
        }

        public void ClearAll()
        {
            found.Clear();
            Changed?.Invoke();
        }
    }
}
