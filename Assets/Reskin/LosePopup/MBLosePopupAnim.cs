using Common;
using UnityEngine;

namespace qp {
    /// <summary>
    /// Reskin-side show visuals for the lose popup: rewinds the Animator on every open and
    /// receives the animation events fired by LosePopupShow.anim. Lives in the reskin folder
    /// so MBFailPopup itself carries no skin-specific references.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class MBLosePopupAnim : MonoBehaviour {

        static readonly int ShowState = Animator.StringToHash("Show");

        Animator _animator;
        MBSpriteFlipbook _char;

        void Awake() {
            _animator = GetComponent<Animator>();
            _char = transform.RecursiveFindChild<MBSpriteFlipbook>("$Char");
        }

        // The popup is re-enabled for every loss (and once more by the layout pass in
        // MBFailPopup.Start), so the Animator would otherwise resume wherever it left off.
        // Update(0) evaluates frame 0 right away — without it the first frame shows rest values.
        void OnEnable() {
            _animator.Play(ShowState, 0, 0f);
            _animator.Update(0f);
        }

        /// <summary>Animation event. Starts the character flipbook; the lose "In" state loops.</summary>
        public void PlayCharIn() {
            if (_char != null) _char.Play("In");
        }
    }
}
