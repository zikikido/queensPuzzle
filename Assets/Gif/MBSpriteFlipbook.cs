using UnityEngine;
using UnityEngine.UI;

namespace qp {
    /// <summary>
    /// Plays a SOFlipbookController on the attached Image (UI) or SpriteRenderer. The
    /// controller's default state starts on enable; Play("Happy") switches state by name.
    /// A non-looping state chains into its 'next' state (after 'nextDelay' on the last frame)
    /// or holds the last frame when it has none. Looping states run off the GLOBAL unscaled
    /// clock, so every instance in the same state shows the same frame at the same moment
    /// (all puppies blink together, whenever they spawned).
    /// </summary>
    public class MBSpriteFlipbook : MonoBehaviour {

        [SerializeField] SOFlipbookController _controller;

        Image _image;
        SpriteRenderer _sr;
        SOFlipbookController.State _state;   // what's playing now
        int _frame;
        float _left;       // non-loop: time left on the current frame (or on the next-delay)
        bool _waitNext;    // non-loop: last frame done, waiting nextDelay before the next state
        bool _done;        // non-loop, no next: finished — hold the last frame
        float[] _ends;     // loop: cumulative end time of each frame within one cycle
        float _cycle;      // loop: full cycle length

        public SOFlipbookController Controller => _controller;
        public string CurrentState => _state != null ? _state.name : null;

        void Awake() {
            _image = GetComponent<Image>();
            if (_image == null) _sr = GetComponent<SpriteRenderer>();
        }

        void OnEnable() => PlayDefault();

        /// <summary>Enter the controller's default state — a (none) default means: sit still
        /// on the authored sprite until someone calls Play().</summary>
        public void PlayDefault() {
            if (_controller != null) Play(_controller.Default);
        }

        /// <summary>Total length (seconds) of a state's clip; 0 when the state is unknown.</summary>
        public float StateLength(string stateName) {
            var s = _controller != null ? _controller.Find(stateName) : null;
            return s != null && s.anim != null ? s.anim.Length : 0f;
        }

        /// <summary>Play a state by name (case-insensitive), e.g. Play("Happy").</summary>
        public void Play(string stateName) {
            var s = _controller != null ? _controller.Find(stateName) : null;
            if (s == null) { Debug.LogWarning($"[MBSpriteFlipbook] No state '{stateName}' on {name}"); return; }
            Play(s);
        }

        void Play(SOFlipbookController.State s) {
            if (s == null || s.anim == null || s.anim.frames == null || s.anim.frames.Length == 0) return;
            _state = s;
            _frame = -1;   // force the first Show to apply
            _waitNext = _done = false;
            if (s.loop) { BuildCycle(); SyncToClock(); }
            else { _left = s.anim.DurationOf(0); Show(0); }
        }

        // Cumulative frame end-times over one loop cycle; the loop delay extends the first
        // frame, so the rest between cycles happens on the base pose.
        void BuildCycle() {
            var anim = _state.anim;
            _ends = new float[anim.frames.Length];
            float t = 0f;
            for (int i = 0; i < anim.frames.Length; i++) {
                t += anim.DurationOf(i) + (i == 0 ? _state.loopDelay : 0f);
                _ends[i] = t;
            }
            _cycle = t;
        }

        void Update() {
            if (_state == null || _done) return;

            if (_state.loop) { SyncToClock(); return; }

            _left -= Time.unscaledDeltaTime;
            while (_left <= 0f) {
                if (_waitNext) { Play(_state.next); return; }   // delay served — chain
                if (_frame >= _state.anim.frames.Length - 1) {
                    if (string.IsNullOrEmpty(_state.next)) { _done = true; return; }   // hold last frame
                    _waitNext = true;
                    _left += _state.nextDelay;   // 0 delay → chains this same frame
                } else {
                    Show(_frame + 1);
                    _left += _state.anim.DurationOf(_frame);
                }
            }
        }

        // Every instance reads the same clock, so all of them show the same frame.
        void SyncToClock() {
            if (_cycle <= 0f) return;
            float t = Time.unscaledTime % _cycle;
            int i = 0;
            while (i < _ends.Length - 1 && t >= _ends[i]) i++;
            Show(i);
        }

        void Show(int i) {
            if (i == _frame) return;
            _frame = i;
            if (_image != null) _image.sprite = _state.anim.frames[i];
            else if (_sr != null) _sr.sprite = _state.anim.frames[i];
        }
    }
}
