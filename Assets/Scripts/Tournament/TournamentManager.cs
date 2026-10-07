using System;
using System.Threading.Tasks;
using Common;
using UnityEngine;

namespace qp {

    /// <summary>Where the tournament stands for THIS player — the one value the lobby card switches
    /// on. Set by the server's answer, not by the local clock, so a clock that runs fast only makes
    /// the timer hit zero early and never changes the state.</summary>
    public enum ETournamentStatus {
        None,         // no tournament running at all (server says so) — the card is hidden
        Locked,       // campaign hasn't reached the unlock level yet
        Offline,      // no internet / no trusted clock — wins are kept and sent later
        NotJoined,    // tournament running, no win in it yet
        Active,       // joined — timer + rank
        EndingSoon,   // joined, less than the config's threshold left
        Calculating,  // the local timer hit zero — waiting for the next sync to bring the result
        Ended,        // a tournament the player played is closed — Claim Prize / See Results
    }

    /// <summary>
    /// Everything Tournament in one place — API only, for review. Logic comes next.
    ///
    /// Shape: local first. A win counts at once and is queued; <see cref="Sync"/> (background, every
    /// ~30s and on resume) sends the queue and takes back the server's picture. The only thing that
    /// must come from the server is the final ranking; the prize for it is granted here.
    ///
    /// What the UI reads is <see cref="State"/> — the snapshots exactly as the last sync returned
    /// them — plus the four values below, which can't live there because they need the clock, the
    /// config and the connection. Everything else (time, config, backend, sync bookkeeping) is
    /// internal.
    /// </summary>
    public static class TournamentManager {

        static TournamentState _state;
        static TournamentSyncer _syncer;

        /// <summary>Boot (MBStartup). Loads the blob, builds the syncer and starts the background
        /// runner. Main thread, instant: it never waits on the network — the runner holds its first
        /// sync until server time is trusted. Calling it twice does nothing.</summary>
        public static void Init() {
            if (_state != null) return;
            _state = TournamentState.Load();
            _syncer = new TournamentSyncer(new LocalTournamentBackend(UserID.GetUserIDLocal()), _state);
            MBTournamentSyncRunner.Create(_syncer);
        }

        /// <summary>The blob: the tournament running now + the last closed one, and the wins not
        /// sent yet. Written only here — the UI reads.</summary>
        public static TournamentState State => _state;

        // ---- derived: needs the clock / config / connection, so it can't sit in State ----

        /// <summary>
        /// What the lobby card shows. Read top to bottom — the first line that matches wins:
        /// no feature at all, then the lock, then no connection (nothing below it can be trusted),
        /// then a result waiting to be seen, and only then the running tournament.
        /// </summary>
        public static ETournamentStatus Status {
            get {
                if (TournamentConfig.Instance == null || _state == null) return ETournamentStatus.None;
                if (!_isUnlocked) return ETournamentStatus.Locked;
                if (!_isOnline) return ETournamentStatus.Offline;

                // A closed tournament the player hasn't been shown yet — Claim Prize / See Results.
                // The row check is a belt: the server only sends one the player actually played,
                // and a popup with no rank in it would say nothing.
                if (_state.lastSyncClosed.Exists && !_state.closedShown
                    && _state.lastSyncClosed.standings.myIndex >= 0) return ETournamentStatus.Ended;

                if (!_state.lastSyncCurrent.Exists) return ETournamentStatus.None;

                // The timer ran out. "Calculating" only if there is really a result on the way —
                // the player scored in this one. Otherwise (never joined, or a blob left over from
                // weeks ago) there is nothing to wait for, so the card stays hidden until the sync
                // brings the tournament running now.
                if (TimeLeft <= TimeSpan.Zero)
                    return _state.ConfirmedScore > 0 ? ETournamentStatus.Calculating
                                                     : ETournamentStatus.None;

                if (MyScoreWithPending <= 0) return ETournamentStatus.NotJoined;
                return TimeLeft < TournamentConfig.Instance.EndingSoonTime
                    ? ETournamentStatus.EndingSoon
                    : ETournamentStatus.Active;
            }
        }

        /// <summary>Countdown to the end of the current tournament (zero once it is over).</summary>
        public static TimeSpan TimeLeft {
            get {
                if (_state == null || !_state.lastSyncCurrent.Exists) return TimeSpan.Zero;
                var left = _state.lastSyncCurrent.info.EndUtc - MBServerTimeManagerV2.UTCNow;
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }

        /// <summary>The player's score in the current tournament: what the server confirmed plus
        /// the wins still waiting to be sent. With an empty queue this is exactly the score in
        /// <see cref="State"/>; it differs only between a win and its sync, or while offline.
        /// A pending win belongs to the tournament it will arrive at, so once this one is over it
        /// already belongs to the next — counting it here would inflate a table that is closing.</summary>
        public static int MyScoreWithPending =>
            _state == null ? 0
            : _state.ConfirmedScore + (TimeLeft > TimeSpan.Zero ? _state.PendingScore : 0);

        /// <summary>The player's row in the table, re-evaluated with that score — so a win that
        /// hasn't been sent yet already moves the player up, before the server has seen it.
        /// 0-based; -1 = not in the table. Ties keep the player below whoever already has that
        /// score, which is what the server will say too once the win lands.</summary>
        public static int MyIndexWithPending {
            get {
                int mine = MyScoreWithPending;
                if (mine <= 0) return -1;

                var st = _state.lastSyncCurrent.standings;
                if (st.entries == null) return 0;   // joined, table not synced yet

                int index = 0;
                for (int i = 0; i < st.entries.Length; i++)
                    if (!st.IsMe(i) && st.entries[i].score >= mine) index++;
                return index;
            }
        }

        // ---- what the game calls ---------------------------------------------------------

        /// <summary>A campaign level was won with <paramref name="score"/> left. Counted locally at
        /// once and queued; which tournament it lands in is the server's call (the one running when
        /// it arrives). Called from the win flow, before the win popup.
        /// Each win gets its own id, so it can be sent again freely until the server confirms it.
        /// The queue holds at most <see cref="TournamentConfig.maxPendingWins"/> wins; past that the
        /// oldest are dropped — they belong to a tournament that has closed anyway.
        /// Returns what the climb popup needs: where the player was, where they are now, and
        /// whether there is anything to animate at all.</summary>
        public static TournamentWinResult OnLevelWin(int score) {
            var res = new TournamentWinResult { fromIndex = -1, toIndex = -1 };
            if (_state == null || TournamentConfig.Instance == null || !_isUnlocked || score <= 0) return res;

            res.fromIndex = MyIndexWithPending;
            bool wasIn = res.fromIndex >= 0;

            _state.pending.Add(new TournamentWin {
                id = Guid.NewGuid().ToString("N"),
                score = score,
                wonAtTicks = MBServerTimeManagerV2.UTCNow.Ticks,
            });

            // Oldest first: a win that old belongs to a tournament that closed long ago anyway.
            while (_state.pending.Count > TournamentConfig.Instance.maxPendingWins) _state.pending.RemoveAt(0);

            _state.Save();

            res.newScore = MyScoreWithPending;
            res.toIndex = MyIndexWithPending;
            // Nothing to animate when the tournament isn't running: the win is queued all the same
            // and counts for the one it reaches (see MyScoreWithPending).
            res.scoreAdded = res.toIndex >= 0 ? score : 0;
            res.joined = !wasIn && res.toIndex >= 0;

            _ = Sync();
            return res;
        }

        /// <summary>Sync now. Scheduling lives in <see cref="MBTournamentSyncRunner"/>, so this
        /// also pushes the next scheduled sync away.</summary>
        public static Task Sync() => MBTournamentSyncRunner.RunNow();

        /// <summary>The Tournament Ended popup's button (Claim / Continue). The rank was already
        /// confirmed during sync, so the prize is granted here and nothing is sent. Marks the result
        /// as shown, so it never pops again — the server keeps returning that closed tournament.</summary>
        public static void CompleteEnded() {
            if (_state == null || !_state.lastSyncClosed.Exists || _state.closedShown) return;

            // TODO (step 5): grant the prize for _state.lastSyncClosed.standings.myIndex + 1
            //                (TournamentConfig.Instance.prizePlaces decides who wins).

            _state.closedShown = true;
            _state.Save();
        }


        // ---- internal --------------------------------------------------------------------

        static bool _isUnlocked => AppData.LevelIdx.Value + 1 >= TournamentConfig.Instance.unlockLevel;

        /// <summary>A confirmed connection AND a trusted clock. Reachability alone lies — a captive
        /// wifi "has network" — so this is the checked one. And without server time the timer and
        /// "is it over" would run on a clock the player can set, so the card goes Offline instead.</summary>
        static bool _isOnline => InternetConnection.Instance != null
                                && InternetConnection.Instance.HasInternet
                                && MBServerTimeManagerV2.IsTimeSynced;
    }
}
