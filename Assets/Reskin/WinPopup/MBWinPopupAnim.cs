using Coffee.UIExtensions;
using Common;
using UnityEngine;

namespace qp {
    /// <summary>
    /// Reskin-side show visuals for the win popup: rewinds the Animator on every open and
    /// receives the animation events fired by WinPopupShow.anim. Lives in the reskin folder
    /// so MBWinPopup itself carries no skin-specific references.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class MBWinPopupAnim : MonoBehaviour, IPopupAnim {

        static readonly int ShowState = Animator.StringToHash("Show");

        Animator _animator;
        MBSpriteFlipbook _char;

        void Awake() {
            _animator = GetComponent<Animator>();
            _char = transform.RecursiveFindChild<MBSpriteFlipbook>("$Char");
        }

        /// <summary>Rewinds and plays the show cascade. Driven by MBWinPopup.Show.</summary>
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

        /// <summary>Animation event. Starts the character flipbook; its controller chains In -> Idle.</summary>
        public void PlayCharIn() {
            if (_char != null) _char.Play("In");
        }

        /// <summary>Animation event. Fires the particle system named by the event's string argument.</summary>
        public void PlayPS(string psName) {
            var t = transform.RecursiveFindChild(psName);
            if (t == null) return;

            // UIParticle wraps the emitter to render it inside the canvas and owns its play
            // state — going straight to ParticleSystem.Play would bypass that bookkeeping.
            var uip = t.GetComponent<UIParticle>();
            if (uip != null) { uip.Play(); return; }

            var ps = t.GetComponent<ParticleSystem>();
            if (ps != null) ps.Play();
        }
    }
}
