using System;
using System.Collections.Generic;
using Common;

namespace qp {

    /// <summary>
    /// The client's tournament blob (PlayerPrefs JSON). Holds only what can't be derived: the two
    /// snapshots the server sent (the tournament running now, and the last closed one still waiting
    /// for its popup) and the wins not sent yet. The player's score / index / joined are derived
    /// from these (see <see cref="TournamentManager"/>). Mutated only by TournamentManager.
    /// Note: JsonUtility never stores null for nested classes — "none" is a snapshot with no id.
    /// </summary>
    [Serializable]
    public class TournamentState {

        static readonly PlayerPrefsHelper.ObjectHolder<TournamentState> _holder
            = new PlayerPrefsHelper.ObjectHolder<TournamentState>("qp_tournament_state");

        /// <summary>The tournament running now + the player's table in it.</summary>
        public TournamentSnapshot current = new TournamentSnapshot();

        /// <summary>The last closed tournament the player took part in, with its final table —
        /// waiting for the Tournament Ended popup. Stored only once the server confirmed it.</summary>
        public TournamentSnapshot ended = new TournamentSnapshot();

        /// <summary>Wins not sent yet (offline / server down).</summary>
        public List<TournamentWin> pending = new List<TournamentWin>();

        // The package currently being sent: fixed when the send starts, so every retry repeats the
        // exact same wins under the same id. Wins added meanwhile simply go in the next package.
        public string batchId = "";
        public int batchCount;

        public static TournamentState Load() => _holder.Value ?? new TournamentState();

        public void Save() => _holder.Save(this);

        public static void Delete() => _holder.Delete();

        // ---- reading the blob ---------------------------------------------------------

        /// <summary>The player's score as the server confirmed it in the current tournament.</summary>
        public int ConfirmedScore => current.standings.Me?.score ?? 0;

        /// <summary>Score of the wins not sent yet.</summary>
        public int PendingScore {
            get {
                int sum = 0;
                foreach (var w in pending) sum += w.score;
                return sum;
            }
        }
    }
}
