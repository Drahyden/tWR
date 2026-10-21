#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace TWR.Scares
{
    /// <summary>
    /// The Scares section of the F1 debug menu (D-239), editor and development builds only; text is not localized (D-159).
    /// Shows each scare's state (ready / held and why / fired / skipped), the anchors in the loaded section, the combat
    /// count and the time left until the 120 s gap is over, plus "Fire" buttons (rules skipped, figure 4 m in front of the
    /// player, nothing marked as fired) and "Reset scares this run". Drawn by RunDebugOverlay. IMGUI: allocations are fine.
    /// </summary>
    public static class ScareDebugPanel
    {
        const string Dim = "#8A8A8A";
        const string Good = "#7CFC7C";
        const string Warn = "#FFD54A";

        public static void Draw(GUIStyle rich)
        {
            GUILayout.Label("<b>Scares (D-236 to D-239)</b>", rich);
            ScareDirector director = ScareDirector.Instance;
            if (director == null)
            {
                GUILayout.Label($"<color={Dim}>No ScareDirector in Core: run TWR > Run > Build Run Scenes.</color>", rich);
                return;
            }
            ScareSettings settings = director.Settings;
            if (settings == null || settings.scares == null)
            {
                GUILayout.Label("<color=#FF7A6A>The ScareDirector has no ScareSettings.</color>", rich);
                return;
            }

            float cooldown = director.CooldownLeftSeconds;
            GUILayout.Label($"Playing: {director.PlayingId ?? "-"}   last hit: {director.LastFiredId ?? "-"}   " +
                            $"gap left: {(cooldown > 0f ? $"<color={Warn}>{cooldown:0} s</color>" : "0 s")}   " +
                            $"chasing Seekers: {director.HostileCount}", rich);

            foreach (ScareData data in settings.scares)
            {
                if (data == null) continue;
                ScareGate gate = director.Check(data, out string reason);
                string state = director.HasFired(data) ? $"<color={Dim}>{reason}</color>"
                             : gate == ScareGate.Allowed ? $"<color={Good}>ready</color>"
                             : gate == ScareGate.Held ? $"<color={Warn}>held: {reason}</color>{(director.IsHeld(data) ? " (counted)" : "")}"
                             : $"<color={Dim}>{reason}</color>";
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{data.DisplayId}</b> ({data.template}, {data.stingerId}): {state}", rich, GUILayout.Width(330f));
                if (GUILayout.Button("Fire", GUILayout.Width(60f))) director.DebugFire(data);
                GUILayout.EndHorizontal();
            }

            foreach (ScareAnchor anchor in ScareDirector.Anchors)
            {
                if (anchor == null || anchor.Data == null) continue;
                string extra = anchor.Data.trigger == ScareTrigger.SeenAfterUnobserved
                    ? $"   unobserved {anchor.UnobservedFor:0.0} / {anchor.Data.unobservedSeconds:0} s   figure placed: {(anchor.FigurePrepared ? "yes" : "no")}"
                    : "";
                GUILayout.Label($"<size=11>   anchor '{anchor.Data.DisplayId}' armed: {(anchor.Armed ? "yes" : "no")}{extra}</size>", rich);
            }

            if (GUILayout.Button("Reset scares this run")) director.DebugReset();
        }
    }
}
#endif
