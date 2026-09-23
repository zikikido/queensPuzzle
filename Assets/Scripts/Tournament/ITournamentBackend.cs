using System.Threading.Tasks;

namespace qp {

    /// <summary>
    /// The tournament server as the client sees it — three calls. <see cref="MockTournamentBackend"/>
    /// fakes it locally (bots, delays, failures); a real HTTP backend replaces it later with no
    /// change to <see cref="TournamentManager"/>.
    /// Who is asking is part of the connection, not of the calls: every implementation is built with
    /// the player's identity (later a token) and sends it itself — a server must never take the
    /// player id from the request body.
    /// Any failure (no network, server down, timeout) is thrown as an exception; the manager
    /// keeps the work pending and retries on the next sync.
    /// </summary>
    public interface ITournamentBackend {

        /// <summary>The one call. The server applies the reported wins (if any), then answers with
        /// the tournament running now and the player's table in it — one consistent picture, and
        /// one round trip. Wins count for the tournament running when they arrive, so a package
        /// sent after end time belongs to the next tournament; the first win puts the player in a
        /// group; a batchId already applied is ignored, so a retry is just a refresh.
        /// With <see cref="TournamentSyncRequest.includeLastClosed"/> the answer also carries the
        /// last closed tournament the player took part in — the final ranking, which is the only
        /// thing the server has to confirm (the prize for it is granted locally).</summary>
        Task<TournamentSyncResult> Sync(TournamentSyncRequest req);
    }
}
