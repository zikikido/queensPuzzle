using System;
using Common;
using QueensPuzzle;

namespace qp {

    /// <summary>Where today's daily challenge stands for THIS player — drives the lobby card.</summary>
    public enum EDailyChallengeStatus {
        Lock,     // campaign hasn't reached the unlock level yet
        Active,   // available today, not solved yet
        Done,     // solved today — card shows time + TOP X %
    }

    /// <summary>
    /// Everything Daily Challenge in one place — status, tier/day math, timer, solved bookkeeping.
    /// Everyone else only asks: the lobby card reads <see cref="Status"/>/<see cref="TimeLeft"/>/
    /// <see cref="State"/>, LevelLoader reads <see cref="InDailyRun"/>/<see cref="DayIndex"/>/
    /// <see cref="TierName"/> to serve the right pack+slot, GP ticks <see cref="AddPlayTime"/> and
    /// reports <see cref="OnSolved"/>.
    ///
    /// Persisted: <see cref="BoardsSolved"/> (one int) and <see cref="State"/> (today's blob).
    /// Everything else is derived on read — nothing to sync, nothing to invalidate:
    ///   DailyProgress = campaign LevelIdx + BoardsSolved → tier (locked per day at first open)
    ///   DayIndex = days since epoch (UTC) → slot inside the tier pack (LevelLoader: % packCount)
    /// Tier ranges + epoch ship in Resources as <see cref="DailyChallengeTiersConfig"/> ("DailyTiers").
    /// </summary>
    public static class DailyChallengeManager {

        // Campaign LevelIdx (0-based) that unlocks the daily — 21 completed levels, matching the
        // first tier's progressMin so DailyProgress lands inside a tier the moment it unlocks.
        const int UnlockLevelIdx = 21;

        const string TiersResource = "DailyChallengeTiers";

        // ---- persisted ----------------------------------------------------------------

        static readonly PlayerPrefsHelper.IntHolder _boardsSolved
            = new PlayerPrefsHelper.IntHolder("qp_daily_solved", 0);

        static readonly DailyChallengeState _state = DailyChallengeState.Load();

        /// <summary>Total dailies ever solved — grows only via <see cref="OnSolved"/>.</summary>
        public static int BoardsSolved => _boardsSolved.Value;

        /// <summary>Today's blob: locked tier, accumulated timer, solved + topPct for the card.</summary>
        public static DailyChallengeState State => _state;

        // ---- session ------------------------------------------------------------------

        /// <summary>A daily board is being played right now — set by the lobby's play click,
        /// cleared on exit. LevelLoader and the save slot key off this.</summary>
        public static bool InDailyRun { get; private set; }

        // ---- derived, nothing stored ----------------------------------------------------

        public static EDailyChallengeStatus Status =>
            !IsUnlocked || Tiers == null ? EDailyChallengeStatus.Lock :   // no shipped config = feature off
            _state.dayIndex == DayIndex && _state.solved ? EDailyChallengeStatus.Done :
            EDailyChallengeStatus.Active;

        /// <summary>Days since epoch, UTC — the calendar index all players share.</summary>
        public static int DayIndex => (int)(DateTime.UtcNow.Date - Tiers.Epoch).TotalDays;

        /// <summary>Today's tier: the one locked at first open wins; before that, derived from progress.</summary>
        public static int Tier =>
            _state.dayIndex == DayIndex && _state.tier >= 0 ? _state.tier : Tiers.TierOf(DailyProgress);

        /// <summary>Tier name as authored ("Tier2".."Endless") — the pack resource is
        /// Levels/daily_{name,lowercase}, the levelSetId is "Daily{name}".</summary>
        public static string TierName => Tiers.tiers[Tier].name;

        /// <summary>Countdown to the next UTC midnight — when today's puzzle is replaced.</summary>
        public static TimeSpan TimeLeft => DateTime.UtcNow.Date.AddDays(1) - DateTime.UtcNow;

        /// <summary>Today's date for the lobby card, e.g. "Jul 15" (UTC — matches the puzzle day).</summary>
        public static string NiceDate =>
            DateTime.UtcNow.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture);

        static bool IsUnlocked => AppData.LevelIdx >= UnlockLevelIdx;

        static int DailyProgress => AppData.LevelIdx + _boardsSolved.Value;

        static DailyChallengeTiersConfig _tiers;
        static DailyChallengeTiersConfig Tiers {
            get {
                if (_tiers == null) {
                    _tiers = UnityEngine.Resources.Load<DailyChallengeTiersConfig>(TiersResource);
                    if (_tiers == null)
                        CDebug.LogError($"[DailyChallengeManager] Resources/{TiersResource}.asset not found - run the daily export.");
                }
                return _tiers;
            }
        }

        // ---- flow -----------------------------------------------------------------------

        /// <summary>The lobby's play click. First open of a new UTC day locks today's tier and
        /// resets the blob (an unfinished yesterday never counted); a re-entry the same day keeps
        /// the timer and continues.</summary>
        public static void StartDaily() {
            if (_state.dayIndex != DayIndex) {
                _state.dayIndex = DayIndex;
                _state.tier = Tiers.TierOf(DailyProgress);
                _state.timeSec = 0f;
                _state.attempts = 0;
                _state.solved = false;
                _state.topPct = 0;
                _state.Save();
            }
            InDailyRun = true;
        }

        /// <summary>Back to lobby/campaign — the daily save blob stays for a same-day resume.</summary>
        public static void ExitDaily() => InDailyRun = false;

        /// <summary>A fresh daily board began (first start, restart, retry after fail) — the
        /// daily counterpart of AppData.LevelAttempts. Resets with the day (StartDaily).</summary>
        public static void BumpAttempt() {
            if (_state.dayIndex != DayIndex) return;   // StartDaily always runs first
            _state.attempts++;
            _state.Save();
        }

        /// <summary>GP ticks this only while the board is actively playing. Persists on every
        /// call — feed it accumulated chunks (~once per second / on pause), not per frame.</summary>
        public static void AddPlayTime(float sec) {
            if (!InDailyRun || _state.solved || _state.dayIndex != DayIndex) return;
            _state.timeSec += sec;
            _state.Save();
        }

        /// <summary>The daily board was solved. If the UTC day flipped mid-solve, the win counts
        /// for nothing — the day it belonged to is gone (rule: unfinished day = not solved).</summary>
        public static void OnSolved() {
            if (!InDailyRun || _state.solved || _state.dayIndex != DayIndex) return;
            _state.solved = true;
            _state.topPct = FakeTopPct(_state.timeSec);
            _state.Save();
            _boardsSolved.Value++;
        }

        /// <summary>Solve-time display shared by the lobby card and the top bar: "12:34", "1:02:34".</summary>
        public static string FormatTime(float sec) {
            int s = sec < 0f ? 0 : (int)Math.Round(sec);
            return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
        }

        // "TOP X %" v1 — no backend yet, so a deterministic curve over the solve time:
        // 2 min or faster = top 5%, then linear to 95% at 25 min. Replace with the real
        // leaderboard percentile when a server exists.
        static int FakeTopPct(float timeSec) {
            const float fast = 120f, slow = 1500f;
            float t = (timeSec - fast) / (slow - fast);
            int pct = (int)Math.Round(5f + 90f * t);
            return pct < 5 ? 5 : pct > 95 ? 95 : pct;
        }
    }
}
