using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// Telegram-style thumbs-up reaction: a big thumb springs in with a tilt, mini thumbs
    /// fly out of it coin-flipping, and a few stars sparkle. One-shot. Check <see cref="UI"/>
    /// to render through UI Images (place under its own Canvas; 1 unit = 1 px at root scale 1).
    /// Editor previews drive it via Init() + Evaluate(); runtime uses Play().
    /// </summary>
    public class MBThumbBurst : MonoBehaviour, IInGameReactionFX {

        public bool UI;
        public Sprite ThumbSprite;
        public Sprite StarSprite;
        public int Minis = 9;
        public int Stars = 6;
        public int Seed = 11;
        public float Speed = 1f;
        public float LifeTime = 0.7f;    // base item life in seconds (at Speed 1)
        public float Radius = 1.2f;      // how far the mini thumbs fly
        public float BigSize = 0.85f;
        public float SizeMin = 0.22f;    // mini thumb size range
        public float SizeMax = 0.42f;
        public Color Tint = Color.white;   // sprite carries the emoji colors; tint stays neutral
        public Color StarTint = new Color(1f, 0.85f, 0.4f);

        bool _ui;
        SpriteRenderer[] _sr;
        Image[] _img;
        int[] _kind;   // 0 big thumb, 1 mini thumb, 2 star
        float[] _delay, _life, _ang, _dist, _size, _phase, _spin;
        Color[] _tint;
        float _timeline;

        public float Duration => Speed > 0.01f ? _timeline / Speed : _timeline;

        int _total => 1 + Minis + Stars;

        public void Init() {
            if (_kind != null) return;
            _ui = UI;
            int total = _total;
            if (_ui) _img = new Image[total]; else _sr = new SpriteRenderer[total];
            _kind = new int[total];
            _delay = new float[total]; _life = new float[total];
            _ang = new float[total]; _dist = new float[total]; _size = new float[total];
            _phase = new float[total]; _spin = new float[total];
            _tint = new Color[total];
            var orders = new int[total];

            var old = Random.state;
            Random.InitState(Seed);
            for (int i = 0; i < total; i++) {
                Sprite sprite;
                if (i == 0) {                                        // the big thumb
                    _kind[i] = 0;
                    _delay[i] = 0f;
                    _life[i] = LifeTime * 1.4f;
                    _size[i] = BigSize;
                    _tint[i] = Tint;
                    sprite = ThumbSprite;
                    orders[i] = 315;
                } else if (i <= Minis) {                             // mini thumbs, upward fan
                    _kind[i] = 1;
                    _delay[i] = 0.05f + Random.Range(0f, 0.12f);
                    _life[i] = LifeTime * Random.Range(0.8f, 1.1f);
                    _ang[i] = Mathf.Lerp(25f, 155f, (i - 1) / Mathf.Max(1f, Minis - 1f)) + Random.Range(-8f, 8f);
                    _dist[i] = Radius * Random.Range(0.5f, 1f);
                    _size[i] = Random.Range(SizeMin, SizeMax);
                    _phase[i] = Random.Range(0f, Mathf.PI * 2f);
                    _spin[i] = Random.Range(4f, 8f);
                    _tint[i] = Color.Lerp(Tint, Color.white, Random.Range(0f, 0.3f));
                    sprite = ThumbSprite;
                    orders[i] = 300 + i;
                } else {                                             // sparkle stars, all directions
                    _kind[i] = 2;
                    _delay[i] = Random.Range(0f, 0.08f);
                    _life[i] = LifeTime * Random.Range(0.5f, 0.75f);
                    _ang[i] = Random.Range(0f, 360f);
                    _dist[i] = Radius * Random.Range(0.6f, 1.2f);
                    _size[i] = Random.Range(0.1f, 0.2f);
                    _phase[i] = Random.Range(0f, Mathf.PI * 2f);
                    _spin[i] = Random.Range(-4f, 4f);
                    _tint[i] = Color.Lerp(StarTint, Color.white, Random.Range(0f, 0.4f));
                    sprite = StarSprite;
                    orders[i] = 330 + i;
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
                img.rectTransform.sizeDelta = Vector2.one;
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

                if (_kind[i] == 0) {                                            // big thumb: spring in, tilt, drift out
                    float pop = _easeOutBack(Mathf.Min(1f, p * 2.2f));
                    float exit = _smooth01(0.62f, 1f, p);
                    float s = _size[i] * pop * (1f - 0.25f * exit);
                    float rot = Mathf.LerpUnclamped(-28f, 6f, _easeOutBack(Mathf.Min(1f, p * 1.8f)));
                    var pos = new Vector3(0f, 0.25f * Radius * exit, 0f);       // floats up while leaving
                    var c = Color.Lerp(Color.white, _tint[i], Mathf.Min(1f, p * 3f));
                    c.a = Mathf.Clamp01(p / 0.06f) * (1f - _smooth01(0.7f, 1f, p));
                    _apply(i, pos, rot, s, s, c);
                    continue;
                }

                var dir = new Vector3(Mathf.Cos(_ang[i] * Mathf.Deg2Rad), Mathf.Sin(_ang[i] * Mathf.Deg2Rad), 0f);
                float travel = _easeOutExpo(p);

                if (_kind[i] == 1) {                                            // mini thumb: fly out, coin-flip, hang
                    var pos = dir * (_dist[i] * travel);
                    pos.y += 0.25f * Radius * p * p;                            // gentle lift
                    float sy = _size[i] * Mathf.Clamp01(p / 0.1f) * (1f - _smooth01(0.6f, 1f, p) * 0.5f);
                    float sx = sy * Mathf.Cos(p * _spin[i] + _phase[i]);        // the 3D coin-flip
                    float rot = Mathf.Sin(p * 6f + _phase[i]) * 12f;            // playful rocking
                    var c = _tint[i];
                    c.a = 1f - _smooth01(0.65f, 1f, p);
                    _apply(i, pos, rot, sx, sy, c);
                } else {                                                        // star: shoot out, spin, shrink
                    var pos = dir * (_dist[i] * travel);
                    float s = _size[i] * (1f - p);
                    float rot = _phase[i] * Mathf.Rad2Deg + p * _spin[i] * 60f;
                    var c = _tint[i];
                    c.a = Mathf.Clamp01(p / 0.1f) * (1f - _smooth01(0.5f, 1f, p));
                    _apply(i, pos, rot, s, s, c);
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
