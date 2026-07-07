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

        // ---- game flow: Firebase events, each with the level and its attempt number ----

        public static void GameStart(int levelIdx, int attempts) => GameEvent("game_start", levelIdx, attempts);
        public static void GameWin(int levelIdx, int attempts)   => GameEvent("game_win",   levelIdx, attempts);
        public static void GameLose(int levelIdx, int attempts)  => GameEvent("game_lose",  levelIdx, attempts);

        static void GameEvent(string name, int levelIdx, int attempts) {

#if !IGNORE_FIREBASE
            CDebug.Log(name, new Firebase.Analytics.Parameter("level_idx", levelIdx),
                             new Firebase.Analytics.Parameter("attempts", attempts));
#else
            CDebug.Log(name);
#endif
        }
    }
}
