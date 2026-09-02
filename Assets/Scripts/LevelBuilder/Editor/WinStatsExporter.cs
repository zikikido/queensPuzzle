using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using qp;

namespace QueensPuzzle
{
    /// <summary>
    /// Winstats blob: per-stage "better than X%" data baked into the build (Resources/winstats.bytes).
    ///
    /// The SERVER packs the binary (single packer, same bytes at build time and, later, over the
    /// runtime endpoint); this exporter only collects the build's level hashes, downloads the blob
    /// and bakes it. See pawdoku-winstats-server/src/winstats.ts for the byte layout — the runtime
    /// reader must match it.
    ///
    /// Two entry points:
    ///  - Manual: QueensPuzzle → Export WinStats — fetch from the winstats-server and bake.
    ///  - EVERY BUILD (IPreprocessBuildWithReport): fetches the fresh blob and compares to the baked
    ///    one. Identical → build continues silently. Different / missing / fetch failed → the
    ///    build PAUSES on a modal: [Download & Continue] [Continue WITHOUT] [Cancel Build];
    ///    a failed download re-shows the same dialog (with the error) until a choice sticks.
    /// </summary>
    public sealed class WinStatsExporter : IPreprocessBuildWithReport
    {
        const string ServerUrl = "https://pawdoku-winstats-server-production.up.railway.app/winstats/build";
        const string AdminKey = "1ebe3fd24ea52fe8c2d02874182168c979959de36c2b67d2";   // editor-only; never ships
        const string DbName = "pawdoku_events";

        const string BlobPath = "Assets/Reskin/Resources/winstats.bytes";
        const string LevelsFolder = "Assets/Reskin/Resources/Levels";

        // ---- manual export -----------------------------------------------------------

        [MenuItem("QueensPuzzle/Export WinStats (fetch from server)")]
        public static void ExportManual()
        {
            try
            {
                byte[] blob = FetchBlob();
                SaveBlob(blob);
                EditorUtility.DisplayDialog("WinStats export OK",
                    $"Blob: {blob.Length / 1024f:0.0} KB -> {BlobPath}", "OK");
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("WinStats export FAILED",
                    "The winstats blob was NOT updated!\n\n" + e.Message, "OK");
                Debug.LogError("[WinStats] export failed: " + e);
            }
        }

        // ---- every build -------------------------------------------------------------

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            // CI / -batchmode has nobody to answer a dialog: try, log loudly, never block.
            if (Application.isBatchMode)
            {
                try { SaveBlob(FetchBlob()); Debug.Log("[WinStats] baked fresh blob (batch mode)"); }
                catch (Exception e) { Debug.LogError("[WinStats] batch fetch FAILED — building with the existing blob. " + e.Message); }
                return;
            }

            string failure = null;
            while (true)
            {
                byte[] fresh = null;
                if (failure == null)
                {
                    try { fresh = FetchBlob(); }
                    catch (Exception e) { failure = Innermost(e).Message; }
                    finally { EditorUtility.ClearProgressBar(); }
                }

                if (fresh != null)
                {
                    byte[] baked = File.Exists(BlobPath) ? File.ReadAllBytes(BlobPath) : null;
                    if (baked != null && baked.SequenceEqual(fresh))
                        return;   // up to date — build continues silently

                    int choice = EditorUtility.DisplayDialogComplex(
                        "WinStats changed on the server",
                        (baked == null ? "There is NO baked winstats blob yet.\n" : "The server has NEWER winstats than the baked blob.\n") +
                        "\nDownload it and continue the build?",
                        "Download & Continue",        // 0 (ok)
                        "Cancel Build",               // 1 (cancel)
                        "Continue WITHOUT download"); // 2 (alt)

                    if (choice == 0)
                    {
                        try { SaveBlob(fresh); return; }
                        catch (Exception e) { failure = e.Message; continue; }   // re-show as a failure
                    }
                    if (choice == 2) return;
                    throw new BuildFailedException("[WinStats] build cancelled by user.");
                }
                else
                {
                    int choice = EditorUtility.DisplayDialogComplex(
                        "WinStats download FAILED",
                        "Could not fetch winstats from the server:\n\n" + failure +
                        "\n\nTry the download again?",
                        "Retry Download",             // 0 (ok)
                        "Cancel Build",               // 1 (cancel)
                        "Continue WITHOUT download"); // 2 (alt)

                    if (choice == 0) { failure = null; continue; }
                    if (choice == 2) return;
                    throw new BuildFailedException("[WinStats] build cancelled by user.");
                }
            }
        }

        // ---- fetch -------------------------------------------------------------------

        static byte[] FetchBlob()
        {
            EditorUtility.DisplayProgressBar("WinStats", "Collecting level hashes...", 0.1f);
            var levels = CollectAllLevels();
            string setHash = LevelLoader.LevelSetsHash;

            EditorUtility.DisplayProgressBar("WinStats", $"Fetching blob for {levels.Count} levels...", 0.5f);
            byte[] blob = Fetch(setHash, levels, out string counts);
            EditorUtility.ClearProgressBar();

            Debug.Log($"[WinStats] set {setHash}: {levels.Count} unique levels, {counts}, blob {blob.Length / 1024f:0.0} KB");
            return blob;
        }

        static void SaveBlob(byte[] blob)
        {
            File.WriteAllBytes(BlobPath, blob);
            AssetDatabase.ImportAsset(BlobPath);
        }

        // hash -> weight for every unique board in the build (identical boards dedup by content).
        static SortedDictionary<int, int> CollectAllLevels()
        {
            if (!Directory.Exists(LevelsFolder))
                throw new Exception($"Levels folder not found: {LevelsFolder} — run the level exports first.");

            var levels = new SortedDictionary<int, int>();
            var files = Directory.GetFiles(LevelsFolder, "*.bytes");
            if (files.Length == 0)
                throw new Exception($"No level packs in {LevelsFolder} — run the level exports first.");

            int decoded = 0;
            foreach (var file in files)
            {
                byte[] plain;
                try { plain = LevelPack.Decrypt(File.ReadAllBytes(file)); }
                catch (Exception e) { throw new Exception($"Pack unreadable: {Path.GetFileName(file)} ({e.Message})"); }

                int count = LevelPack.Count(plain);
                decoded += count;
                for (int i = 0; i < count; i++)
                {
                    var level = LevelPack.Decode(plain, i);
                    levels[level.ContentHash()] = level.weight;
                }
            }

            Debug.Log($"[WinStats] decoded {decoded} levels across {files.Length} packs -> {levels.Count} unique boards ({decoded - levels.Count} duplicates)");
            return levels;
        }

        [Serializable] class BuildRequest { public string level_sets_hash; public int[] hashes; public int[] weights; }

        static byte[] Fetch(string setHash, SortedDictionary<int, int> levels, out string counts)
        {
            var hashes = new int[levels.Count];
            var weights = new int[levels.Count];
            int n = 0;
            foreach (var kv in levels) { hashes[n] = kv.Key; weights[n] = kv.Value; n++; }

            string body = JsonUtility.ToJson(new BuildRequest { level_sets_hash = setHash, hashes = hashes, weights = weights });

            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
            using (var content = new StringContent(body, Encoding.UTF8, "application/json"))
            {
                content.Headers.Add("X-Admin-Key", AdminKey);
                content.Headers.Add("X-Db-Name", DbName);

                HttpResponseMessage http;
                try { http = client.PostAsync(ServerUrl, content).Result; }
                catch (Exception e) { throw new Exception($"Server unreachable: {ServerUrl}\n{Innermost(e).Message}"); }

                if (!http.IsSuccessStatusCode)
                    throw new Exception($"Server returned {(int)http.StatusCode}: {http.Content.ReadAsStringAsync().Result}");

                byte[] blob = http.Content.ReadAsByteArrayAsync().Result;

                // Deep validation with the game's OWN reader (structure, every record, values)
                // — what gets baked is guaranteed readable by exactly the code that will read it.
                string invalid = WinStats.ValidateBlob(blob);
                if (invalid != null)
                    throw new Exception($"Server blob failed validation: {invalid}");

                string H(string name) => http.Headers.TryGetValues(name, out var v) ? v.First() : "?";
                counts = $"{H("X-Winstats-Matched")} with data, {H("X-Winstats-Qualified")} qualified";
                return blob;
            }
        }

        static Exception Innermost(Exception e) { while (e.InnerException != null) e = e.InnerException; return e; }
    }
}
