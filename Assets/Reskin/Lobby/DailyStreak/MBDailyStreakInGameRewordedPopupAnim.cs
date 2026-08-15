using UnityEngine;

namespace qp {

    /// <summary>
    /// Reskin-side show visuals for the streak REWARD popup: rewinds the Animator on every open and
    /// plays the "Show" cascade over the %-marked objects. Lives in the reskin folder so
    /// MBDailyStreakInGameRewordedPopup carries no skin-specific references.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class MBDailyStreakInGameRewordedPopupAnim : MonoBehaviour, IPopupAnim {

        static readonly int ShowState = Animator.StringToHash("Show");
        static readonly int RewardState = Animator.StringToHash("Reward");

        Animator _animator;

        void Awake() => _animator = GetComponent<Animator>();

        /// <summary>Rewinds and plays the show cascade. Driven by MBDailyStreakInGameRewordedPopup.Show.</summary>
        public void PlayIn() {
            _animator.Play(ShowState, 0, 0f);
            if (_animator.isInitialized) _animator.Update(0f);
        }

        /// <summary>Phase 3 — the gift opens and the reward fades in. Driven after the streak beats.</summary>
        public void PlayReward() {
            _animator.Play(RewardState, 0, 0f);
            if (_animator.isInitialized) _animator.Update(0f);
        }

        /// <summary>True once the Show clip has played through (it's a one-shot, so it holds at the end).</summary>
        public bool IsFinished() {
            if (_animator == null || !_animator.isInitialized) return true;
            var st = _animator.GetCurrentAnimatorStateInfo(0);
            return !st.IsName("Show") || st.normalizedTime >= 1f;
        }
    }
}
