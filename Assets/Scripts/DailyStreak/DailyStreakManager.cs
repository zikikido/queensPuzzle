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
    /// a win after a one-day gap continues; a longer gap starts over at 1. Streak cycles 1..28 then
    /// wraps back to 1. Milestones (7/14/28) grant their reward the instant they're reached.
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
        /// streak per the day-gap rule, wraps 28→1, and grants any milestone reward reached.
        /// Returns what happened so the win flow can show the streak/reward UI.</summary>
        public static StreakResult RegisterWin() {
            if (Config == null || !IsOnline || _lastWinDay.Value == Today)   // off/offline; one win/day
                return new StreakResult { advanced = false, streak = _streak.Value, reward = null };

            int today = Today;
            int s = _streak.Value;
            // completed a full cycle, or the streak lapsed (missed a day / first ever) → start over
            if (s >= Config.cycleLength || _lastWinDay.Value != today - 1) s = 1;
            else s++;                               // second consecutive day and onward

            _streak.Value = s;
            _lastWinDay.Value = today;

            Reward reward = Config.RewardFor(s);   // null unless s is a milestone day
            reward?.Grant();

            return new StreakResult { advanced = true, streak = s, reward = reward };
        }

        /// <summary>Lock (below unlock level, or config missing) / Offline (no trusted time) / Active.</summary>
        public static EStreakStatus Status =>
            !IsOnline ? EStreakStatus.Offline :
            EStreakStatus.Active;

        public static int Streak =>  _streak.Value;


        // ---- helpers --------------------------------------------------------------------

        static bool IsOnline => MBServerTimeManagerV2.IsTimeSynced;

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
