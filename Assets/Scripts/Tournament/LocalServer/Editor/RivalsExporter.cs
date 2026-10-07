using System;
using System.Net.Http;
using UnityEditor;
using UnityEngine;
using qp;

namespace QueensPuzzle
{
    /// <summary>
    /// Rivals blob: recorded 48-hour play histories baked into the build
    /// (Resources/rivals.bytes), which the tournament picks its opponents from.
    ///
    /// The SERVER packs the binary — see pawdoku-winstats-server/src/tournament/pack.ts for
    /// the layout, and <see cref="RivalsBlob"/> for the reader that must match it. This
    /// exporter only downloads it and bakes it.
    ///
    /// What is baked is the FLOOR, not the file: the game refreshes it from the same endpoint
    /// at runtime. It matters anyway — a first launch, a player with no connection, and a
    /// server outage all fall back to exactly these bytes.
    ///
    /// Two entry points:
    ///  - Manual: QueensPuzzle → Export Rivals — fetch from the server and bake.
    ///  - EVERY BUILD, through <see cref="BakedBlobsBuildCheck"/>, which asks about this and
    ///    winstats together rather than once each.
    /// </summary>
    public static class RivalsExporter
    {
        const string ServerUrl = "https://pawdoku-winstats-server-production.up.railway.app/tournament/rivals";

        const string BlobPath = "Assets/Reskin/Resources/rivals.bytes";

        /// <summary>This blob for the pre-build check: what to call it, where it lives, how to
        /// fetch it. Everything else about a baked blob is the same for all of them.</summary>
        public static BakedBlob Blob => new BakedBlob("Rivals", BlobPath, FetchBlob);

        // ---- manual export -----------------------------------------------------------

        [MenuItem("QueensPuzzle/Export Rivals (fetch from server)")]
        public static void ExportManual()
        {
            try
            {
                byte[] blob = FetchBlob();
                Blob.Save(blob);
                EditorUtility.DisplayDialog("Rivals export OK",
                    $"Blob: {blob.Length / 1024f:0.0} KB -> {BlobPath}", "OK");
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Rivals export FAILED",
                    "The rivals blob was NOT updated!\n\n" + Innermost(e).Message, "OK");
                Debug.LogError("[Rivals] export failed: " + e);
            }
        }

        // ---- fetch -------------------------------------------------------------------

        static byte[] FetchBlob()
        {
            EditorUtility.DisplayProgressBar("Rivals", "Fetching recordings...", 0.5f);
            try
            {
                byte[] blob = Fetch();
                Debug.Log($"[Rivals] {RecordingCount(blob)} recordings, blob {blob.Length / 1024f:0.0} KB");
                return blob;
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        static byte[] Fetch()
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
            {
                HttpResponseMessage http;
                try { http = client.GetAsync(ServerUrl).Result; }
                catch (Exception e) { throw new Exception($"Server unreachable: {ServerUrl}\n{Innermost(e).Message}"); }

                // 503 is the server's honest answer while its first build is still running —
                // say so, rather than leaving "the download failed" to be guessed at.
                if (http.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                    throw new Exception("The server has not built its recordings yet — try again in a minute.");

                if (!http.IsSuccessStatusCode)
                    throw new Exception($"Server returned {(int)http.StatusCode}: {http.Content.ReadAsStringAsync().Result}");

                byte[] blob = http.Content.ReadAsByteArrayAsync().Result;

                // Deep validation with the game's OWN reader (header, every entry, every win)
                // — what gets baked is guaranteed readable by exactly the code that will read it.
                string invalid = RivalsBlob.Validate(blob);
                if (invalid != null)
                    throw new Exception($"Server blob failed validation: {invalid}");

                return blob;
            }
        }

        /// <summary>recordCount from the header — the blob is already validated by here.</summary>
        static int RecordingCount(byte[] b) => b[8] | (b[9] << 8) | (b[10] << 16) | (b[11] << 24);

        static Exception Innermost(Exception e) { while (e.InnerException != null) e = e.InnerException; return e; }
    }
}
