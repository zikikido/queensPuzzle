using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// The one pre-build check for every blob the game bakes from the server.
    ///
    /// Each exporter still owns its own blob — its URL, its request, its validation, and its menu
    /// item for exporting on demand. What they no longer own is the build: two exporters meant two
    /// identical three-question dialogs back to back on every build, which is how a useful prompt
    /// turns into one people dismiss without reading.
    ///
    /// So the questions are asked once, about all of them:
    ///   1. "Check the server?" — opt-in, because a difference PAUSES the build.
    ///   2. Anything different → [Use Fresh] [Keep Current] [Cancel Build], naming what changed
    ///      (all identical → the build continues silently; failures loop a Retry dialog).
    ///   3. Something was baked → "Commit to git?" — a build must not ship what the repo lacks.
    ///
    /// Fresh and Keep apply to everything that changed. Per-blob choices were the obvious
    /// alternative and would put us back where we started, asking one question per blob.
    /// </summary>
    public sealed class BakedBlobsBuildCheck : IPreprocessBuildWithReport
    {
        /// <summary>Every blob the build ships. A new one is a new entry here.</summary>
        static IEnumerable<BakedBlob> All => new[]
        {
            WinStatsExporter.Blob,
            RivalsExporter.Blob,
        };

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            // CI / -batchmode has nobody to answer a dialog: try each, log loudly, never block.
            if (Application.isBatchMode)
            {
                foreach (var blob in All)
                {
                    try { blob.Save(blob.Fetch()); Debug.Log($"[{blob.Name}] baked fresh blob (batch mode)"); }
                    catch (Exception e) { Debug.LogError($"[{blob.Name}] batch fetch FAILED — building with the existing blob. " + Innermost(e).Message); }
                }
                return;
            }

            var blobs = All.ToList();
            string names = string.Join(", ", blobs.Select(b => b.Name));

            int check = EditorUtility.DisplayDialogComplex(
                "Baked data",
                $"Check the baked data against the server before building?\n({names})\n\n" +
                "If the server has anything newer, the build will PAUSE and ask.",
                "Check Server",                  // 0 (ok)
                "Cancel Build",                  // 1 (cancel)
                "Skip — build with baked");      // 2 (alt)
            if (check == 2) return;
            if (check == 1) throw new BuildFailedException("[BakedData] build cancelled by user.");

            while (true)
            {
                var fresh = new Dictionary<BakedBlob, byte[]>();
                var failures = new List<string>();

                foreach (var blob in blobs)
                {
                    try { fresh[blob] = blob.Fetch(); }
                    catch (Exception e) { failures.Add($"{blob.Name}: {Innermost(e).Message}"); }
                    finally { EditorUtility.ClearProgressBar(); }
                }

                if (failures.Count > 0)
                {
                    int choice = EditorUtility.DisplayDialogComplex(
                        "Baked data download FAILED",
                        "Could not fetch from the server:\n\n" + string.Join("\n\n", failures) +
                        "\n\nTry the download again?",
                        "Retry Download",             // 0 (ok)
                        "Cancel Build",               // 1 (cancel)
                        "Continue WITHOUT download"); // 2 (alt)

                    if (choice == 0) continue;
                    if (choice == 2) return;
                    throw new BuildFailedException("[BakedData] build cancelled by user.");
                }

                var changed = fresh.Where(kv => !BytesEqual(kv.Key.Baked(), kv.Value)).ToList();
                if (changed.Count == 0) return;   // all up to date — build continues silently

                string what = string.Join("\n", changed.Select(kv =>
                    kv.Key.Baked() == null
                        ? $"  {kv.Key.Name} — nothing baked yet"
                        : $"  {kv.Key.Name} — the server has newer"));

                int use = EditorUtility.DisplayDialogComplex(
                    "Baked data changed on the server",
                    what + "\n\nThe fresh data is already downloaded and validated — bake it into this build?",
                    "Use Fresh & Continue",          // 0 (ok)
                    "Cancel Build",                  // 1 (cancel)
                    "Keep Current & Continue");      // 2 (alt)

                if (use == 2) return;
                if (use == 1) throw new BuildFailedException("[BakedData] build cancelled by user.");

                try
                {
                    foreach (var kv in changed) kv.Key.Save(kv.Value);
                }
                catch (Exception e)
                {
                    // Saving failed, so re-ask as a failure rather than build on half-written data.
                    EditorUtility.DisplayDialog("Baked data save FAILED", Innermost(e).Message, "OK");
                    continue;
                }

                if (EditorUtility.DisplayDialog("Baked data updated",
                        "The baked data changed:\n\n" + string.Join("\n", changed.Select(kv => "  " + kv.Key.Name)) +
                        "\n\nCommit to git now?\n(Push stays manual.)",
                        "Commit", "Don't Commit"))
                    foreach (var kv in changed) kv.Key.Commit();

                return;
            }
        }

        static bool BytesEqual(byte[] a, byte[] b) => a != null && b != null && a.SequenceEqual(b);

        static Exception Innermost(Exception e) { while (e.InnerException != null) e = e.InnerException; return e; }
    }
}
