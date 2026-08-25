using Spine.Unity;
using UnityEngine;

namespace qp {
    /// <summary>
    /// Plays a SOSpineController on the attached SkeletonAnimation — same API as
    /// MBSpriteFlipbook so game code doesn't care which one it's talking to. The controller's
    /// default state starts on enable; Play("Happy") switches state by name. A non-looping
    /// state chains into its 'next' state (after 'nextDelay' on the last pose) or holds the
    /// last pose when it has none. Instances stay in sync because callers play them all in
    /// the same frame (PlayQueens), not through any clock seeding here.
    /// </summary>
    public class MBSpinePlayer : MonoBehaviour {

        [SerializeField] SOSpineController _controller;

        SkeletonAnimation _skeleton;
        SOSpineController.State _state;   // what's playing now
        float _wait;       // >0: counting down on the last pose (to next state or loop restart)
        bool _restart;     // what _wait leads to: restart this loop (true) or enter next (false)
        bool _done;        // non-loop, no next: finished — hold the last pose

        public SOSpineController Controller => _controller;
        public string CurrentState => _state != null ? _state.name : null;

        // Per-state playback speed; guarded so a 0 (or negative) never divides to infinity.
        static float Speed(SOSpineController.State s) => s.speed > 0.01f ? s.speed : 1f;

        void Awake() {
            _skeleton = GetComponent<SkeletonAnimation>();
            if (_skeleton != null) _skeleton.UnscaledTime = true;   // match the flipbook's unscaled clock

            // the controller's dropdowns were picked from ITS data asset — if the renderer plays
            // a different one, the configured animation names may simply not exist there
            var renderer = GetComponent<SkeletonRenderer>();
            if (_controller != null && _controller.skeletonDataAsset != null && renderer != null) {
                if (renderer.skeletonDataAsset == null) {
                    // import hiccups (the 4.3 deprecated-field upgrade) can strip the renderer's
                    // reference — the controller knows what this character plays, so heal from it
                    renderer.skeletonDataAsset = _controller.skeletonDataAsset;
                    renderer.Initialize(true);              // it may have already initialized empty
                    if (_skeleton != null) _skeleton.Initialize(true);
                    Debug.LogWarning($"[MBSpinePlayer] SkeletonRenderer on {name} had no SkeletonData — " +
                        $"assigned '{_controller.skeletonDataAsset.name}' from controller '{_controller.name}'", this);
                } else if (renderer.skeletonDataAsset != _controller.skeletonDataAsset) {
                    Debug.LogError($"[MBSpinePlayer] Data mismatch on {name}: controller '{_controller.name}' is set up for " +
                        $"'{_controller.skeletonDataAsset.name}' but SkeletonRenderer plays " +
                        $"'{renderer.skeletonDataAsset.name}'", this);
                }
            }
        }

        void OnEnable() => PlayDefault();

        /// <summary>Enter the controller's default state — a (none) default means: sit still
        /// on the setup pose until someone calls Play().</summary>
        public void PlayDefault() {
            if (_controller != null) Play(_controller.Default);
        }

        /// <summary>Total length (seconds) of a state's clip; 0 when the state is unknown.</summary>
        public float StateLength(string stateName) {
            var s = _controller != null ? _controller.Find(stateName) : null;
            var anim = s != null ? _FindAnim(s) : null;
            return anim != null ? anim.Duration / Speed(s) : 0f;
        }

        /// <summary>
        /// Show a state's first frame immediately, without starting playback. Used to seed the
        /// starting pose — e.g. a Queen begins on the Idle first frame — until Play() actually
        /// runs the animation.
        /// </summary>
        public void StartFrame(string stateName) {
            var s = _controller != null ? _controller.Find(stateName) : null;
            var anim = s != null ? _FindAnim(s) : null;
            if (anim == null) return;
            _state = null;   // stop our state logic so Update() won't chain from this
            var entry = _skeleton.AnimationState.SetAnimation(0, anim, false);
            entry.MixDuration = 0f;
            entry.TimeScale = 0f;   // freeze on the first frame
        }

        /// <summary>Play a state by name (case-insensitive), e.g. Play("Happy").</summary>
        public void Play(string stateName) {
            var s = _controller != null ? _controller.Find(stateName) : null;
            if (s == null) { Debug.LogWarning($"[MBSpinePlayer] No state '{stateName}' on {name}"); return; }
            Play(s);
        }

        void Play(SOSpineController.State s, float mixOverride = -1f) {
            var anim = s != null ? _FindAnim(s) : null;
            if (anim == null) {
                if (s != null && !string.IsNullOrEmpty(s.spineAnim))
                    Debug.LogWarning($"[MBSpinePlayer] Animation '{s.spineAnim}' (state '{s.name}') not found in skeleton data on {name}", this);
                return;
            }
            _state = s;
            _wait = 0f;
            _done = false;

            // A loop with a rest between cycles plays as one-shot and is restarted by Update();
            // a plain loop is handed to Spine. Board-wide sync comes from the callers playing
            // every instance in the same frame (PlayQueens) — no clock seeding needed.
            bool plainLoop = s.loop && s.loopDelay <= 0f;
            var entry = _skeleton.AnimationState.SetAnimation(0, anim, plainLoop);
            entry.TimeScale = Speed(s);
            entry.MixDuration = mixOverride >= 0f ? mixOverride : s.mix;
        }

        // Chain into _state.next, honoring its nextMix override.
        void _PlayNext() {
            var from = _state;
            var s = _controller != null ? _controller.Find(from.next) : null;
            if (s == null) { Debug.LogWarning($"[MBSpinePlayer] No state '{from.next}' on {name}"); return; }
            Play(s, from.nextMix);
        }

        void Update() {
            if (_state == null || _done) return;

            if (_wait > 0f) {   // resting on the last pose
                _wait -= Time.unscaledDeltaTime;
                if (_wait > 0f) return;
                if (_restart) { var s = _state; _state = null; Play(s); }   // loop cycle rest served
                else _PlayNext();                                           // next-delay served — chain
                return;
            }

            if (_state.loop && _state.loopDelay <= 0f) return;   // plain loop — Spine handles it

            var entry = _skeleton.AnimationState.GetTrack(0);
            if (entry == null || !entry.IsComplete) return;

            if (_state.loop) { _restart = true; _wait = _state.loopDelay / Speed(_state); }
            else if (string.IsNullOrEmpty(_state.next)) _done = true;   // hold the last pose
            else if (_state.nextDelay > 0f) { _restart = false; _wait = _state.nextDelay / Speed(_state); }
            else _PlayNext();   // 0 delay — chain this same frame
        }

        Spine.Animation _FindAnim(SOSpineController.State s) {
            if (string.IsNullOrEmpty(s.spineAnim)) return null;
            if (_skeleton == null) _skeleton = GetComponent<SkeletonAnimation>();   // if never awake
            if (_skeleton == null || _skeleton.SkeletonDataAsset == null) return null;
            var data = _skeleton.SkeletonDataAsset.GetSkeletonData(false);
            return data != null ? data.FindAnimation(s.spineAnim) : null;
        }
    }
}
