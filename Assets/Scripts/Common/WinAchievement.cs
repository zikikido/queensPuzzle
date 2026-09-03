using UnityEngine;

namespace qp {

    /// <summary>What the win screen celebrates — one per win, or None.</summary>
    public enum EWinAchievement {
        None,        // nothing crossed the bar — the popup shows no achievement line
        Time,        // ⏱️ FASTER THAN X% OF PLAYERS!
        NoBones,     // 🦴 BETTER THAN X% OF PLAYERS!   (finished with all lives)
        FirstTry,    // 🎯 BETTER THAN X% OF PLAYERS!   (won on the first attempt)
    }

    /// <summary>The chosen achievement + its number, ready for display.</summary>
    public readonly struct WinAchievement {
        public readonly EWinAchievement Type;
        public readonly float Pct;      // "better/faster than Pct%"

        WinAchievement(EWinAchievement type, float pct) {
            Type = type; Pct = pct;
        }

        public static readonly WinAchievement None = new WinAchievement(EWinAchievement.None, -1f);

        static WinAchievement Of(EWinAchievement type, float pct) => new WinAchievement(type, pct);

        /// <summary>A comparison line only shows when the player beats at least this share —
        /// display POLICY, so it lives here and not in the WinStats data reader.</summary>
        public const float MinShowPct = 60f;

        /// <summary>
        /// Picks the ONE achievement this win shows: the STRONGEST comparison (⏱️/🦴/🎯)
        /// that cleared <see cref="MinShowPct"/> — always the best, every time. Nothing
        /// qualifies → no line. Stats come from the baked winstats blob; -1 = no data.
        /// Call AFTER the win while LastPlayData still describes it (same contract as
        /// Analytics.GameWin).
        /// </summary>
        public static WinAchievement Pick() {
            var d = AppData.LastPlayData;
            var stats = WinStats.For(LevelLoader.CurrentLevelHash, LevelLoader.CurrentLevelWeight);

            // Daily runs keep their time/attempts in DailyChallengeManager.State — same
            // switches Analytics uses, so the comparisons are right in both modes.
            bool daily = DailyChallengeManager.InDailyRun;
            float timeSec = daily ? DailyChallengeManager.State.timeSec : AppData.LevelTimeSec.Value;
            int attempts = daily ? DailyChallengeManager.State.attempts : AppData.LevelAttempts.Value;

            var pick = None;

            void Consider(EWinAchievement type, float pct) {
                if (pct >= MinShowPct && pct > pick.Pct) pick = Of(type, pct);
            }

            Consider(EWinAchievement.Time, stats.FasterThanPct(timeSec));
            if (d.bonesLost == 0) Consider(EWinAchievement.NoBones, stats.NoBonesBeatsPct);   // only a clean run
            if (attempts == 1) Consider(EWinAchievement.FirstTry, stats.FirstTryBeatsPct);

#if UNITY_EDITOR
            UnityEngine.Debug.Log($"[WinAchievement] hash {LevelLoader.CurrentLevelHash} global={stats.IsGlobal} | " +
                $"time {timeSec:0}s -> {stats.FasterThanPct(timeSec):0.#}% | bones lost {d.bonesLost} -> {stats.NoBonesBeatsPct}% | " +
                $"attempts {attempts} -> {stats.FirstTryBeatsPct}% | picked: {pick.Type}");
#endif
            return pick;
        }
    }
}
