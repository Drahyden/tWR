using System.Collections.Generic;
using UnityEngine;
using TWR.Run;

namespace TWR.Endings
{
    /// <summary>
    /// The list of every ending in the game (D-025, D-203). Select picks the highest-priority ending whose rule matches
    /// a frozen RunResult (D-204); on a tie the one listed first wins. The four launch endings (D-195 to D-198) cover the
    /// path x performance matrix (D-028) exactly once; secret endings (D-208) are extra assets with a higher priority.
    /// Validate() is shared by RunConfig.Validate (runtime log) and the [TWR] editor validation.
    /// Create via: Create > TWR > Endings > Ending Catalog. The RunConfig points at it.
    /// </summary>
    [CreateAssetMenu(fileName = "EndingCatalog", menuName = "TWR/Endings/Ending Catalog")]
    public class EndingCatalog : ScriptableObject
    {
        public const int ScriptedMinSeconds = 10;
        public const int ScriptedMaxSeconds = 30;

        [Tooltip("Every ending. End-screen slots follow this order (D-200).")]
        public EndingData[] endings = new EndingData[0];

        static readonly RunPath[] Paths = { RunPath.Escape, RunPath.Discover };

        /// <summary>Launch endings (not secret): the "X / 4" denominator (D-208).</summary>
        public int LaunchCount
        {
            get
            {
                int count = 0;
                if (endings != null)
                    for (int i = 0; i < endings.Length; i++)
                        if (endings[i] != null && !endings[i].isSecret) count++;
                return count;
            }
        }

        /// <summary>The ending for this result (D-204), or null if none matches.</summary>
        public EndingData Select(RunResult result, IEndingsRecord record)
        {
            EndingData best = null;
            if (endings == null || result == null) return null;
            for (int i = 0; i < endings.Length; i++)
            {
                EndingData e = endings[i];
                if (e == null || !e.Matches(result, record)) continue;
                if (best == null || e.priority > best.priority) best = e;
            }
            return best;
        }

        public EndingData FindById(string id)
        {
            if (endings == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < endings.Length; i++)
                if (endings[i] != null && endings[i].id == id) return endings[i];
            return null;
        }

        public EndingData FindByScene(string sceneName)
        {
            if (endings == null || string.IsNullOrEmpty(sceneName)) return null;
            for (int i = 0; i < endings.Length; i++)
                if (endings[i] != null && endings[i].sceneName == sceneName) return endings[i];
            return null;
        }

        /// <summary>
        /// Fills errors (the endings can't work) and warnings. Checks: unique non-empty ids, a scene and a title per ending,
        /// the launch endings cover every path x performance cell exactly once (D-028, D-203), secret endings outrank
        /// every launch ending (D-208), and nothing else could silently win a cell. Allocates; editor/startup only.
        /// </summary>
        public bool Validate(List<string> errors, List<string> warnings)
        {
            int before = errors.Count;
            if (endings == null || endings.Length == 0)
            {
                errors.Add("Ending catalog is empty (D-025: 4 launch endings).");
                return false;
            }

            var ids = new HashSet<string>();
            int maxLaunchPriority = int.MinValue;
            foreach (EndingData e in endings)
            {
                if (e == null) { warnings.Add("Ending catalog has an empty slot."); continue; }
                if (string.IsNullOrEmpty(e.id)) errors.Add($"Ending '{e.name}' has no id (D-212: the save stores ids).");
                else if (!ids.Add(e.id)) errors.Add($"Ending id '{e.id}' is used twice.");
                if (string.IsNullOrEmpty(e.sceneName)) errors.Add($"Ending '{e.DisplayId}' has no scene (D-199).");
                if (e.title == null || e.title.IsEmpty) errors.Add($"Ending '{e.DisplayId}' has no localized title (D-084, D-200).");
                if (e.presentation == EndingPresentation.Scripted && (e.voiceLine == null || e.voiceLine.IsEmpty))
                    warnings.Add($"Scripted ending '{e.DisplayId}' has no inner-voice subtitle key (D-199).");
                if (e.graphic == null) warnings.Add($"Ending '{e.DisplayId}' has no end-screen graphic (D-200).");
                if (!e.isSecret && e.priority > maxLaunchPriority) maxLaunchPriority = e.priority;
            }

            // Launch endings: exactly one per cell (D-028, D-203).
            foreach (RunPath path in Paths)
                foreach (bool good in new[] { true, false })
                {
                    var covering = new List<string>();
                    foreach (EndingData e in endings)
                        if (e != null && !e.isSecret && e.rule.CoversCell(path, good) && e.rule.minScore <= 0f &&
                            (e.rule.requiredFoundEndings == null || e.rule.requiredFoundEndings.Length == 0))
                            covering.Add(e.DisplayId);
                    string cell = $"{path} + {(good ? "Good" : "Poor")}";
                    if (covering.Count == 0) errors.Add($"No launch ending for {cell} (D-028).");
                    else if (covering.Count > 1) errors.Add($"{covering.Count} launch endings for {cell}: {string.Join(", ", covering)} (exactly one, D-203).");
                }

            foreach (EndingData e in endings)
                if (e != null && e.isSecret && e.priority <= maxLaunchPriority)
                    warnings.Add($"Secret ending '{e.DisplayId}' has priority {e.priority}, not above the launch endings ({maxLaunchPriority}); it would never win (D-203).");

            return errors.Count == before;
        }
    }
}
