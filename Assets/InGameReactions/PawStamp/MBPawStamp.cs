using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// Pawdoku's own reaction: a big paw stamps down on the cell with an impact squash, then
    /// small paw prints trot away along a curved path, alternating left/right like footsteps,
    /// and fade. One-shot. Check <see cref="UI"/> to render through UI Images (place under
    /// its own Canvas). Editor previews drive it via Init() + Evaluate(); runtime uses Play().
    /// </summary>
    public class MBPawStamp : MonoBehaviour, IInGameReactionFX {

        public bool UI;
        public Sprite PawSprite;
        public int Steps = 5;            // trail paw prints
        public int Seed = 4;
        public float Speed = 1f;
        public float StampSize = 0.85f;
        public float StepSize = 0.32f;
        public float StepGap = 0.42f;    // distance between trail steps
        public Color[] Tints = {         // every print picks one — colorful trail
            new Color(1f, 0.35f, 0.45f),     // pink-red
            new Color(1f, 0.62f, 0.2f),      // orange
            new Color(1f, 0.82f, 0.25f),     // gold
            new Color(0.4f, 0.8f, 0.45f),    // green
            new Color(0.35f, 0.65f, 1f),     // blue
            new Color(0.75f, 0.5f, 1f),      // purple
        };

        bool _ui;
        SpriteRenderer[] _sr;
        Image[] _img;
        float[] _delay, _life, _rot;
        Vector3[] _pos;
        Color[] _tint;
        float _timeline;

        public float Duration => Speed > 0.01f ? _timeline / Speed : _timeline;

        int _total => 1 + Steps;

        public void Init() {
            if (_delay != null) return;
            _ui = UI;
            int total = _total;
            if (_ui) _img = new Image[total]; else _sr = new SpriteRenderer[total];
            _delay = new float[total]; _life = new float[total]; _rot = new float[total];
            _pos = new Vector3[total];
            _tint = new Color[total];

            var old = Random.state;
            Random.InitState(Seed);
            // the trail walks away in a gently curving direction, biased upward
            float walkAng = Random.Range(50f, 130f) * Mathf.Deg2Rad;
            float curve = Random.Range(-0.35f, 0.35f);   // radians of bend over the whole trail

            for (int i = 0; i < total; i++) {
                if (i == 0) {                                        // the stamp itself
                    _delay[i] = 0f;
                    _life[i] = 0.9f;
                    _pos[i] = Vector3.zero;
                    _rot[i] = Random.Range(-10f, 10f);
                } else {                                             // trail step i
                    int k = i - 1;
                    float ang = walkAng + curve * (k / Mathf.Max(1f, Steps - 1f));
                    var dir = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
                    var perp = new Vector3(-dir.y, dir.x, 0f);
                    _delay[i] = 0.3f + k * 0.13f;
                    _life[i] = 0.8f;
                    _pos[i] = dir * (StepGap * (k + 1)) + perp * (k % 2 == 0 ? 0.13f : -0.13f);
                    _rot[i] = ang * Mathf.Rad2Deg - 90f;             // print faces along the walk
                }
                _tint[i] = Tints[(i + Random.Range(0, Tints.Length)) % Tints.Length];
                _makeItem(i);
                _timeline = Mathf.Max(_timeline, _delay[i] + _life[i]);
            }
            Random.state = old;
            // trail draws behind the stamp; creation order already does that in UI too
        }

        void _makeItem(int i) {
            var child = transform.Find("item" + i);
            var go = child != null ? child.gameObject : new GameObject("item" + i);
            go.transform.SetParent(transform, false);
            if (_ui) {
                _strip<SpriteRenderer>(go);
                var img = go.GetComponent<Image>();
                if (img == null) img = go.AddComponent<Image>();
                img.sprite = PawSprite;
                img.raycastTarget = false;
                img.rectTransform.sizeDelta = Vector2.one;
                img.enabled = false;
                if (i == 0) go.transform.SetAsLastSibling();   // stamp above its trail
                _img[i] = img;
            } else {
                _strip<Image>(go);
                var sr = go.GetComponent<SpriteRenderer>();
                if (sr == null) sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = PawSprite;
                sr.sortingOrder = i == 0 ? 310 : 300 + i;
                sr.enabled = false;
                _sr[i] = sr;
            }
        }

        static void _strip<T>(GameObject go) where T : Component {
            var c = go.GetComponent<T>();
            if (c == null) return;
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
        }

        /// <summary>Drop the cached choreography and rebuild from the current inspector values.</summary>
        public void Rebuild() {
            Hide();
            _delay = null; _sr = null; _img = null;
            _timeline = 0f;
            for (int i = _total; ; i++) {
                var c = transform.Find("item" + i);
                if (c == null) break;
                if (Application.isPlaying) Destroy(c.gameObject);
                else DestroyImmediate(c.gameObject);
            }
            Init();
        }

        public void Play(Vector3 worldPos) {
            transform.position = worldPos;
            Play();
        }

        /// <summary>Play in place (UI: leave the RectTransform where it is).</summary>
        public void Play() {
            Init();
            _avoidBackdrop();
            StopAllCoroutines();
            StartCoroutine(_run());
        }

        // Re-pick any tint that would blend into the cell color under the stamp.
        void _avoidBackdrop() {
            var probe = ReactionBackdrop.ColorAt;
            var bg = probe != null ? probe(transform.position) : null;
            if (bg == null) return;
            for (int i = 0; i < _tint.Length; i++) {
                var c = _tint[i];
                for (int guard = 0; guard < Tints.Length * 2 && _close(c, bg.Value); guard++)
                    c = Tints[Random.Range(0, Tints.Length)];
                if (_close(c, bg.Value)) c = Color.white;   // palette exhausted — white always reads
                _tint[i] = c;
            }
        }

        static bool _close(Color a, Color b) {
            float dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b;
            return dr * dr + dg * dg + db * db < 0.12f;
        }

        IEnumerator _run() {
            for (float t = 0f; t < Duration; t += Time.unscaledDeltaTime) {
                Evaluate(t);
                yield return null;
            }
            Hide();
        }

        /// <summary>Set every item's state for time t — pure, so editor code can scrub it.</summary>
        public void Evaluate(float t) {
            t *= Speed;
            for (int i = 0; i < _delay.Length; i++) {
                float p = (t - _delay[i]) / _life[i];
                if (p < 0f || p > 1f) { _off(i); continue; }

                if (i == 0) {                                                   // the stamp
                    // falls in: big + transparent -> lands at full size (ease-in = accelerating)
                    float drop = Mathf.Clamp01(p / 0.2f);
                    float s = StampSize * Mathf.LerpUnclamped(1.7f, 1f, drop * drop);
                    // impact squash right after landing
                    float sq = p > 0.2f && p < 0.34f ? Mathf.Sin((p - 0.2f) / 0.14f * Mathf.PI) * 0.14f : 0f;
                    var c = _tint[i];
                    c.a = Mathf.Clamp01(p / 0.12f) * (1f - _smooth01(0.72f, 1f, p));
                    _apply(i, _pos[i], _rot[i], s * (1f + sq), s * (1f - sq), c);
                } else {                                                        // trail print
                    float pop = _easeOutBack(Mathf.Clamp01(p / 0.2f));
                    float s = StepSize * pop;
                    var c = _tint[i];
                    c.a = Mathf.Clamp01(p / 0.12f) * (1f - _smooth01(0.6f, 1f, p));
                    _apply(i, _pos[i], _rot[i], s, s, c);
                }
            }
        }

        void _apply(int i, Vector3 pos, float zRot, float scaleX, float scaleY, Color c) {
            Transform tr;
            if (_ui) {
                var img = _img[i];
                img.enabled = true;
                img.color = c;
                tr = img.transform;
            } else {
                var sr = _sr[i];
                sr.enabled = true;
                sr.color = c;
                tr = sr.transform;
            }
            tr.localPosition = pos;
            tr.localRotation = Quaternion.Euler(0f, 0f, zRot);
            tr.localScale = new Vector3(scaleX, scaleY, 1f);
        }

        void _off(int i) {
            if (_ui) { if (_img[i] != null) _img[i].enabled = false; }
            else if (_sr[i] != null) _sr[i].enabled = false;
        }

        public void Hide() {
            if (_delay == null) return;
            for (int i = 0; i < _delay.Length; i++) _off(i);
        }

        static float _easeOutBack(float x) {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float pp = x - 1f;
            return 1f + c3 * pp * pp * pp + c1 * pp * pp;
        }

        // edge-style smoothstep (Mathf.SmoothStep interpolates, which is not what fades need)
        static float _smooth01(float e0, float e1, float x) {
            float u = Mathf.Clamp01((x - e0) / (e1 - e0));
            return u * u * (3f - 2f * u);
        }
    }
}
