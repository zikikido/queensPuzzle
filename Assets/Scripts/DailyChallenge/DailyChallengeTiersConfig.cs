using System;
using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// The SHIPPED slice of the daily config — only what the runtime needs: the epoch and which
    /// tier serves which DailyProgress range. Difficulty bands, weekly wave, pool sizes stay
    /// editor-side on <see cref="DailyChallengeCurveConfig"/>; this asset is auto-generated from it on
    /// daily export (single authoring source, no drift) and lives in Resources as "DailyChallengeTiers".
    /// Pool size is not here on purpose — the runtime reads it from the tier pack's header.
    /// </summary>
    [CreateAssetMenu(fileName = "DailyChallengeTiers", menuName = "QueensPuzzle/Daily Challenge Tiers (runtime)", order = 3)]
    public class DailyChallengeTiersConfig : ScriptableObject
    {
        [Serializable]
        public class Tier
        {
            [Tooltip("Same name as the authoring tier — the pack resource is Levels/daily_{name,lowercase}.")]
            public string name;
            public int progressMin;
            public int progressMax;
        }

        [Tooltip("Day 0, UTC, yyyy-MM-dd, a Monday — copied from DailyChallengeCurveConfig.")]
        public string epochDate = "2026-01-05";

        public Tier[] tiers;

        public DateTime Epoch =>
            DateTime.TryParseExact(epochDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var d) ? d.Date : new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Tier index for a DailyProgress value (first tier whose range holds it; clamps to the last).</summary>
        public int TierOf(int dailyProgress)
        {
            for (int i = 0; i < tiers.Length; i++)
                if (dailyProgress <= tiers[i].progressMax) return i;
            return tiers.Length - 1;
        }
    }
}
