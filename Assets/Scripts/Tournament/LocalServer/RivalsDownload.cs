using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Keeps the rivals file current on a phone that was installed months ago.
    ///
    /// The server redraws its recordings every day, and without this that only ever reached the
    /// next BUILD — the whole point of having a server rather than baking a file would be lost on
    /// everyone who already has the app.
    ///
    /// Its entire job is to REPLACE ONE FILE, the one <see cref="RivalsBlob.FilePath"/> names. It
    /// hands nobody any bytes and decides nothing about what is in use: the blob picks its source
    /// at launch, so a download that lands now takes effect at the next one. That is also the
    /// safer answer, since a running tournament must not have its opponents swapped underneath it.
    ///
    /// The new bytes go to a temp file and are only moved into place once they have been read
    /// successfully — a half-written file is never one the game can find.
    ///
    /// Nothing here blocks the boot and nothing here can break the tournament: every failure just
    /// leaves the file that is already there, or the baked copy if there is none. The ETag means
    /// the usual answer is 304 and no bytes move at all.
    /// </summary>
    public static class RivalsDownload {

        const string Url = "https://pawdoku-winstats-server-production.up.railway.app/tournament/rivals";

        const string TempSuffix = ".tmp";
        const string EtagSuffix = ".etag";

        /// <summary>A launch gets this many tries, then waits for the next one. The file changes
        /// once a day — there is nothing here worth chasing harder than that.</summary>
        const int Attempts = 3;
        const int FirstRetrySeconds = 5;

        static string _path;   // RivalsBlob.FilePath, read on the main thread and kept

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void _boot() {
            _path = RivalsBlob.FilePath;   // persistentDataPath is a Unity API — not off-thread

            // Started on the main thread and left there: every continuation below comes back to
            // the player loop, so the file is written and read from ONE thread and there is no
            // window where it half-exists. The download itself is async I/O and occupies no
            // thread while it waits.
            //
            // The one exception is the validation — see _tryOnce.
            _ = _refresh();
        }

        /// <summary>Runs on the main thread — see <see cref="_boot"/>.</summary>
        static async Task _refresh() {
            for (int attempt = 1; attempt <= Attempts; attempt++) {
                // A Task outlives play mode: leaving it in the editor tears the domain down while
                // this is still waiting, and whatever came next would run against a half-dead
                // engine. On device this is simply always true.
                if (!Application.isPlaying) return;

                if (await _tryOnce()) return;
                if (attempt < Attempts)
                    await Task.Delay(FirstRetrySeconds * 1000 * attempt);   // 5s, then 10s
            }
        }

        /// <summary>One attempt. True when there is nothing more to do — either the server said
        /// 304, or a new file is in place.</summary>
        static async Task<bool> _tryOnce() {
            try {
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) }) {
                    string etag = _readEtag();
                    if (!string.IsNullOrEmpty(etag))
                        http.DefaultRequestHeaders.TryAddWithoutValidation("If-None-Match", etag);

                    var response = await http.GetAsync(Url);

                    // The common case: the file on disk is what the server has.
                    if (response.StatusCode == HttpStatusCode.NotModified) return true;

                    // 503 is the server building its first recordings. Worth another try.
                    if (!response.IsSuccessStatusCode) {
                        Debug.LogWarning($"[RivalsDownload] server returned {(int)response.StatusCode}");
                        return false;
                    }

                    byte[] bytes = await response.Content.ReadAsByteArrayAsync();

                    // Checked with the game's own reader before it replaces anything — a file that
                    // cannot be read must never become the one the game finds first.
                    //
                    // The ONE thing here that leaves the main thread: it walks every index entry
                    // and all ~120,000 recorded wins, which is milliseconds of frame time, and it
                    // is pure arithmetic over a byte array — no file, no Unity API, nothing to
                    // race with. The continuation lands back on the player loop, so everything
                    // that touches the file still happens on one thread.
                    string invalid = await Task.Run(() => RivalsBlob.Validate(bytes));
                    if (invalid != null) {
                        Debug.LogError("[RivalsDownload] server blob rejected: " + invalid);
                        return true;   // not a network problem; retrying would fetch the same bytes
                    }

                    _install(bytes, response.Headers.ETag?.Tag);
                    return true;
                }
            } catch (Exception e) {
                // No connection yet, airplane mode, DNS — ordinary states, not defects.
                Debug.LogWarning("[RivalsDownload] fetch failed: " + e.Message);
                return false;
            }
        }

        /// <summary>Write beside the real file, then move over it. The move is the moment the new
        /// version exists; before it, the old one is still whole.</summary>
        static void _install(byte[] bytes, string etag) {
            string temp = _path + TempSuffix;
            try {
                File.WriteAllBytes(temp, bytes);

                if (File.Exists(_path)) File.Delete(_path);
                File.Move(temp, _path);

                // Only now, and only together: an ETag without its bytes would have us claim a
                // version we do not hold, and the server would answer 304 to a file we never got.
                _writeEtag(etag);
                Debug.Log($"[RivalsDownload] replaced the rivals file, {bytes.Length / 1024} KB — in use next launch");
            } catch (Exception e) {
                Debug.LogWarning("[RivalsDownload] could not replace the rivals file: " + e.Message);
                try { if (File.Exists(temp)) File.Delete(temp); } catch { /* nothing to do */ }
            }
        }

        static string _readEtag() {
            try {
                string path = _path + EtagSuffix;
                return File.Exists(path) ? File.ReadAllText(path) : null;
            } catch { return null; }
        }

        static void _writeEtag(string etag) {
            string path = _path + EtagSuffix;
            try {
                if (string.IsNullOrEmpty(etag)) { if (File.Exists(path)) File.Delete(path); }
                else File.WriteAllText(path, etag);
            } catch {
                // Losing the ETag only costs a full download next launch.
                try { if (File.Exists(path)) File.Delete(path); } catch { /* nothing to do */ }
            }
        }
    }
}
