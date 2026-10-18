using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TWR.EditorTools
{
    /// <summary>
    /// Menu "TWR/Audio/Check Licenses" (D-083, D-218): compares docs/audio/LICENSES.md with the audio files
    /// (.wav, .ogg, .mp3) under Assets/_Project/Audio and logs a [TWR] warning for every file with no row, every row
    /// marked TO VERIFY or UNKNOWN, and every row whose file no longer exists. A file counts as logged when its file
    /// name appears anywhere in a table row, so music stems listed in their track's Notes column count too.
    /// Warnings only, so it never blocks a build; the A1 audio validation still errors on unlogged files (D-227).
    /// </summary>
    static class LicenseLogCheck
    {
        static readonly string[] AudioExtensions = { ".wav", ".ogg", ".mp3" };

        static string AudioRoot => Path.Combine(Application.dataPath, "_Project/Audio");
        static string LogPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/audio/LICENSES.md"));

        [MenuItem("TWR/Audio/Check Licenses")]
        public static void CheckLicenses()
        {
            if (!File.Exists(LogPath))
            {
                Debug.LogWarning($"[TWR] License log not found at {LogPath}. Create it (D-218).");
                return;
            }

            List<string> rows = ReadTableRows(File.ReadAllLines(LogPath));
            List<string> files = Directory.Exists(AudioRoot)
                ? Directory.GetFiles(AudioRoot, "*.*", SearchOption.AllDirectories)
                           .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                           .Select(f => f.Replace('\\', '/'))
                           .OrderBy(f => f, StringComparer.Ordinal)
                           .ToList()
                : new List<string>();

            int warnings = 0;

            // 1. Audio files with no row.
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                if (!rows.Any(r => r.Contains(name)))
                {
                    Debug.LogWarning($"[TWR] License log: '{Relative(file)}' has no row in docs/audio/LICENSES.md.");
                    warnings++;
                }
            }

            // 2. Rows still to be verified or identified.
            foreach (string row in rows)
            {
                if (row.Contains("TO VERIFY") || row.Contains("UNKNOWN"))
                {
                    string status = row.Contains("UNKNOWN") ? "UNKNOWN (ask developer)" : "TO VERIFY";
                    Debug.LogWarning($"[TWR] License log: row '{FileCell(row)}' is {status}.");
                    warnings++;
                }
            }

            // 3. Rows whose file (or wildcard pattern) matches nothing on disk.
            foreach (string row in rows)
            {
                string path = BacktickPath(FileCell(row));
                if (path == null || RowMatchesFiles(path)) continue;
                Debug.LogWarning($"[TWR] License log: row '{path}' points to no existing file.");
                warnings++;
            }

            if (warnings == 0)
                Debug.Log($"[TWR] License log OK: {files.Count} audio files, all logged and verified (D-218).");
            else
                Debug.LogWarning($"[TWR] License log: {warnings} warning(s) for {files.Count} audio files (see above).");
        }

        /// <summary>Table rows of the log, without the header and separator lines.</summary>
        static List<string> ReadTableRows(string[] lines)
        {
            var rows = new List<string>();
            bool headerSeen = false;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (!line.StartsWith("|")) continue;
                if (!headerSeen) { headerSeen = true; continue; }   // first table line is the header
                if (line.StartsWith("|---")) continue;
                rows.Add(line);
            }
            return rows;
        }

        static string FileCell(string row)
        {
            string[] cells = row.Split('|');
            return cells.Length > 1 ? cells[1].Trim() : row;
        }

        /// <summary>The `path` inside backticks, or null for rows with no file (e.g. runtime-generated tones).</summary>
        static string BacktickPath(string cell)
        {
            int start = cell.IndexOf('`');
            int end = start >= 0 ? cell.IndexOf('`', start + 1) : -1;
            return end > start ? cell.Substring(start + 1, end - start - 1) : null;
        }

        static bool RowMatchesFiles(string relativePath)
        {
            string full = Path.Combine(AudioRoot, relativePath);
            string dir = Path.GetDirectoryName(full);
            string pattern = Path.GetFileName(full);
            if (!Directory.Exists(dir)) return false;
            return pattern.Contains("*")
                ? Directory.GetFiles(dir, pattern).Length > 0
                : File.Exists(full);
        }

        static string Relative(string fullPath)
        {
            string root = Application.dataPath.Replace('\\', '/');
            return fullPath.StartsWith(root) ? "Assets" + fullPath.Substring(root.Length) : fullPath;
        }
    }
}
