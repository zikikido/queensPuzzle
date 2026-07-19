using Common;
using UnityEngine;

namespace qp {
    /// <summary>
    /// Reskin-side show visuals for the lose popup: rewinds the Animator on every open and
    /// receives the animation events fired by LosePopupShow.anim. Lives in the reskin folder
    /// so MBFailPopup itself carries no skin-specific references.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class MBLosePopupAnim : MonoBehaviour, IPopupAnim {

        static readonly int ShowState = Animator.StringToHash("Show");

        Animator _animator;
        MBSpriteFlipbook _char;

        void Awake() {
            _animator = GetComponent<Animator>();
            _char = transform.RecursiveFindChild<MBSpriteFlipbook>("$Char");
        }

        /// <summary>Rewinds and plays the show cascade. Driven by MBFailPopup.Show.</summary>
        public void PlayIn() {
            // Clear the previous run's last frame so the dog opens on the neutral "In" first frame
            // instead of flashing the stale pose until PlayCharIn fires later in the cascade.
            if (_char != null) _char.StartFrame("In");

            _animator.Play(ShowState, 0, 0f);

            // Evaluate frame 0 now, or the popup renders one frame at rest values before the
            // Animator's own update lands. Guarded because Update asserts on m_DidAwake if the
            // Animator has not awoken yet.
            if (_animator.isInitialized) _animator.Update(0f);
        }

        /// <summary>Animation event. Starts the character flipbook; the lose "In" state loops.</summary>
        public void PlayCharIn() {
            if (_char != null) _char.Play("In");
        }
    }
}
