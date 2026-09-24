using System;
using System.Threading.Tasks;

namespace qp {

    /// <summary>
    /// Local stand-in for the tournament server, so the feature can be built and tested before a
    /// real one exists: fixed 48h windows, one group of bots per tournament that gain score over
    /// time, packages applied once per batch id, etags, and debug knobs for a slow or dead server.
    /// Empty until the design is settled — the earlier draft is kept outside the project.
    /// </summary>
    public class MockTournamentBackend : ITournamentBackend {

        readonly string _playerId;   // who is asking — like the header/token a real backend would send

        public MockTournamentBackend(string playerId) => _playerId = playerId;

        public Task<TournamentSyncResult> Sync(TournamentSyncRequest req) => throw new NotImplementedException();
    }
}
