using UnityEngine;

namespace qp {

    /// <summary>
    /// Reskin-side show visuals for the streak info popup: rewinds the Animator on every open and
    /// plays the "Show" cascade (phase 1 appear → phase 2 on the %-marked objects). Lives in the
    /// reskin folder so MBDailyStreakInfoPopup carries no skin-specific references. Extra beats
    /// (SFX, the old→new count-up) hook in later as animation events.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class MBDailyStreakInfoPopupAnim : MonoBehaviour, IPopupAnim {

        static readonly int ShowState = Animator.StringToHash("Show");

        Animator _animator;

        void Awake() => _animator = GetComponent<Animator>();

        /// <summary>Rewinds and plays the show cascade. Driven by MBDailyStreakInfoPopup.Show.</summary>
        public void PlayIn() {
            _animator.Play(ShowState, 0, 0f);

            // Evaluate frame 0 now, or the popup renders one frame at rest values before the
            // Animator's own update lands. Guarded because Update asserts if it hasn't awoken yet.
            if (_animator.isInitialized) _animator.Update(0f);
        }

        // ---- animation events (wired from the Show clip) --------------------------------

        /// <summary>Animation event — the old→new streak count-up hooks in here later.</summary>
        public void PlayCountUp() { }
    }
}
