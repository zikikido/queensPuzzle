using System;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Tunables for the daily streak, shipped as a single asset in Resources ("DailyStreakConfig").
    /// Holds the unlock gate, the cycle length, and the milestone → reward table. The manager reads
    /// it on demand; if the asset is missing the feature is treated as off (button stays Lock).
    /// </summary>
    [CreateAssetMenu(menuName = "QP/Daily Streak Config", fileName = "DailyStreakConfig")]
    public class DailyStreakConfig : ScriptableObject {

        [Tooltip("Campaign LevelIdx (0-based) that unlocks the streak.")]
        public int unlockLevelIdx = 21;

        [Tooltip("Streak wraps back to 1 after this many days.")]
        public int cycleLength = 28;

        [Tooltip("Streak day → reward granted on reaching it. Days must be within the cycle (e.g. 7/14/28).")]
        public Milestone[] milestones;

        /// <summary>The reward for reaching a given streak day, or null when it isn't a milestone.</summary>
        public Reward RewardFor(int streak) {
            if (milestones == null) return null;
            foreach (var m in milestones)
                if (m.day == streak) return m.reward;
            return null;
        }

        [Serializable]
        public class Milestone {
            public int day;         // streak day this reward is granted on (7 / 14 / 28)
            public Reward reward;   // small / better / best — escalates with the day
        }
    }
}
