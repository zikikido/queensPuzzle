using System;
using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// Daily Challenge tuning as an EDITOR authoring asset — the daily counterpart of
    /// <see cref="CampaignCurveConfig"/>, but not a curve: a target FACTORY for pre-built pools.
    ///
    /// DailyProgress = campaign level reached + daily boards solved → a tier; each tier owns a
    /// pool of weeks*7 levels. Selection is calendar-driven and deterministic (fair leaderboards):
    ///   dayIndex = days since epoch (UTC) · slot = dayIndex % poolSize
    /// Pools are whole weeks and the epoch is a Monday, so slot % 7 IS the weekday and the weekly
    /// wave below stays aligned forever as the pool rotates.
    ///
    /// Scale, size ranges, tolerance and quality gates all come from the referenced campaign
    /// curve — daily 0.30 means exactly campaign 0.30. Jitter is a hash of (tier, slot) so
    /// same-weekday slots aren't clones and builds reproduce. Milestones (0.85–1.00) never
    /// appear in dailies.
    /// </summary>
    [CreateAssetMenu(fileName = "DailyCurve", menuName = "QueensPuzzle/Daily Curve", order = 2)]
    public class DailyCurveConfig : ScriptableObject
    {
        [Serializable]
        public class Tier
        {
            [Tooltip("Pool folder = Assets/Levels/Sets/Daily{name}; also the future pack suffix.")]
            public string name;
            [Tooltip("DailyProgress range (inclusive). The last tier is open-ended upward.")]
            public int progressMin;
            public int progressMax;
            [Tooltip("Target difficulty band 0..1 — the weekly wave is clamped into it.")]
            public float diffMin;
            public float diffMax;
            [Tooltip("The wave's center: weekday multipliers apply around this.")]
            public float baseDiff;
            [Tooltip("Pool length in WHOLE WEEKS (slots = weeks*7) so rotation keeps weekday alignment.")]
            public int weeks;

            public int PoolSize => weeks * 7;
        }

        [Tooltip("Shared source of truth: weight scale, size ranges, match tolerance, quality gates.")]
        public CampaignCurveConfig campaign;

        [Tooltip("Day 0, UTC, yyyy-MM-dd — MUST be a Monday so slot % 7 = weekday.")]
        public string epochDate = "2026-01-05";

        [Tooltip("Mon..Sun multipliers around each tier's baseDiff. Saturday = weekly peak, Sunday = breather.")]
        public float[] weekdayMult = { 0.85f, 0.95f, 1.05f, 0.90f, 1.10f, 1.20f, 0.85f };

        [Tooltip("± spread so same-weekday slots aren't clones. Hash of (tier, slot) — builds reproduce.")]
        public float jitter = 0.025f;

        [Tooltip("Daily unlocks at campaign 21, so there is no tier below DailyProgress 21.")]
        public Tier[] tiers =
        {
            new Tier { name = "Tier2",   progressMin = 21,  progressMax = 50,           diffMin = 0.12f, diffMax = 0.22f, baseDiff = 0.17f, weeks = 5 },
            new Tier { name = "Tier3",   progressMin = 51,  progressMax = 100,          diffMin = 0.18f, diffMax = 0.30f, baseDiff = 0.24f, weeks = 8 },
            new Tier { name = "Tier4",   progressMin = 101, progressMax = 150,          diffMin = 0.25f, diffMax = 0.40f, baseDiff = 0.32f, weeks = 8 },
            new Tier { name = "Tier5",   progressMin = 151, progressMax = 200,          diffMin = 0.35f, diffMax = 0.55f, baseDiff = 0.45f, weeks = 8 },
            new Tier { name = "Endless", progressMin = 201, progressMax = int.MaxValue, diffMin = 0.45f, diffMax = 0.65f, baseDiff = 0.52f, weeks = 52 },
        };

        public DateTime Epoch =>
            DateTime.TryParseExact(epochDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var d) ? d.Date : new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Tier index for a player's DailyProgress. Progress below the first tier still
        /// maps to tier 0 — whether the feature is unlocked at all is the caller's gate.</summary>
        public int TierOf(int dailyProgress)
        {
            for (int i = 0; i < tiers.Length; i++)
                if (dailyProgress <= tiers[i].progressMax) return i;
            return tiers.Length - 1;
        }

        /// <summary>Days since epoch for a UTC time — the calendar index all players share.</summary>
        public int DayIndex(DateTime utc) => (int)(utc.Date - Epoch).TotalDays;

        /// <summary>Rotating pool slot for a calendar day (poolSize % 7 == 0 keeps weekdays aligned).</summary>
        public int SlotOf(int tier, int dayIndex)
        {
            int n = tiers[tier].PoolSize;
            return ((dayIndex % n) + n) % n;
        }

        /// <summary>
        /// Build-time target for one pool slot — the same LevelTarget shape the campaign curve
        /// produces, so PoolBuildRunner is blind to the source. level = slot + 1 (1-based assets,
        /// pack index = slot).
        /// </summary>
        public CampaignCurveConfig.LevelTarget GetSlotTarget(int tier, int slot)
        {
            var tr = tiers[tier];
            float d = tr.baseDiff * weekdayMult[slot % 7];
            d += (Hash01((uint)((tier + 1) * 8191 + slot)) * 2f - 1f) * jitter;
            d = Mathf.Clamp(d, tr.diffMin, tr.diffMax);

            int w = Mathf.RoundToInt(d * campaign.weightCap);
            int tol = Mathf.Max(campaign.minTolWeight, (int)(campaign.matchTol * w));
            return new CampaignCurveConfig.LevelTarget
            {
                level = slot + 1,
                boardSize = campaign.FitSize(w, campaign.sizeRotation[slot % campaign.sizeRotation.Length]),
                difficulty = d,
                targetWeight = w,
                minWeight = Mathf.Max(1, w - tol),
                maxWeight = w + tol,
                role = campaign.RoleOf(d),   // Milestone never occurs — dailies have none
            };
        }

        /// <summary>All targets of one tier's pool — the builder's shopping list.</summary>
        public System.Collections.Generic.List<CampaignCurveConfig.LevelTarget> GetTierTargets(int tier)
        {
            int n = tiers[tier].PoolSize;
            var list = new System.Collections.Generic.List<CampaignCurveConfig.LevelTarget>(n);
            for (int s = 0; s < n; s++) list.Add(GetSlotTarget(tier, s));
            return list;
        }

        static float Hash01(uint x)   // lowbias32 — same hash family as the campaign curve
        {
            x ^= x >> 16; x *= 0x7feb352d;
            x ^= x >> 15; x *= 0x846ca68b;
            x ^= x >> 16;
            return x / (float)uint.MaxValue;
        }
    }
}
