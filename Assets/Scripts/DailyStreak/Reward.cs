using UnityEngine;

namespace qp {

    /// <summary>
    /// A grantable reward. Base type for everything the game can hand out (streak milestones today,
    /// more sources later). Concrete rewards are ScriptableObject assets so designers set the
    /// amounts/icons in the inspector.
    /// </summary>
    public abstract class Reward : ScriptableObject {

        [Tooltip("Sprite the UI shows for this reward.")]
        public Sprite icon;

        /// <summary>Apply the reward to the player (add boosts, coins, …).</summary>
        public abstract void Grant();

        /// <summary>Short human label for the amount, e.g. "x3" — for the reward UI.</summary>
        public abstract string Label { get; }
    }
}
