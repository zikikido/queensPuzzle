using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// The queen's reaction: a golden crown drops onto the cell, lands with a squash-bounce
    /// and a burst of sparkles, holds with a proud little shine wiggle, then fades away.
    /// One-shot. Check <see cref="UI"/> to render through UI Images. Editor previews drive
    /// it via Init() + Evaluate(); runtime uses Play().
    /// </summary>
    public class MBCrownDrop : MonoBehaviour, IInGameReactionFX {

        public bool UI;
        public Sprite CrownSprite;
        public Sprite SparkleSprite;
        public int Sparkles = 12;
        public int Glints = 3;      // jewel blinks on the crown during the hold
        public bool RandomSeed = true;   // each Play rolls a new arrangement; off = always the Seed field
        public int Seed = 6;
        public float Speed = 1f;
        public float CrownSize = 1.4f;   // crown WIDTH in world units (any sprite, PPU-independent)
        public float DropHeight = 1.2f;
        public Color CrownTint = Color.white;   // art carries its colors; tint stays neutral
        public Color SparkleTint = new Color(1f, 0.92f, 0.55f);

        const float Land = 0.2f;         // fraction of the life when the crown hits

        bool _ui;
        SpriteRenderer[] _sr;
        Image[] _img;
        float[] _delay, _life, _ang, _dist, _size, _spin;
        float _timeline;

        public float Duration => Speed > 0.01f ? _timeline / Speed : _timeline;

        int _total => 1 + Sparkles + Glints;
        float _crownY;

        public void Init() {
            if (_delay != null) return;
            _ui = UI;
            int total = _total;
            if (_ui) _img = new Image[total]; else _sr = new SpriteRenderer[total];
            _delay = new float[total]; _life = new float[total];
            _ang = new float[total]; _dist = new float[total];
            _size = new float[total]; _spin = new float[total];
            _timeline = 1.5f;

            var old = Random.state;
            Random.InitState(Seed);
            for (int i = 0; i < total; i++) {
                if (i == 0) {                                        // the crown
                    _delay[i] = 0f;
                    _life[i] = _timeline;
                } else if (i <= Sparkles) {                          // impact sparkles: spray out of the base
                    int k = i - 1;
                    _delay[i] = _timeline * Land + Random.Range(0f, 0.12f);   // fuller, staggered burst
                    _life[i] = Random.Range(0.4f, 0.65f);
                    _ang[i] = Mathf.Lerp(-10f, 190f, k / Mathf.Max(1f, Sparkles - 1f)) + Random.Range(-12f, 12f);
                    _dist[i] = Random.Range(0.6f, 1.5f);                       // some fly far
                    _size[i] = Random.Range(0.25f, 0.6f);
                    _spin[i] = Random.Range(-90f, 90f);
                } else {                                             // jewel glints on the crown tips
                    int g = i - 1 - Sparkles;
                    _delay[i] = _timeline * (Land + 0.12f) + g * 0.16f + Random.Range(0f, 0.05f);
                    _life[i] = Random.Range(0.3f, 0.4f);
                    _ang[i] = g;                                     // which tip
                    _size[i] = Random.Range(0.35f, 0.5f);
                    _spin[i] = Random.Range(-60f, 60f);
                }
                _makeItem(i);
            }
            Random.state = old;
        }

        void _makeItem(int i) {
            var child = transform.Find("item" + i);
            var go = child != null ? child.gameObject : new GameObject("item" + i);
            go.transform.SetParent(transform, false);
            var sprite = i == 0 ? CrownSprite : SparkleSprite;
            if (_ui) {
                _strip<SpriteRenderer>(go);
                var img = go.GetComponent<Image>();
                if (img == null) img = go.AddComponent<Image>();
                img.sprite = sprite;
                img.raycastTarget = false;
                img.rectTransform.sizeDelta = Vector2.one;
                img.enabled = false;
                if (i == 0) go.transform.SetAsLastSibling();         // crown above its sparkles
                _img[i] = img;
            } else {
                _strip<Image>(go);
                var sr = go.GetComponent<SpriteRenderer>();
                if (sr == null) sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = i == 0 ? 50 : (i <= Sparkles ? i : 51 + i);   // spray < crown < glints
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
            if (RandomSeed) { Seed = Random.Range(0, 1 << 20); Rebuild(); }
            else Init();
            StopAllCoroutines();
            StartCoroutine(_run());
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
            float p = Mathf.Clamp01(t / _timeline);

            // the crown
            {
                float unit = CrownSprite != null && CrownSprite.bounds.size.x > 0.001f
                    ? CrownSize / CrownSprite.bounds.size.x : CrownSize;
                float alpha, y, rot, sx, sy;
                if (p < Land) {                                          // falling: accelerates in
                    float d = p / Land;
                    y = DropHeight * (1f - d * d);
                    rot = Mathf.Lerp(-9f, 0f, d);
                    sx = sy = unit;
                    alpha = Mathf.Clamp01(d / 0.35f);
                } else {                                                 // landed
                    float q = (p - Land) / (1f - Land);
                    // squash on impact, then a small rebound hop
                    float squash = Mathf.Max(0f, 1f - q / 0.14f);
                    float wob = Mathf.Sin(squash * Mathf.PI);
                    sx = unit * (1f + 0.16f * wob);
                    sy = unit * (1f - 0.2f * wob);
                    float hop = Mathf.Max(0f, Mathf.Sin(Mathf.Clamp01((q - 0.14f) / 0.18f) * Mathf.PI)) * 0.07f;
                    y = hop * (q < 0.32f ? 1f : 0f);
                    rot = Mathf.Sin(q * 14f) * 2.5f * (1f - q);          // proud shine wiggle, calming down
                    y += 0.18f * _smooth01(0.8f, 1f, q);                 // floats up while leaving
                    alpha = 1f - _smooth01(0.78f, 1f, q);
                }
                var c = CrownTint;
                c.a = alpha;
                _crownY = y;
                var tr = _apply(0, c);
                tr.localPosition = new Vector3(0f, y, 0f);
                tr.localRotation = Quaternion.Euler(0f, 0f, rot);
                tr.localScale = new Vector3(sx, sy, 1f);
            }

            // sparkles + glints — both TWINKLE: scale rises and dies on a sine, like a real glint
            for (int i = 1; i < _total; i++) {
                float sp = (t - _delay[i]) / _life[i];
                if (sp < 0f || sp > 1f) { _off(i); continue; }
                float twinkle = Mathf.Pow(Mathf.Sin(sp * Mathf.PI), 0.7f);
                Vector3 pos;
                if (i <= Sparkles) {                                     // impact spray, hugs the ground
                    var dir = new Vector3(Mathf.Cos(_ang[i] * Mathf.Deg2Rad), Mathf.Sin(_ang[i] * Mathf.Deg2Rad) * 0.5f, 0f);
                    pos = dir * (_dist[i] * _easeOutExpo(sp));
                } else {                                                 // pinned to a crown tip
                    int g = (int)_ang[i];
                    float w = CrownSize;                                  // crown visual width by definition
                    pos = new Vector3((g - 1) * 0.3f * w, _crownY + (g == 1 ? 0.3f : 0.22f) * w, 0f);
                }
                float s = _size[i] * twinkle;
                var c = Color.Lerp(SparkleTint, Color.white, i <= Sparkles ? 0.3f : 0.7f);
                c.a = twinkle;
                var tr = _apply(i, c);
                tr.localPosition = pos;
                tr.localRotation = Quaternion.Euler(0f, 0f, 45f + _spin[i] * sp);
                tr.localScale = new Vector3(s, s, 1f);
            }
        }

        Transform _apply(int i, Color c) {
            if (_ui) {
                var img = _img[i];
                img.enabled = c.a > 0.001f;
                img.color = c;
                return img.transform;
            }
            var sr = _sr[i];
            sr.enabled = c.a > 0.001f;
            sr.color = c;
            return sr.transform;
        }

        void _off(int i) {
            if (_ui) { if (_img[i] != null) _img[i].enabled = false; }
            else if (_sr[i] != null) _sr[i].enabled = false;
        }

        public void Hide() {
            if (_delay == null) return;
            for (int i = 0; i < _total; i++) _off(i);
        }

        static float _easeOutExpo(float x) {
            return 1f - Mathf.Pow(2f, -8f * x);
        }

        // edge-style smoothstep (Mathf.SmoothStep interpolates, which is not what fades need)
        static float _smooth01(float e0, float e1, float x) {
            float u = Mathf.Clamp01((x - e0) / (e1 - e0));
            return u * u * (3f - 2f * u);
        }
    }
}
