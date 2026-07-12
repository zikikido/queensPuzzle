using UnityEngine;

namespace qp {
    /// <summary>
    /// One flipbook animation clip (IDLE / HAPPY / CRY / ...): just the frames and their
    /// timings. How it PLAYS (loop, what comes next) is the controller's business —
    /// see SOFlipbookController.
    /// </summary>
    [CreateAssetMenu(fileName = "FlipbookAnim", menuName = "QueensPuzzle/Flipbook Animation")]
    public class SOFlipbookAnim : ScriptableObject {

        const float FallbackDuration = 0.1f;   // for missing/zero entries — a frame must never be 0s

        public Sprite[] frames;
        public float[] frameDurations;   // seconds per frame, one entry per frame (GIF timeline)

        [SerializeField] float _length;  // auto-calculated (OnValidate) — read-only, don't edit

        /// <summary>Total clip length in seconds (sum of all frame durations).</summary>
        public float Length => _length;

        public float DurationOf(int i) =>
            frameDurations != null && i < frameDurations.Length && frameDurations[i] > 0f
                ? frameDurations[i] : FallbackDuration;

        // keeps _length correct on every edit/import in the editor; builds read the saved value
        void OnValidate() {
            _length = 0f;
            if (frames == null) return;
            for (int i = 0; i < frames.Length; i++) _length += DurationOf(i);
        }
    }
}
