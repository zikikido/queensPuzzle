using UnityEngine;
using UnityEngine.UI;

namespace qp {
    /// <summary>
    /// Plays SOFlipbookAnim assets on the attached Image (UI) or SpriteRenderer.
    /// _animations[0] is the DEFAULT: it auto-plays on enable, and any finished one-shot
    /// (HAPPY, CRY, ...) returns to it. Looping animations run off the GLOBAL unscaled clock,
    /// so every instance playing the same animation shows the same frame at the same moment
    /// (all puppies blink together, whenever they spawned).
    /// </summary>
    public class MBSpriteFlipbook : MonoBehaviour {

        [SerializeField] SOFlipbookAnim[] _animations;   // [0] = default (IDLE)

        Image _image;
        SpriteRenderer _sr;
        SOFlipbookAnim _anim;   // what's playing now
        int _frame;
        float _left;      // one-shot mode: time left on the current frame
        float[] _ends;    // loop mode: cumulative end time of each frame within one cycle
        float _cycle;     // loop mode: full cycle length (all frames + loop delay)

        public SOFlipbookAnim[] Animations => _animations;
        public SOFlipbookAnim Current => _anim;

        void Awake() {
            _image = GetComponent<Image>();
            if (_image == null) _sr = GetComponent<SpriteRenderer>();
        }

        void OnEnable() => PlayDefault();

        /// <summary>Back to _animations[0] (the idle).</summary>
        public void PlayDefault() {
            if (_animations != null && _animations.Length > 0) Play(_animations[0]);
        }

        /// <summary>Play by asset name (case-insensitive), e.g. Play("Happy").</summary>
        public void Play(string animName) {
            if (_animations == null) return;
            foreach (var a in _animations)
                if (a != null && string.Equals(a.name, animName, System.StringComparison.OrdinalIgnoreCase)) {
                    Play(a);
                    return;
                }
            Debug.LogWarning($"[MBSpriteFlipbook] No animation named '{animName}' on {name}");
        }

        public void Play(SOFlipbookAnim anim) {
            if (anim == null || anim.frames == null || anim.frames.Length == 0) return;
            _anim = anim;
            _frame = -1;   // force the first Show to apply
            if (_anim.loop) { BuildCycle(); SyncToClock(); }
            else { _left = _anim.DurationOf(0); Show(0); }
        }

        // Cumulative frame end-times over one cycle; the loop delay extends the first frame.
        void BuildCycle() {
            int n = _anim.frames.Length;
            _ends = new float[n];
            float t = 0f;
            for (int i = 0; i < n; i++) {
                t += _anim.DurationOf(i) + (i == 0 ? _anim.loopDelay : 0f);
                _ends[i] = t;
            }
            _cycle = t;
        }

        void Update() {
            if (_anim == null || _anim.frames.Length < 2) return;

            if (_anim.loop) { SyncToClock(); return; }

            _left -= Time.unscaledDeltaTime;
            while (_left <= 0f) {
                if (_frame >= _anim.frames.Length - 1) {
                    // one-shot finished — settle back into the default (unless this IS it)
                    if (_animations != null && _animations.Length > 0 && _animations[0] != _anim) PlayDefault();
                    return;
                }
                Show(_frame + 1);
                _left += _anim.DurationOf(_frame);
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
            if (_image != null) _image.sprite = _anim.frames[i];
            else if (_sr != null) _sr.sprite = _anim.frames[i];
        }
    }
}
