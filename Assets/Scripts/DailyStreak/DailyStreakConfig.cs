using System;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Tunables for the daily streak, shipped as a single asset in Resources ("DailyStreakConfig").
    /// Holds the cycle length and the per-tier reward list. The milestone DAYS are derived from the
    /// cycle by <see cref="DailyStreakManager.Milestones"/> (1/3, 2/3, full), so rewards here are
    /// ordered by tier: [0] = first milestone (small) … [2] = last (best). The manager reads this on
    /// demand; if the asset is missing the feature is treated as off.
    /// </summary>
    [CreateAssetMenu(menuName = "QP/Daily Streak Config", fileName = "DailyStreakConfig")]
    public class DailyStreakConfig : ScriptableObject {

        [Tooltip("Streak wraps back to 1 after this many days. Milestones are 1/3, 2/3, full (21 → 7/14/21).")]
        public int cycleLength = 21;

        [Tooltip("Reward per milestone tier, ascending: [0] first milestone (small) … [2] last (best).")]
        public Reward[] rewards;

        /// <summary>The reward for a milestone tier (0-based), or null when out of range.</summary>
        public Reward RewardForTier(int tier) =>
            rewards != null && tier >= 0 && tier < rewards.Length ? rewards[tier] : null;

        [Tooltip("Sprite shown for each boost type in the reward UI — swap here to change dynamically.")]
        public BoostSprite[] boostSprites;

        /// <summary>The configured sprite for a boost type, or null if none is set.</summary>
        public Sprite SpriteFor(EBoostType type) {
            if (boostSprites != null)
                foreach (var b in boostSprites)
                    if (b.type == type) return b.sprite;
            return null;
        }

        [Serializable]
        public class BoostSprite {
            public EBoostType type;
            public Sprite sprite;
        }
    }
}
