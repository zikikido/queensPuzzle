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
    ///   - every sync sends every win the client still holds; each carries its own id, so the
    ///     server applies it once and a resend is free — nothing is frozen, and wins made while a
    ///     request is in flight simply ride along on the next one
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
                var res = await _backend.Sync(_request());
                _apply(res);
                LastOk = true;
                LastUtc = MBServerTimeManagerV2.UTCNow;   // server time, like everything else here
            } catch (Exception e) {
                LastOk = false;
                CDebug.CrashLog($"[TournamentSyncer] sync failed, will retry: {e.Message}");
            } finally {
                Running = false;
            }
        }

        // Build the request: every win we still hold + the etags of what we already have.
        TournamentSyncRequest _request() => new TournamentSyncRequest {
            wins = _state.pending.ToArray(),
            currentEtag = _state.lastSyncCurrent.etag,
            closedEtag = _state.lastSyncClosed.etag,
        };

        // Fold the answer in: drop the wins the server now holds, replace the snapshots that came
        // with content, reset "already shown" when the closed tournament is a different one.
        void _apply(TournamentSyncResult res) {
            // By id, never by position — wins made while the request was in flight stay put.
            foreach (var id in res.acceptedWinIds)
                _state.pending.RemoveAll(w => w.id == id);

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
