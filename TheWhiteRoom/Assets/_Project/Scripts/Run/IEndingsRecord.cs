namespace TWR.Run
{
    /// <summary>
    /// Read-only view of the endings the player has seen (D-039, D-099). The RunManager asks it whether the
    /// curated first run (D-154) is still due: SeenEndingCount == 0 means "no endings saved" (D-159).
    /// The real save-data version comes with D-099; until then StubEndingsRecord is used.
    /// </summary>
    public interface IEndingsRecord
    {
        int SeenEndingCount { get; }
    }

    /// <summary>
    /// Placeholder endings record (D-159): always 0, so every run is the curated first run, unless the debug
    /// toggle "pretend endings saved" is on (then 1, so runs are random). Never writes to disk or PlayerPrefs.
    /// </summary>
    public class StubEndingsRecord : IEndingsRecord
    {
        /// <summary>Debug toggle (overlay). True = behave as if one ending was saved.</summary>
        public bool PretendEndingsSaved { get; set; }

        public int SeenEndingCount => PretendEndingsSaved ? 1 : 0;
    }
}
