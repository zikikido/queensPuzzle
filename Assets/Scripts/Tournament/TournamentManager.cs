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

        /// <summary>Boot (MBStartup, right after the server-time stage — the feature needs a
        /// trusted clock). Loads the blob, builds the syncer and starts the background sync.
        /// Main thread, instant: it never waits on the network.</summary>
        public static void Init() => throw new NotImplementedException();

        /// <summary>The blob: the tournament running now + the last closed one, and the wins not
        /// sent yet. Written only here — the UI reads.</summary>
        public static TournamentState State => throw new NotImplementedException();

        // ---- derived: needs the clock / config / connection, so it can't sit in State ----

        public static ETournamentStatus Status => throw new NotImplementedException();

        /// <summary>Countdown to the end of the current tournament (zero once it is over).</summary>
        public static TimeSpan TimeLeft => throw new NotImplementedException();

        /// <summary>The player's score in the current tournament: what the server confirmed plus
        /// the wins still waiting to be sent. With an empty queue this is exactly the score in
        /// <see cref="State"/>; it differs only between a win and its sync, or while offline.</summary>
        public static int MyScoreWithPending => throw new NotImplementedException();

        /// <summary>The player's row in the table, re-evaluated with that score — so a win that
        /// hasn't been sent yet already moves the player up. 0-based; -1 = not joined.</summary>
        public static int MyIndexWithPending => throw new NotImplementedException();

        // ---- what the game calls ---------------------------------------------------------

        /// <summary>A campaign level was won with <paramref name="score"/> left. Counted locally at
        /// once and queued; which tournament it lands in is the server's call (the one running when
        /// it arrives). Called from the win flow, before the win popup.
        /// The queue holds at most <see cref="TournamentConfig.maxPendingWins"/> wins; past that the
        /// oldest are dropped — they belong to a tournament that has closed anyway.</summary>
        public static void OnLevelWin(int score) => throw new NotImplementedException();

        /// <summary>Background sync — one line over <see cref="TournamentSyncer"/>, which owns
        /// the request, the answer and how they change <see cref="State"/>.</summary>
        public static Task Sync() => throw new NotImplementedException();

        /// <summary>The Tournament Ended popup's button (Claim / Continue). The rank was already
        /// confirmed during sync, so the prize is granted here and nothing is sent. Marks the result
        /// as shown, so it never pops again.</summary>
        public static void CompleteEnded() => throw new NotImplementedException();

        // ---- debug (MBDebugWin) ----------------------------------------------------------

        public static void DebugWin(int score) => throw new NotImplementedException();

        /// <summary>Move the tournament clock so the current one ends in <paramref name="left"/>
        /// (negative = already over).</summary>
        public static void DebugSetTimeLeft(TimeSpan left) => throw new NotImplementedException();

        /// <summary>Wipe the client blob, the mock server and the clock offset.</summary>
        public static void DebugResetAll() => throw new NotImplementedException();

        /// <summary>Dump both snapshots to the console, plus the sync bookkeeping.</summary>
        public static void DebugLogTable() => throw new NotImplementedException();

        // ---- internal --------------------------------------------------------------------
        //
        // Config   — Resources/TournamentConfig, loaded once on the main thread; null = feature off.
        // Backend  — the server (mock until the real one exists).
        // Syncer   — TournamentSyncer: builds the request, calls the backend, applies the answer.
        // Now      — server UTC + the debug offset; the clock the feature (and the mock) runs on.
        // IsUnlocked / IsOnline — inputs to Status: the campaign reached Config.unlockLevel, and
        //            there is a network AND a trusted clock (MBServerTimeManagerV2.IsTimeSynced).
        // LastSyncOk / LastSyncUtc — sync bookkeeping, shown in the debug window only.
    }
}
