#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace TWR.Audio
{
    /// <summary>
    /// The Music section of the F1 debug menu (D-235), editor and development builds only; text is not localized
    /// (D-159). Shows the playing and wanted state, the track on each bank with every layer (on/off, current volume and
    /// its sync offset to the first stem, to check D-231), the puzzle timer, the chaser count, the boss and ending state,
    /// and buttons to force each state or clear the force. Drawn by RunDebugOverlay (Core / sandbox) or, where none
    /// exists (MainMenu), by the MusicDebugOverlay on the director. RunDebugOverlay registers itself as the host, so F1
    /// is only ever handled once. IMGUI: allocations are fine here (debug only).
    /// </summary>
    public static class MusicDebugPanel
    {
        const string Dim = "#8A8A8A";
        const string OnColour = "#7CFC7C";
        const string Highlight = "#FFD54A";

        static int hostCount;

        /// <summary>True while a RunDebugOverlay exists: it draws this panel and owns F1.</summary>
        public static bool HasHost => hostCount > 0;
        public static void RegisterHost() => hostCount++;
        public static void UnregisterHost() => hostCount = Mathf.Max(0, hostCount - 1);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => hostCount = 0;

        public static void Draw(GUIStyle rich)
        {
            GUILayout.Label("<b>Music (D-230 to D-235)</b>", rich);
            MusicDirector director = MusicDirector.Instance;
            if (director == null)
            {
                GUILayout.Label($"<color={Dim}>No MusicDirector running.</color>", rich);
                return;
            }
            if (director.Library == null)
                GUILayout.Label("<color=#FF7A6A>Resources/MusicLibrary missing: run TWR > Audio > Build Music (A2).</color>", rich);

            string forced = director.IsForced ? $"   <color={Highlight}><b>[forced]</b></color>" : "";
            GUILayout.Label($"State: <b>{director.Playing}</b>{forced}   game wants: {director.Wanted}   " +
                            $"silent room: {(SoundscapeState.IsSilenced ? "<b>yes</b>" : "no")}   snapshot: {AudioState.Current}", rich);

            MusicBank active = director.ActiveBank;
            DrawBank(active, true, rich);
            MusicBank other = active == director.BankA ? director.BankB : director.BankA;
            if (other != null && other.IsPlaying) DrawBank(other, false, rich);

            string puzzle = director.PuzzleActive
                ? $"'{director.PuzzleId}' {director.PuzzleSeconds:0.0} / {director.PuzzleDrumsDelay:0} s -> drums {(director.PuzzleSeconds >= director.PuzzleDrumsDelay ? "ON" : "off")}"
                : $"none (timer {director.PuzzleSeconds:0.0} s)";
            GUILayout.Label($"Puzzle: {puzzle}", rich);
            GUILayout.Label($"Combat chasers: <b>{director.ChaserCount}</b>" +
                            (director.CombatReleasing ? $"   drums out, back in {director.CombatReleaseLeft:0.0} s" : ""), rich);
            GUILayout.Label(director.BossTrackName != null
                ? $"Boss: {director.BossTrackName}   first hit: {(director.BossHit ? "yes" : "no")}   phase 2: {(director.BossPhaseTwo ? "yes" : "no")}"
                : $"<color={Dim}>Boss: none</color>", rich);
            GUILayout.Label(director.EndingId != null
                ? $"Ending: {director.EndingId}   stinger played: {(director.EndingStingerDone ? "yes" : "no")}"
                : $"<color={Dim}>Ending: none</color>", rich);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Main menu")) director.DebugForce(MusicStateKind.MainMenu, null);
            if (GUILayout.Button("Lobby")) director.DebugForce(MusicStateKind.Lobby, null);
            if (GUILayout.Button("Exploration")) director.DebugForce(MusicStateKind.Exploration, null);
            if (GUILayout.Button("Puzzle")) director.DebugForce(MusicStateKind.Puzzle, null);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Combat")) director.DebugForce(MusicStateKind.Combat, null);
            MusicLibrary library = director.Library;
            if (library != null && library.bosses != null)
                foreach (MusicData bossTrack in library.bosses)
                    if (bossTrack != null && GUILayout.Button("Boss " + bossTrack.bossId)) director.DebugForce(MusicStateKind.Boss, bossTrack);
            if (GUILayout.Button("Ending")) director.DebugForce(MusicStateKind.Ending, null);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear force")) director.DebugClearForce();
            if (GUILayout.Button("Puzzle drums now")) director.DebugPuzzleDrumsNow();
            if (GUILayout.Button("Boss hit")) director.DebugBossHit();
            if (GUILayout.Button("Boss phase 2")) director.DebugBossPhaseTwo();
            GUILayout.EndHorizontal();
        }

        static void DrawBank(MusicBank bank, bool isActive, GUIStyle rich)
        {
            if (bank == null || !bank.IsPlaying)
            {
                GUILayout.Label($"<color={Dim}>Track: none</color>", rich);
                return;
            }
            string name = bank.Data != null ? bank.Data.DisplayName : bank.OneShot != null ? bank.OneShot.name + " (stinger, once)" : "?";
            string role = isActive ? "playing" : "fading out";
            GUILayout.Label($"Track: <b>{name}</b>   bank {bank.Label} {role}, volume {bank.Volume:0.00} -> {bank.TargetVolume:0.00}", rich);

            AudioSource first = bank.Source(0);
            for (int i = 0; i < bank.Count; i++)
            {
                AudioSource source = bank.Source(i);
                string stem = bank.Data != null ? bank.Data.StemName(i) : "full mix";
                bool on = bank.LayerOn(i);
                string sync = "";
                if (i > 0 && first != null && source != null && source.clip != null && first.clip != null && source.isPlaying)
                {
                    float ms = (source.timeSamples - first.timeSamples) * 1000f / source.clip.frequency;
                    sync = $"   sync {ms:+0;-0;0} ms";
                }
                string time = source != null && source.clip != null ? $"   {source.time:0.0}/{source.clip.length:0.0} s" : "   (no clip)";
                GUILayout.Label($"<size=11>      {stem}: {(on ? $"<color={OnColour}>ON</color>" : $"<color={Dim}>off</color>")} " +
                                $"{bank.LayerVolume(i):0.00}{time}{sync}</size>", rich);
            }
        }
    }
}
#endif
