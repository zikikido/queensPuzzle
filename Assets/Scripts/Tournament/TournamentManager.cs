using System;
using System.Threading.Tasks;
using Common;
using UnityEngine;

namespace qp {

    /// <summary>Where the tournament stands for THIS player — drives the lobby card.</summary>
    /// <summary>Set by the server's answer, not by the local clock: a clock that runs fast only
    /// makes the timer hit zero early, never changes the state.</summary>
    public enum ETournamentStatus {
        Locked,       // campaign hasn't reached the unlock level yet
        Offline,      // no internet — wins are kept and sent later
        NotJoined,    // tournament running, no win in it yet
        Active,       // joined — timer + rank
        EndingSoon,   // joined, less than EndingSoonTime left
        Calculating,  // the local timer hit zero — waiting for the next sync to bring the result
        Ended,        // the server closed a tournament the player played — Claim Prize / See Results
    }

    /// <summary>
    /// Everything Tournament in one place — API only, for review. Logic comes next.
    ///
    /// Shape: local first. A win counts at once and is queued; <see cref="Sync"/> (background,
    /// every ~30s and on resume) sends the queue and brings back the server's picture. The only
    /// thing that must come from the server is the final ranking; the prize for it is granted here.
    ///
    /// Persisted: <see cref="TournamentState"/> — two snapshots (current, ended) + unsent wins.
    /// Everything below is derived on read, nothing is stored twice.
    /// Server: <see cref="Backend"/>. Time: <see cref="Now"/>.
    /// </summary>
    public static class TournamentManager {

        // ---- config ---------------------------------------------------------------------

        /// <summary>Unlock level, "ending soon" threshold and prize places. Loaded once at boot on
        /// the main thread (see <see cref="TournamentConfig"/>); null = the feature is off.</summary>
        public static TournamentConfig Config => TournamentConfig.Instance;

        /// <summary>The server. Swapped for the real one later; nothing else changes.</summary>
        public static ITournamentBackend Backend = new MockTournamentBackend(UserID.GetUserIDLocal());

        // ---- time -----------------------------------------------------------------------

        /// <summary>Server UTC (+ the debug offset) — the clock the whole feature runs on.</summary>
        public static DateTime Now => throw new NotImplementedException();

        // ---- what the UI reads ----------------------------------------------------------

        /// <summary>The tournament running now (id, window, group size).</summary>
        public static TournamentInfo Info => throw new NotImplementedException();

        /// <summary>Its table, as the server last sent it.</summary>
        public static TournamentStandings Standings => throw new NotImplementedException();

        /// <summary>The closed tournament waiting for its popup (Exists = something to show).</summary>
        public static TournamentSnapshot Ended => throw new NotImplementedException();

        public static bool HasEnded => throw new NotImplementedException();

        /// <summary>The player's final place there (1-based; 0 = nothing waiting).</summary>
        public static int EndedRank => throw new NotImplementedException();

        /// <summary>Did that place win a prize — our rule, not the server's.</summary>
        public static bool EndedWon => throw new NotImplementedException();

        // Inputs to Status, not API: unlocked = campaign reached Config.unlockLevel; online =
        // network AND a trusted clock (MBServerTimeManagerV2.IsTimeSynced) — without server time
        // nothing here can be trusted, so the card goes Offline and wins simply wait.
        static bool IsUnlocked => throw new NotImplementedException();
        static bool IsOnline => throw new NotImplementedException();

        /// <summary>Countdown to the end of the current tournament (zero when it is over).</summary>
        public static TimeSpan TimeLeft => throw new NotImplementedException();

        /// <summary>The player's score in the current tournament: server-confirmed + unsent wins.</summary>
        public static int MyScore => throw new NotImplementedException();

        /// <summary>At least one win in the current tournament.</summary>
        public static bool Joined => throw new NotImplementedException();

        /// <summary>The player's row in the table, re-evaluated with the local score so an unsent
        /// win already moves the player up. 0-based; -1 = not joined. Rank shown = this + 1.</summary>
        public static int MyIndex => throw new NotImplementedException();

        /// <summary>The one value the lobby card switches on.</summary>
        public static ETournamentStatus Status => throw new NotImplementedException();

        /// <summary>Wins still waiting to be sent — the Offline card shows this.</summary>
        public static int PendingCount => throw new NotImplementedException();

        public static bool LastSyncOk => throw new NotImplementedException();
        public static DateTime LastSyncUtc => throw new NotImplementedException();

        // ---- what the game calls ---------------------------------------------------------

        /// <summary>A campaign level was won with <paramref name="score"/> left. Counted locally at
        /// once and queued; which tournament it lands in is the server's call (the one running when
        /// it arrives). Called from the win flow, before the win popup.</summary>
        public static void OnLevelWin(int score) => throw new NotImplementedException();

        /// <summary>Background sync: send the queued wins, take back the current snapshot and —
        /// while nothing is waiting for the Ended popup — the last closed tournament too.
        /// Safe to call any time: overlapping calls are skipped and a failure just retries later.</summary>
        public static Task Sync() => throw new NotImplementedException();

        /// <summary>The Tournament Ended popup's button (Claim / Continue). The rank was already
        /// confirmed during sync, so the prize is granted here and nothing is sent. Returns true
        /// when the ended tournament was cleared.</summary>
        public static bool CompleteEnded() => throw new NotImplementedException();

        // ---- debug (MBDebugWin) ----------------------------------------------------------

        public static void DebugWin(int score) => throw new NotImplementedException();

        /// <summary>Move the tournament clock so the current one ends in <paramref name="left"/>
        /// (negative = already over).</summary>
        public static void DebugSetTimeLeft(TimeSpan left) => throw new NotImplementedException();

        /// <summary>Wipe the client blob, the mock server and the clock offset.</summary>
        public static void DebugResetAll() => throw new NotImplementedException();

        /// <summary>Dump the current table + the ended result to the console.</summary>
        public static void DebugLogTable() => throw new NotImplementedException();
    }
}
