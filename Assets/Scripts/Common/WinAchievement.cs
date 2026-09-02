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

        // Variety memory (session-only): the type shown on the previous win is DEMOTED to the
        // back of the queue, so back-to-back wins don't repeat the same line when another
        // qualifying achievement exists.
        static EWinAchievement _lastShown = EWinAchievement.None;

        /// <summary>
        /// Picks the ONE achievement this win shows. Default priority:
        ///   1. comparisons that cleared <see cref="MinShowPct"/> (⏱️/🦴/🎯), strongest first
        ///   2. 🏆 a NEW best streak (2+)
        ///   3. ⚡ the running win streak, once it's 3+
        /// ...but the type shown on the PREVIOUS win drops to the end of that order, so the
        /// popup varies. Nothing qualifies → no line.
        /// Comparison stats come from the baked winstats blob; -1 everywhere = no line.
        /// Call AFTER the win while LastPlayData still describes it (same contract as
        /// Analytics.GameWin), passing the streak state of THIS win.
        /// </summary>
        public static WinAchievement Pick(int winStreak, bool isNewBestStreak) {
            var d = AppData.LastPlayData;
            var stats = WinStats.For(LevelLoader.CurrentLevelHash, LevelLoader.CurrentLevelWeight);

            // Daily runs keep their time/attempts in DailyChallengeManager.State — same
            // switches Analytics uses, so the comparisons are right in both modes.
            bool daily = DailyChallengeManager.InDailyRun;
            float timeSec = daily ? DailyChallengeManager.State.timeSec : AppData.LevelTimeSec.Value;
            int attempts = daily ? DailyChallengeManager.State.attempts : AppData.LevelAttempts.Value;

            // Everything that qualifies, in the default order.
            var candidates = new System.Collections.Generic.List<WinAchievement>(5);

            void Consider(EWinAchievement type, float pct) {
                if (pct < MinShowPct) return;
                int at = candidates.Count;                              // comparisons: strongest first
                while (at > 0 && candidates[at - 1].Pct < pct) at--;
                candidates.Insert(at, Of(type, pct));
            }

            Consider(EWinAchievement.Time, stats.FasterThanPct(timeSec));
            if (d.bonesLost == 0) Consider(EWinAchievement.NoBones, stats.NoBonesBeatsPct);   // only a clean run
            if (attempts == 1) Consider(EWinAchievement.FirstTry, stats.FirstTryBeatsPct);

            if (isNewBestStreak && winStreak >= 2)                      // a 1-long "best" isn't a moment
                candidates.Add(OfStreak(EWinAchievement.BestStreak, winStreak));
            if (winStreak >= 3)
                candidates.Add(OfStreak(EWinAchievement.WinStreak, winStreak));

            var pick = None;
            if (candidates.Count > 0) {
                pick = candidates[0];
                if (pick.Type == _lastShown)                            // demote what was just shown
                    for (int i = 1; i < candidates.Count; i++)
                        if (candidates[i].Type != _lastShown) { pick = candidates[i]; break; }
                _lastShown = pick.Type;
            }

#if UNITY_EDITOR
            UnityEngine.Debug.Log($"[WinAchievement] hash {LevelLoader.CurrentLevelHash} global={stats.IsGlobal} | " +
                $"time {timeSec:0}s -> {stats.FasterThanPct(timeSec):0.#}% | bones lost {d.bonesLost} -> {stats.NoBonesBeatsPct}% | " +
                $"attempts {attempts} -> {stats.FirstTryBeatsPct}% | streak {winStreak} (newBest={isNewBestStreak}) | " +
                $"candidates {candidates.Count} | picked: {pick.Type}");
#endif
            return pick;
        }
    }
}
