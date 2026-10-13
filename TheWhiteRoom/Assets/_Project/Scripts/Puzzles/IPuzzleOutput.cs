using UnityEngine;

namespace TWR.Puzzles
{
    /// <summary>
    /// Something a solved puzzle switches on (D-124): a door lock, a bridge, a light, a portal...
    /// Put the component in the PuzzleController's "outputs" list. The door lock (DoorLock) is just one kind.
    /// An interface (instead of a UnityEvent) so outputs are type-checked, easy to follow in code, and
    /// can be wired automatically by the scene builder.
    /// </summary>
    public interface IPuzzleOutput
    {
        /// <summary>Called once when the puzzle is solved. solver = who made the final hit (may be null).</summary>
        void OnPuzzleSolved(PuzzleController puzzle, GameObject solver);
    }
}
