using Common;
using QueensPuzzle;

namespace qp {

    /// <summary>Where the daily streak stands for THIS player — drives the lobby button.</summary>
    public enum EStreakStatus {
        Offline,   // no trusted server time — the streak can't be verified/advanced
        Active,    // unlocked and online — shows streak + progress
    }

    /// <summary>What a win did to the streak — returned by <see cref="DailyStreakManager.RegisterWin"/>
    /// so the win flow can show the right UI.</summary>
    public struct StreakResult {
        public bool advanced;    // the streak went up this call (online, first win today) — show streak UI
        public int streak;       // the streak value after this win
        public Reward reward;    // null = no milestone; non-null = milestone reached, show reward UI
    }

    /// <summary>
    /// Daily streak logic. Online-only: a win counts toward the streak only when server time is
    /// trusted (MBServerTimeManagerV2.IsTimeSynced); the day index comes from MBServerTimeManagerV2.UTCNow.
    ///
    /// Rule: first win of a server-UTC day advances the streak by 1; a same-day extra win does nothing;
    /// a win after a one-day gap continues; a longer gap starts over at 1. Streak cycles 1..cycleLength
    /// (21) then wraps back to 1. Milestones (7/14/21) grant their reward the instant they're reached.
    ///
    /// Persisted: <see cref="_streak"/> and <see cref="_lastWinDay"/> (two ints). Everything the
    /// lobby shows (status, missed-day reset, milestone/progress) is derived on read.
    /// </summary>
    public static class DailyStreakManager {

        const string ConfigResource = "DailyStreakConfig";

        static readonly System.DateTime Epoch =
            new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);

        // ---- persisted ----------------------------------------------------------------

        static readonly PlayerPrefsHelper.IntHolder _streak
            = new PlayerPrefsHelper.IntHolder("qp_streak", 0);

        static readonly PlayerPrefsHelper.IntHolder _lastWinDay
            = new PlayerPrefsHelper.IntHolder("qp_streak_last_day", -1);

        // ---- public API -----------------------------------------------------------------

        /// <summary>Called from the win hook (MBGameplay.Win). Online only. Advances or resets the
        /// streak per the day-gap rule, wraps cycle→1, and grants any milestone reward reached.
        /// Returns what happened so the win flow can show the streak/reward UI.</summary>
        public static StreakResult RegisterWin() {
            SyncDayChange();
            if (Config == null || !IsOnline || _lastWinDay.Value == Today)   // off/offline; one win/day
                return new StreakResult { advanced = false, streak = _streak.Value, reward = null };

            int today = Today;
            int s = _streak.Value;
            // completed a full cycle, or the streak lapsed (missed a day / first ever) → start over
            if (s >= Config.cycleLength || _lastWinDay.Value != today - 1) s = 1;
            else s++;                               // second consecutive day and onward

            _streak.Value = s;
            _lastWinDay.Value = today;

            Reward reward = MilestoneRewardFor(s);   // null unless s is a milestone day
            reward?.Grant();

            return new StreakResult { advanced = true, streak = s, reward = reward };
        }

        /// <summary>Lock (below unlock level, or config missing) / Offline (no trusted time) / Active.</summary>
        public static EStreakStatus Status =>
            !IsOnline ? EStreakStatus.Offline :
            EStreakStatus.Active;

        public static int Streak =>  _streak.Value;

        /// <summary>Debug only (MBDebugWin) — force the streak to a value and pin the last win to
        /// today, so <see cref="SyncDayChange"/> won't immediately reset it.</summary>
        public static void DebugSetStreak(int value) {
            _streak.Value = value < 0 ? 0 : value;
            _lastWinDay.Value = Today;
        }

        /// <summary>Break the streak if a day lapsed since the last win. Online only (offline can't
        /// verify the day). Idempotent — the streak &gt; 0 guard means it writes at most once.
        /// The lobby button calls this each second so a midnight lapse shows even while it sits open.</summary>
        public static void SyncDayChange() {
            if (IsOnline && _streak.Value > 0 && _lastWinDay.Value < Today - 1)
                _streak.Value = 0;
        }

        /// <summary>Streak wraps back to 1 after this many days (from config; 21 if config missing).</summary>
        public static int CycleLength => Config != null ? Config.cycleLength : 21;

        /// <summary>The three milestone days, ascending — evenly spaced at 1/3, 2/3 and the full
        /// cycle (e.g. cycle 21 → 7 / 14 / 21). These are the progress-bar stage boundaries.</summary>
        public static int[] Milestones {
            get {
                int c = CycleLength;
                return new[] {
                    (int)System.Math.Round(c / 3.0),
                    (int)System.Math.Round(c * 2.0 / 3.0),
                    c,
                };
            }
        }

        // ---- helpers --------------------------------------------------------------------

        static bool IsOnline => MBServerTimeManagerV2.IsTimeSynced;

        // The reward for reaching a given streak day, or null when it isn't a milestone. Rewards are
        // indexed by tier (1st/2nd/3rd milestone) since the days themselves are derived from the cycle.
        static Reward MilestoneRewardFor(int streak) {
            var ms = Milestones;
            for (int i = 0; i < ms.Length; i++)
                if (ms[i] == streak) return Config != null ? Config.RewardForTier(i) : null;
            return null;
        }

        // Days since epoch in trusted server UTC — the calendar index the streak counts in.
        static int Today => (int)(MBServerTimeManagerV2.UTCNow.Date - Epoch).TotalDays;

        static DailyStreakConfig _config;
        static DailyStreakConfig Config {
            get {
                if (_config == null) {
                    _config = UnityEngine.Resources.Load<DailyStreakConfig>(ConfigResource);
                    if (_config == null)
                        CDebug.LogError($"[DailyStreakManager] Resources/{ConfigResource}.asset not found - streak is off.");
                }
                return _config;
            }
        }
    }
}
