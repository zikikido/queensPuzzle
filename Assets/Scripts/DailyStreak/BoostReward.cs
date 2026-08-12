using UnityEngine;

namespace qp {

    /// <summary>A reward that grants a number of a single boost type.</summary>
    [CreateAssetMenu(menuName = "QP/Rewards/Boost Reward", fileName = "BoostReward")]
    public class BoostReward : Reward {

        public EBoostType type;
        [Min(1)] public int amount = 1;

        public override void Grant() => AppData.Boosts[type].Value += amount;

        public override string Label => $"x{amount}";
    }
}
