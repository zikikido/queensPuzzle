using UnityEngine;

namespace qp {

    /// <summary>What the win screen celebrates — one per win, or None.</summary>
    public enum EWinAchievement {
        None,        // nothing crossed the bar — the popup shows no achievement line
        Time,        // ⏱️ FASTER THAN X% OF PLAYERS!
        NoBones,     // 🦴 BETTER THAN X% OF PLAYERS!   (finished with all lives)
        FirstTry,    // 🎯 BETTER THAN X% OF PLAYERS!   (won on the first attempt)
        WinStreak,   // ⚡ X WIN STREAK!
        BestStreak,  // 🏆 NEW BEST STREAK: X!
    }

    /// <summary>The chosen achievement + its number, ready for display.</summary>
    public readonly struct WinAchievement {
        public readonly EWinAchievement Type;
        public readonly float Pct;      // comparison types — "better/faster than Pct%"
        public readonly int Streak;     // streak types — the streak length

        WinAchievement(EWinAchievement type, float pct, int streak) {
            Type = type; Pct = pct; Streak = streak;
        }

        public static readonly WinAchievement None = new WinAchievement(EWinAchievement.None, -1f, 0);

        static WinAchievement Of(EWinAchievement type, float pct) => new WinAchievement(type, pct, 0);
        static WinAchievement OfStreak(EWinAchievement type, int n) => new WinAchievement(type, -1f, n);

        /// <summary>A comparison line only shows when the player beats at least this share —
        /// display POLICY, so it lives here and not in the WinStats data reader.</summary>
        public const float MinShowPct = 60f;

        /// <summary>
        /// Picks the ONE achievement this win shows, per the agreed priority:
        ///   1. 🏆 a NEW best streak — the rare moment always wins
        ///   2. the strongest comparison that cleared <see cref="MinShowPct"/> (⏱️/🦴/🎯)
        ///   3. ⚡ the running win streak, once it's 3+
        ///   4. nothing
        /// Comparison stats come from the baked winstats blob; -1 everywhere = no line.
        /// Call AFTER the win while LastPlayData still describes it (same contract as
        /// Analytics.GameWin), passing the streak state of THIS win.
        /// </summary>
        public static WinAchievement Pick(int winStreak, bool isNewBestStreak) {
            if (isNewBestStreak && winStreak >= 2)              // a 1-long "best" isn't a moment
                return OfStreak(EWinAchievement.BestStreak, winStreak);

            var d = AppData.LastPlayData;
            var stats = WinStats.For(LevelLoader.CurrentLevelHash, LevelLoader.CurrentLevelWeight);

            var best = None;

            void Consider(EWinAchievement type, float pct) {
                if (pct >= MinShowPct && pct > best.Pct) best = Of(type, pct);
            }

            Consider(EWinAchievement.Time, stats.FasterThanPct(AppData.LevelTimeSec.Value));

            // "no bones lost" only applies when the run was actually clean
            if (d.bonesLost == 0) Consider(EWinAchievement.NoBones, stats.NoBonesBeatsPct);

            if (AppData.LevelAttempts.Value == 1) Consider(EWinAchievement.FirstTry, stats.FirstTryBeatsPct);

            if (best.Type != EWinAchievement.None) return best;

            if (winStreak >= 3) return OfStreak(EWinAchievement.WinStreak, winStreak);

            return None;
        }
    }
}
