using UnityEngine;

namespace qp {

    /// <summary>
    /// Tunables for the daily streak, shipped as a single asset in Resources ("DailyStreakConfig").
    /// One reward per cycle: the streak runs 1..<see cref="cycleLength"/> and the gift is handed out
    /// on the final day, picked at random from <see cref="rewards"/>. The manager reads this on
    /// demand; if the asset is missing the feature is treated as off.
    /// </summary>
    [CreateAssetMenu(menuName = "QP/Daily Streak Config", fileName = "DailyStreakConfig")]
    public class DailyStreakConfig : ScriptableObject {

        [Tooltip("Streak wraps back to 1 after this many days; the gift is granted on the last day.")]
        public int cycleLength = 7;

        [Tooltip("Possible gifts — one is chosen at random when the streak completes.")]
        public Reward[] rewards;

        /// <summary>A random reward from the list, or null when the list is empty.</summary>
        public Reward RewardRandom() =>
            rewards != null && rewards.Length > 0 ? rewards[Random.Range(0, rewards.Length)] : null;

        [Tooltip("Sprite shown for each boost type in the reward UI — swap here to change dynamically.")]
        public BoostSprite[] boostSprites;

        /// <summary>The configured sprite for a boost type, or null if none is set.</summary>
        public Sprite SpriteFor(EBoostType type) {
            if (boostSprites != null)
                foreach (var b in boostSprites)
                    if (b.type == type) return b.sprite;
            return null;
        }

        [System.Serializable]
        public class BoostSprite {
            public EBoostType type;
            public Sprite sprite;
        }
    }
}
