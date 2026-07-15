using Common;

namespace qp {

    /// <summary>
    /// All game analytics goes through here. Every report is both a Firebase event and a
    /// Crashlytics breadcrumb, so crash reports always show what the player was doing.
    /// </summary>
    public static class Analytics {

        /// <summary>Breadcrumb only — attached to the next crash report (and the Editor console).</summary>
        public static void CrashLog(string message) => CDebug.CrashLog(message);

        public static void ScreenOpen(string screen) {
            CrashLog($"[screen] open {screen}");
        }

        public static void ScreenClose(string screen) {
            CrashLog($"[screen] close {screen}");
        }

        // ---- game flow: Firebase events. The level id and attempt number are DERIVED here —
        // daily runs report the day index + the day's attempts, campaign runs the level index +
        // AppData.LevelAttempts — so no call site can ever send the wrong pair.
        //
        // CONTRACT for callers: fire the event while the statics still describe the thing the
        // event is about — i.e. BEFORE advancing progress (LevelIdx++), BEFORE invalidating /
        // stashing LastPlayData, and BEFORE ExitDaily; AFTER LoadLevel, attempt bumps and
        // counter bumps. Win()/Fail() in MBGameplay document the exact order.

        static int LevelIdx => DailyChallengeManager.InDailyRun
            ? DailyChallengeManager.DayIndex : AppData.LevelIdx.Value;
        static int Attempts => DailyChallengeManager.InDailyRun
            ? DailyChallengeManager.State.attempts : AppData.LevelAttempts.Value;

        public static void GameStart() => GameEvent("game_start");
        public static void GameWin()   => GameEvent("game_win");
        public static void GameLose()  => GameEvent("game_lose");

        /// <summary>A boost the player actually used ("hint" / "queen" / "undo") — counters already bumped.</summary>
        public static void BoostUsed(string boost) => GameEvent("boost_used", "boost", boost);

        /// <summary>The fail-continue grant (later: video/coins) — its own event stream, not a boost.</summary>
        public static void LivesAdded(int amount) => GameEvent("lives_added", "amount", amount);

        /// <summary>A booster earned by watching a rewarded ad (no boost left → video).</summary>
        public static void BoostEarned(string boost) => GameEvent("boost_earned", "boost", boost);

        // TEMP — review prepare timing probe, read via BigQuery. REMOVE after the measurement.
        public static void ReviewPrepareTime(int ms, bool prepared) {
            CrashLog($"[review] prepare took {ms} ms (prepared={prepared})");
#if !IGNORE_FIREBASE
            CDebug.Log("review_prepare_time", new Firebase.Analytics.Parameter("gr_ms", ms),
                                              new Firebase.Analytics.Parameter("gr_prepared", prepared ? 1 : 0));
#endif
        }

        // every game event carries the level, the attempt and the attempt's counters;
        // extraKey/extraVal (optional) adds the event's own parameter (boost name, amount, ...)
        static void GameEvent(string name, string extraKey = null, object extraVal = null) {
            var d = AppData.LastPlayData;
            bool daily = DailyChallengeManager.InDailyRun;
            int levelIdx = LevelIdx;
            int attempts = Attempts;
            var extra = extraKey != null ? $" {extraKey}={extraVal}" : "";
            var dailyCrumb = daily ? $" | daily time {(int)DailyChallengeManager.State.timeSec}s" : "";
            CrashLog($"[game] {name}{extra} level {levelIdx} set {LevelLoader.CurrentLevelSetId} pack {LevelLoader.CurrentPackIndex} hash {LevelLoader.CurrentLevelHash} attempt {attempts} | hints {d.hintsUsed} queens {d.queenBoostsUsed} undos {d.undosUsed} lives+ {d.livesAdded} bones- {d.bonesLost}{dailyCrumb}");
#if !IGNORE_FIREBASE
            var ps = new System.Collections.Generic.List<Firebase.Analytics.Parameter> {
                new Firebase.Analytics.Parameter("lvl_idx", levelIdx),
                new Firebase.Analytics.Parameter("pack_idx", LevelLoader.CurrentPackIndex),
                new Firebase.Analytics.Parameter("level_set_id", LevelLoader.CurrentLevelSetId),
                new Firebase.Analytics.Parameter("level_hash", LevelLoader.CurrentLevelHash),
                new Firebase.Analytics.Parameter("lvl_attempts", attempts),
            };
            ps.AddRange(d.ToParams());
            if (daily) {
                // the daily's accumulated solve clock — final on game_win (OnSolved runs first)
                ps.Add(new Firebase.Analytics.Parameter("lvl_time_sec", (int)DailyChallengeManager.State.timeSec));
            }
            if (extraKey != null)
                ps.Add(extraVal is int i ? new Firebase.Analytics.Parameter(extraKey, i)
                                         : new Firebase.Analytics.Parameter(extraKey, extraVal.ToString()));
            CDebug.Log(name, ps.ToArray());
#else
            CDebug.Log(name);
#endif
        }
    }
}
