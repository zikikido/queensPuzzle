using System;
using System.Threading.Tasks;

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
    ///   - etags go out as they came in; a snapshot with no payload means "still the same as yours"
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
        public async Task Run() => throw new NotImplementedException();

        // Build the request: the locked package of wins + the etags of what we already hold.
        TournamentSyncRequest _request() => throw new NotImplementedException();

        // Fold the answer in: drop the wins that arrived, replace the snapshots that came with
        // content, reset "already shown" when the closed tournament is a different one.
        void _apply(TournamentSyncResult res) => throw new NotImplementedException();
    }
}
