using Common;
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace qp {

    /// <summary>
    /// The one place that knows what a sync looks like: it builds the request out of the client
    /// blob, calls the server, and folds the answer back into that same blob. Nothing else in the
    /// game touches <see cref="ITournamentBackend"/>, and <see cref="TournamentManager"/> never
    /// sees a request or a response — it just calls <see cref="Run"/>.
    ///
    /// The rules that live here:
    ///   - the package of unsent wins is fixed when the first attempt starts, so a retry repeats
    ///     exactly the same wins under the same id and can never count twice
    ///   - etags go out as they came in, and every answer carries one — including "there is no
    ///     tournament", which has its own etag. Same etag = keep what we hold; a different etag
    ///     with no payload = there really is nothing, so drop it
    ///   - a closed tournament with a different id than the one on screen resets "already shown"
    ///   - a failure changes nothing: the wins stay queued and the next run tries again
    /// </summary>
    public class TournamentSyncer {

        readonly ITournamentBackend _backend;
        readonly TournamentState _state;

        public TournamentSyncer(ITournamentBackend backend, TournamentState state) {
            _backend = backend;
            _state = state;
        }

        /// <summary>Did the last run reach the server.</summary>
        public bool LastOk { get; private set; }

        /// <summary>When the last successful run came back (UTC).</summary>
        public DateTime LastUtc { get; private set; }

        /// <summary>A run is in flight — a second call would only duplicate work.</summary>
        public bool Running { get; private set; }

        /// <summary>One sync. Safe to call any time: overlapping calls are skipped, and any failure
        /// leaves the blob exactly as it was.</summary>
        public async Task Run() {
            if (Running) return;
            Running = true;
            try {
                var req = _request();
                var res = await _backend.Sync(req);
                _apply(req, res);
                LastOk = true;
                LastUtc = MBServerTimeManagerV2.UTCNow;   // server time, like everything else here
            } catch (Exception e) {
                LastOk = false;
                CDebug.CrashLog($"[TournamentSyncer] sync failed, will retry: {e.Message}");
            } finally {
                Running = false;
            }
        }

        // Build the request: the locked package of wins + the etags of what we already hold.
        TournamentSyncRequest _request() {
            var req = new TournamentSyncRequest {
                currentEtag = _state.lastSyncCurrent.etag,
                closedEtag = _state.lastSyncClosed.etag,
            };

            if (_state.pending.Count > 0) {
                // First attempt for this package: lock what goes in it. Anything won from here on
                // waits for the next package, so a retry can't grow (and double-count) this one.
                if (string.IsNullOrEmpty(_state.batchId)) {
                    _state.batchId = Guid.NewGuid().ToString("N");
                    _state.batchCount = _state.pending.Count;
                    _state.Save();
                }
                req.batchId = _state.batchId;
                req.wins = _state.pending.GetRange(0, _state.batchCount).ToArray();
            }
            return req;
        }

        // Fold the answer in: drop the wins that arrived, replace the snapshots that came with
        // content, reset "already shown" when the closed tournament is a different one.
        void _apply(TournamentSyncRequest req, TournamentSyncResult res) {
            if (!string.IsNullOrEmpty(req.batchId)) {
                _state.pending.RemoveRange(0, _state.batchCount);   // the server has them now
                _state.batchId = "";
                _state.batchCount = 0;
            }

            // Same etag = unchanged, keep what we hold. A different etag is the truth, whatever it
            // is: a new table, or nothing at all (no tournament running / none played yet).
            if (res.current.etag != _state.lastSyncCurrent.etag)
                _state.lastSyncCurrent = res.current;

            if (res.lastClosed.etag != _state.lastSyncClosed.etag) {
                bool isAnotherOne = res.lastClosed.info.id != _state.lastSyncClosed.info.id;
                _state.lastSyncClosed = res.lastClosed;
                // Only a real result is something the player hasn't seen — "there is none" isn't.
                if (isAnotherOne && res.lastClosed.Exists) _state.closedShown = false;
            }

            _state.Save();
        }
    }
}
