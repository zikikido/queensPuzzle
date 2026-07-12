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

        public float DurationOf(int i) =>
            frameDurations != null && i < frameDurations.Length && frameDurations[i] > 0f
                ? frameDurations[i] : FallbackDuration;
    }
}
