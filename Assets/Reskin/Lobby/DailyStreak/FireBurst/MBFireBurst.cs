using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// One-shot fire burst — a bomb-like ignition that rises like real fire: base glow, flash,
    /// two depths of upright flame tongues (dark outer / bright core), spark streaks, embers,
    /// and a smoke tail. Check <see cref="UI"/> to render through UI Images (place it under
    /// its own Canvas; 1 effect unit = 1 px at root scale 1). Editor previews drive it via
    /// Init() + Evaluate(); runtime uses Play(). NOTE: Mathf.SmoothStep(from,to,t)
    /// interpolates — _smooth01 below is the edge-style smoothstep.
    /// </summary>
    public class MBFireBurst : MonoBehaviour {

        public bool UI;                  // render with UI Images instead of SpriteRenderers
        public Sprite FlameSprite;
        public Sprite GlowSprite;
        public int Tongues = 10;
        public int Embers = 8;
        public int Sparks = 6;
        public int Smoke = 4;
        public int Seed = 5;
        public float Speed = 1f;
        public float LifeTime = 0.55f;   // base item life in seconds (at Speed 1)
        public float Radius = 1.1f;      // how far tongues are thrown
        public float RiseHeight = 1f;    // how high the fire climbs over a tongue's life
        public float SizeMin = 0.3f;
        public float SizeMax = 0.6f;
        public Color[] Tints = {
            new Color(1f, 0.9f, 0.5f),
            new Color(1f, 0.65f, 0.15f),
            new Color(1f, 0.4f, 0.05f),
        };

        bool _ui;
        SpriteRenderer[] _sr;
        Image[] _img;
        int[] _kind;   // 0 flash, 1 outer tongue, 2 ember, 3 smoke, 4 spark, 5 base glow, 6 inner tongue
        float[] _delay, _life, _ang, _dist, _size, _wobblePhase, _wobbleSpeed;
        Color[] _tint;
        float _timeline;

        public float Duration => Speed > 0.01f ? _timeline / Speed : _timeline;

        int _total => 1 + Tongues + Embers + Sparks + Smoke + 1;

        public void Init() {
            if (_kind != null) return;
            _ui = UI;
            int total = _total;
            if (_ui) _img = new Image[total]; else _sr = new SpriteRenderer[total];
            _kind = new int[total];
            _delay = new float[total]; _life = new float[total];
            _ang = new float[total]; _dist = new float[total]; _size = new float[total];
            _wobblePhase = new float[total]; _wobbleSpeed = new float[total];
            _tint = new Color[total];
            var orders = new int[total];

            var old = Random.state;
            Random.InitState(Seed);
            for (int i = 0; i < total; i++) {
                Sprite sprite;
                if (i == 0) {                                        // flash
                    _kind[i] = 0;
                    _delay[i] = 0f;
                    _life[i] = 0.22f;
                    _size[i] = Radius * 0.9f;
                    _tint[i] = Color.Lerp(Tints[0], Color.white, 0.5f);
                    sprite = GlowSprite;
                    orders[i] = 299;
                } else if (i <= Tongues) {                           // flame tongues, two depths
                    int k = i - 1;
                    bool inner = k >= (Tongues + 1) / 2;             // second half = bright core, in front
                    _kind[i] = inner ? 6 : 1;
                    _delay[i] = (inner ? 0.04f : 0f) + Random.Range(0f, 0.04f);
                    _life[i] = LifeTime * Random.Range(0.8f, 1.2f);
                    _ang[i] = k * 360f / Tongues + Random.Range(-14f, 14f);
                    _dist[i] = Radius * Random.Range(0.55f, 1f) * (inner ? 0.7f : 1f);
                    _size[i] = Random.Range(SizeMin, SizeMax) * (inner ? 0.72f : 1.15f);
                    _wobblePhase[i] = Random.Range(0f, Mathf.PI * 2f);
                    _wobbleSpeed[i] = Random.Range(6f, 12f);
                    _tint[i] = inner
                        ? Color.Lerp(Tints[0], Color.white, Random.Range(0.1f, 0.35f))
                        : Color.Lerp(Tints[Tints.Length - 1], Tints[Tints.Length / 2], Random.value);
                    sprite = FlameSprite;
                    orders[i] = (inner ? 315 : 300) + i;
                } else if (i <= Tongues + Embers) {                  // ember
                    _kind[i] = 2;
                    _delay[i] = Random.Range(0.02f, 0.1f);
                    _life[i] = LifeTime * Random.Range(1.1f, 1.5f);
                    _ang[i] = Random.Range(0f, 360f);
                    _dist[i] = Radius * Random.Range(1f, 1.5f);
                    _size[i] = Random.Range(0.08f, 0.16f);
                    _tint[i] = Tints[Random.Range(0, Tints.Length)];
                    sprite = FlameSprite;
                    orders[i] = 330 + i;
                } else if (i <= Tongues + Embers + Sparks) {         // spark streak
                    _kind[i] = 4;
                    _delay[i] = Random.Range(0f, 0.08f);
                    _life[i] = LifeTime * Random.Range(0.5f, 0.7f);
                    _ang[i] = Random.Range(0f, 360f);
                    _dist[i] = Radius * Random.Range(0.3f, 0.8f);
                    _size[i] = Random.Range(0.16f, 0.28f);
                    _wobblePhase[i] = Random.Range(0f, Mathf.PI * 2f);
                    _tint[i] = new Color(1f, 0.95f, 0.55f);
                    sprite = FlameSprite;
                    orders[i] = 340 + i;
                } else if (i <= Tongues + Embers + Sparks + Smoke) { // smoke puff
                    _kind[i] = 3;
                    _delay[i] = LifeTime * Random.Range(0.22f, 0.38f);
                    _life[i] = LifeTime * Random.Range(0.9f, 1.2f);
                    _ang[i] = Random.Range(0f, 360f);
                    _dist[i] = Radius * Random.Range(0.15f, 0.45f);
                    _size[i] = Random.Range(0.5f, 0.85f);
                    _wobblePhase[i] = Random.Range(0f, Mathf.PI * 2f);
                    _tint[i] = new Color(0.58f, 0.54f, 0.51f);
                    sprite = GlowSprite;
                    orders[i] = 296 + i - (Tongues + Embers + Sparks);   // behind the flames
                } else {                                             // base glow, grounds the fire
                    _kind[i] = 5;
                    _delay[i] = 0f;
                    _life[i] = LifeTime * 1.1f;
                    _size[i] = Radius * 1.6f;
                    _tint[i] = Tints[Tints.Length / 2];
                    sprite = GlowSprite;
                    orders[i] = 295;
                }
                _makeItem(i, sprite, orders[i]);
                _timeline = Mathf.Max(_timeline, _delay[i] + _life[i]);
            }
            Random.state = old;

            if (_ui) {                                               // UI draws by sibling order
                var ids = new int[total];
                for (int k = 0; k < total; k++) ids[k] = k;
                System.Array.Sort((int[])orders.Clone(), ids);
                for (int rank = 0; rank < total; rank++)
                    _img[ids[rank]].transform.SetSiblingIndex(rank);
            }
        }

        void _makeItem(int i, Sprite sprite, int order) {
            var child = transform.Find("item" + i);
            var go = child != null ? child.gameObject : new GameObject("item" + i);
            go.transform.SetParent(transform, false);
            if (_ui) {
                _strip<SpriteRenderer>(go);
                var img = go.GetComponent<Image>();
                if (img == null) img = go.AddComponent<Image>();
                img.sprite = sprite;
                img.raycastTarget = false;
                img.rectTransform.sizeDelta = Vector2.one;   // 1-unit rect: localScale maps 1:1
                img.enabled = false;
                _img[i] = img;
            } else {
                _strip<Image>(go);
                var sr = go.GetComponent<SpriteRenderer>();
                if (sr == null) sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = order;
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
            _kind = null; _sr = null; _img = null;
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
            StopAllCoroutines();
            StartCoroutine(_run());
        }

        IEnumerator _run() {
            // unscaled: plays the same inside popups that pause or scale time
            for (float t = 0f; t < Duration; t += Time.unscaledDeltaTime) {
                Evaluate(t);
                yield return null;
            }
            Hide();
        }

        /// <summary>Set every item's state for time t — pure, so editor code can scrub it.</summary>
        public void Evaluate(float t) {
            t *= Speed;
            for (int i = 0; i < _kind.Length; i++) {
                float p = (t - _delay[i]) / _life[i];   // normalized 0..1 life progress
                if (p < 0f || p > 1f) { _off(i); continue; }

                if (_kind[i] == 0) {                                            // flash
                    float s = _size[i] * _easeOutBack(Mathf.Min(1f, p * 1.6f));
                    var c = _tint[i];
                    c.a = 0.95f * (1f - _smooth01(0.3f, 1f, p));
                    _apply(i, Vector3.zero, 0f, s, s, c);
                    continue;
                }

                if (_kind[i] == 5) {                                            // base glow: flat ember bed
                    float w = _size[i] * _easeOutBack(Mathf.Min(1f, p * 4f));
                    var gc = _tint[i];
                    gc.a = 0.55f * (1f - _smooth01(0.45f, 1f, p));
                    _apply(i, new Vector3(0f, -0.05f, 0f), 0f, w, w * 0.45f, gc);
                    continue;
                }

                var dir = new Vector3(Mathf.Cos(_ang[i] * Mathf.Deg2Rad), Mathf.Sin(_ang[i] * Mathf.Deg2Rad), 0f);
                float travel = _easeOutExpo(p);

                if (_kind[i] == 1 || _kind[i] == 6) {                           // flame tongue (outer/inner)
                    // kick out of the center (flattened), then heat carries it up
                    var pos = new Vector3(dir.x, dir.y * 0.25f, 0f) * (_dist[i] * travel);
                    pos.y += RiseHeight * p * p;
                    pos.x += Mathf.Sin(p * _wobbleSpeed[i] + _wobblePhase[i]) * 0.04f;   // flicker sway
                    float sy = _size[i] * Mathf.Clamp01(p / 0.12f) * (1f - _smooth01(0.5f, 1f, p) * 0.8f);
                    float sx = sy * (0.6f + 0.4f * Mathf.Cos(p * _wobbleSpeed[i] + _wobblePhase[i]));
                    var c = Color.Lerp(Tints[0], _tint[i], Mathf.Min(1f, p * 2.5f));   // white-hot -> its color
                    c.a = 1f - _smooth01(0.55f, 1f, p);
                    // upright, leaning slightly outward while the kick lasts
                    _apply(i, pos, -dir.x * 16f * (1f - p), sx, sy, c);
                } else if (_kind[i] == 2) {                                     // ember
                    var pos = new Vector3(dir.x, dir.y * 0.5f, 0f) * (_dist[i] * travel);
                    pos.y += RiseHeight * 1.3f * p * p;                         // embers ride the heat higher
                    pos.x += Mathf.Sin(p * 9f + _wobblePhase[i]) * 0.05f;
                    float s = _size[i] * (1f - p * 0.8f);
                    var c = _tint[i];
                    c.a = 1f - _smooth01(0.4f, 1f, p);
                    _apply(i, pos, 0f, s, s, c);
                } else if (_kind[i] == 4) {                                     // spark streak: thin, fast, bright
                    var pos = new Vector3(dir.x, dir.y * 0.3f, 0f) * (_dist[i] * travel);
                    pos.y += RiseHeight * 1.6f * Mathf.Pow(p, 1.4f);
                    pos.x += Mathf.Sin(p * 11f + _wobblePhase[i]) * 0.03f;
                    float sy = _size[i] * (1f - p * 0.5f);
                    var c = _tint[i];
                    c.a = Mathf.Clamp01(p / 0.08f) * (1f - _smooth01(0.5f, 1f, p));
                    _apply(i, pos, 0f, sy * 0.2f, sy, c);
                } else {                                                        // smoke puff: rises, expands, thins
                    var pos = new Vector3(dir.x, dir.y * 0.3f, 0f) * (_dist[i] * travel);
                    pos.y += RiseHeight * 0.6f * p;
                    pos.x += Mathf.Sin(p * 3f + _wobblePhase[i]) * 0.08f;
                    float s = _size[i] * (0.6f + p * 0.9f);
                    var c = _tint[i];
                    c.a = 0.28f * Mathf.Clamp01(p / 0.25f) * (1f - _smooth01(0.5f, 1f, p));
                    _apply(i, pos, _wobblePhase[i] * 20f, s, s, c);
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
            if (_kind == null) return;
            for (int i = 0; i < _kind.Length; i++) _off(i);
        }

        static float _easeOutExpo(float x) {
            return 1f - Mathf.Pow(2f, -8f * x);
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
