using Common;
using UnityEngine;

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

        // Campaign level wins we mark as conversion events in the ad networks / Firebase.
        // 1-based level numbers; the very first level is lvl_win_1.
        // 12 = one stage past StartShowInterAtLevel (11) — "survived the interstitials" signal.
        static readonly int[] WinMilestones = { 1, 2, 3, 5, 11, 12, 21, 30 };

        public static void GameStart() {
            GameEvent("game_start");
            AppLovinEvent("level_start", AppData.LevelIdx.Value + 1);
        }

        public static void GameWin() {
            GameEvent("game_win");
            // Milestone conversion events — campaign only (daily day indices would pollute them).
            // GameWin fires before LevelIdx++, so LevelIdx.Value is the just-solved level (0-based).
            if (!DailyChallengeManager.InDailyRun) {
                int levelNum = AppData.LevelIdx.Value + 1;
                if (System.Array.IndexOf(WinMilestones, levelNum) >= 0)
                    GameEvent("lvl_win_" + levelNum);
            }
            AppLovinEvent("level_complete", AppData.LevelIdx.Value + 1);
            if (AppData.LevelIdx.Value == 0) AppLovinEvent("tutorial_complete");
        }

        public static void GameLose() {
            GameEvent("game_lose");
        }

        /// <summary>Breadcrumb for the cold start, from the boot stage. The launch EVENT is
        /// `pd_session_start` in FirebaseLaunch, which runs once Firebase is up.</summary>
        public static void SessionStart() => CrashLog($"[session] start #{UserData.Instance.Sessions}");

        /// <summary>
        /// Firebase side of the launch — run from the boot stage AFTER Firebase is up (logs before
        /// that are dropped). `pd_session_start` is the launch event retention is derived from (the
        /// name `session_start` is reserved by Firebase), and the user properties ride on every
        /// BigQuery row so it joins to our users without going through GAID.
        /// </summary>
#if !IGNORE_FIREBASE
        public static void FirebaseLaunch() {

            if (!FirebaseBootstrap.FBAvailable) return;
            Firebase.Analytics.FirebaseAnalytics.SetUserProperty("uid", _userId);
            Firebase.Analytics.FirebaseAnalytics.SetUserProperty("first_version", _firstVersion.ToString());
            SetSourceProperty(AppData.SingularSource.Value);
            CDebug.Log("pd_session_start",
                new Firebase.Analytics.Parameter("session", UserData.Instance.Sessions),
                new Firebase.Analytics.Parameter("lvl_idx", AppData.LevelIdx.Value));
        }

        /// <summary>Install network as a Firebase user property — at launch, and again when the
        /// Singular attribution callback resolves it (it can land after the launch stage).</summary>
        public static void SetSourceProperty(SingularSource source) {

            if (!FirebaseBootstrap.FBAvailable || string.IsNullOrEmpty(source?.network)) return;
            var network = source.network.Length > 36 ? source.network.Substring(0, 36) : source.network;   // Firebase value cap
            Firebase.Analytics.FirebaseAnalytics.SetUserProperty("src_network", network);
        }
#endif
        // The user identity FirebaseLaunch reports, read once on the main thread.
        //
        // Both values come from PlayerPrefs (UserData.FirstVersion, UserID.GetUserIDLocal()),
        // which is a Unity API and not thread-safe, and GetUserIDLocal is the dangerous one: it
        // caches into a plain static with no memory barrier, so a thread that misses the main
        // thread's write falls through to the "no id yet" branch, MINTS A NEW GUID and
        // PlayerPrefs.Save()s it over the real one — silently changing the user's identity
        // mid-session and breaking every user_id join. Captured at boot, never read live.
        static string _userId;
        static int _firstVersion;

        /// <summary>Main thread, from MBStartup, before any SDK can fire a callback.</summary>
        public static void CaptureCommon() {
            _firstVersion = UserData.Instance.FirstVersion;
            _userId       = Common.UserID.GetUserIDLocal();
        }

        /// <summary>A boost the player actually used ("hint" / "queen" / "undo") — counters already bumped.</summary>
        public static void BoostUsed(string boost) {
            GameEvent("boost_used", "boost", boost);
            AppLovinEvent("use_prop");
        }

        /// <summary>The lose popup is offering the rewarded revive — once per popup show.</summary>
        public static void RewardedOpportunity() => AppLovinEvent("rewarded_ad_opportunity");

        // AppLovin in-game events (MaxSdk.TrackEvent, predefined names) — signals for their models.
        // Campaign only: nothing is sent during a daily run.
        // Reused: TrackEvent serializes it to JSON synchronously, and every caller is on the main thread.
        static readonly System.Collections.Generic.Dictionary<string, string> _appLovinData = new();
        static void AppLovinEvent(string name, int? value = null) {
            if (DailyChallengeManager.InDailyRun) return;
            CrashLog($"[applovin] {name}" + (value.HasValue ? $" value={value}" : ""));
            _appLovinData.Clear();
            if (value.HasValue) _appLovinData["value"] = value.Value.ToString();
            MaxSdk.TrackEvent(name, _appLovinData);
        }

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
            int timeSec = daily ? (int)DailyChallengeManager.State.timeSec : AppData.LevelTimeSec.Value;
            CrashLog($"[game] {name}{extra} level {levelIdx} set {LevelLoader.CurrentLevelSetId} pack {LevelLoader.CurrentPackIndex} hash {LevelLoader.CurrentLevelHash} attempt {attempts} | hints {d.hintsUsed} queens {d.queenBoostsUsed} undos {d.undosUsed} lives+ {d.livesAdded} bones- {d.bonesLost} | time {timeSec}s");
#if !IGNORE_FIREBASE
            var ps = new System.Collections.Generic.List<Firebase.Analytics.Parameter> {
                new Firebase.Analytics.Parameter("lvl_idx", levelIdx),
                new Firebase.Analytics.Parameter("pack_idx", LevelLoader.CurrentPackIndex),
                new Firebase.Analytics.Parameter("level_set_id", LevelLoader.CurrentLevelSetId),
                new Firebase.Analytics.Parameter("level_hash", LevelLoader.CurrentLevelHash),
                new Firebase.Analytics.Parameter("lvl_attempts", attempts),
                new Firebase.Analytics.Parameter("daily", daily ? 1 : 0),
            };
            ps.AddRange(d.ToParams());
            // accumulated active solve time — final on game_win (daily: OnSolved runs first;
            // campaign: _ready is false at Win, so the clock has already stopped)
            ps.Add(new Firebase.Analytics.Parameter("lvl_time_sec", timeSec));
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
