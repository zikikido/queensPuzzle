using Common;

namespace qp {

    /// <summary>
    /// Win streak — consecutive wins with no fail between them (persisted). Campaign AND
    /// daily wins both count; ANY fail resets it (even if the player then revives and
    /// wins — the fail happened). Quitting mid-level or a settings restart doesn't touch
    /// it. MBGameplay.Win()/Fail() drive it; the win popup reads it for the ⚡/🏆 lines.
    /// </summary>
    public static class WinStreak {

        static readonly PlayerPrefsHelper.IntHolder _current
            = new PlayerPrefsHelper.IntHolder("qp_win_streak", 0);
        static readonly PlayerPrefsHelper.IntHolder _best
            = new PlayerPrefsHelper.IntHolder("qp_best_win_streak", 0);

        public static int Current => _current.Value;
        public static int Best => _best.Value;

        /// <summary>True when the LAST registered win pushed the best to a new record —
        /// valid while the win popup for that win is on screen.</summary>
        public static bool LastWinWasNewBest { get; private set; }

        public static void OnWin() {
            _current.Value = _current.Value + 1;
            LastWinWasNewBest = _current.Value > _best.Value;
            if (LastWinWasNewBest) _best.Value = _current.Value;
        }

        public static void OnFail() {
            _current.Value = 0;
            LastWinWasNewBest = false;
        }
    }
}
