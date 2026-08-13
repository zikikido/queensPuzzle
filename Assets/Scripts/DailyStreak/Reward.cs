using System;
using UnityEngine;

namespace qp {

    /// <summary>
    /// A milestone reward — a plain serializable class configured inline in <see cref="DailyStreakConfig"/>
    /// (no separate asset). Grants a number of a single boost. Add fields/types here as more reward
    /// kinds appear.
    /// </summary>
    [Serializable]
    public class Reward {

        public EBoostType type;
        [Min(1)] public int amount = 1;

        /// <summary>Apply the reward to the player.</summary>
        public void Grant() => AppData.Boosts[type].Value += amount;

        /// <summary>Short amount label for the reward UI, e.g. "x3".</summary>
        public string Label => $"x{amount}";
    }
}
