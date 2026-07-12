using UnityEngine;

namespace qp {
    /// <summary>
    /// One flipbook animation (IDLE / HAPPY / CRY / ...) as an authorable asset: the frame list
    /// plus timing. MBSpriteFlipbook holds a set of these and plays them by name or reference.
    /// </summary>
    [CreateAssetMenu(fileName = "FlipbookAnim", menuName = "QueensPuzzle/Flipbook Animation")]
    public class SOFlipbookAnim : ScriptableObject {

        const float FallbackDuration = 0.1f;   // for missing/zero entries — a frame must never be 0s

        public Sprite[] frames;
        public float[] frameDurations;       // seconds per frame, one entry per frame (GIF timeline)
        public bool loop = true;
        public float loopDelay = 0f;         // extra rest on the first frame between cycles

        public float DurationOf(int i) =>
            frameDurations != null && i < frameDurations.Length && frameDurations[i] > 0f
                ? frameDurations[i] : FallbackDuration;
    }
}
