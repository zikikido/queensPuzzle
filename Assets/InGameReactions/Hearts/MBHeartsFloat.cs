using System.Collections;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Telegram fullscreen-heart style: mini hearts spawn staggered around the center, float
    /// upward with a sideways sine sway, and coin-flip around their vertical axis (scaleX =
    /// cos) — the "3D" look. Editor previews drive it via Init() + Evaluate(); runtime Play().
    /// </summary>
    public class MBHeartsFloat : MonoBehaviour, IInGameReactionFX {

        public Sprite[] Sprites;
        public int Count = 12;
        public bool RandomSeed = true;   // each Play rolls a new arrangement; off = always the Seed field
        public int Seed = 7;
        public float Speed = 1f;        // playback speed: 2 = twice as fast
        public float LifeTime = 1.5f;   // base heart life in seconds (at Speed 1)
        public float SizeMin = 0.24f;
        public float SizeMax = 0.55f;
        public Vector2 SpawnArea = new Vector2(1.1f, 0.4f);   // box (width, height) hearts start inside
        public bool Outline = true;              // white sticker edge behind every heart
        public Color OutlineColor = Color.white;
        public float OutlineScale = 1.15f;
        public Color[] Tints = {
            new Color(1f, 0.35f, 0.45f),     // pink-red
            new Color(1f, 0.62f, 0.2f),      // orange
            new Color(1f, 0.82f, 0.25f),     // gold
            new Color(0.4f, 0.8f, 0.45f),    // green
            new Color(0.35f, 0.65f, 1f),     // blue
            new Color(0.75f, 0.5f, 1f),      // purple
        };

        SpriteRenderer[] _items;
        SpriteRenderer[] _outlines;
        float[] _delay, _life, _x0, _y0, _rise, _swayAmp, _swayFreq, _swayPhase, _size, _spin, _spinPhase;
        float _timeline;   // full effect length on the internal (unscaled) clock

        public float Duration => Speed > 0.01f ? _timeline / Speed : _timeline;

        public void Init() {
            if (_items != null) return;
            _items = new SpriteRenderer[Count];
            _outlines = new SpriteRenderer[Count];
            _delay = new float[Count]; _life = new float[Count];
            _x0 = new float[Count]; _y0 = new float[Count]; _rise = new float[Count];
            _swayAmp = new float[Count]; _swayFreq = new float[Count]; _swayPhase = new float[Count];
            _size = new float[Count]; _spin = new float[Count]; _spinPhase = new float[Count];

            var old = Random.state;
            Random.InitState(Seed);   // same choreography in editor preview and runtime
            for (int i = 0; i < Count; i++) {
                var child = transform.Find("heart" + i);   // reuse children baked into the prefab
                var go = child != null ? child.gameObject : new GameObject("heart" + i);
                go.transform.SetParent(transform, false);
                var sr = go.GetComponent<SpriteRenderer>();
                if (sr == null) sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = Sprites[i % Sprites.Length];
                sr.color = Tints[i % Tints.Length];
                sr.sortingOrder = i * 2 + 1;
                sr.enabled = false;
                _items[i] = sr;
                _outlines[i] = _makeOutline(go, sr, i * 2);

                _delay[i] = i * 0.07f + Random.Range(0f, 0.05f);
                _life[i] = LifeTime * Random.Range(0.85f, 1.15f);
                _x0[i] = Random.Range(-SpawnArea.x, SpawnArea.x) * 0.5f;
                _y0[i] = Random.Range(-SpawnArea.y, SpawnArea.y) * 0.5f;
                _rise[i] = Random.Range(1.1f, 2f);
                _swayAmp[i] = Random.Range(0.15f, 0.4f);
                _swayFreq[i] = Random.Range(2.5f, 4.5f);
                _swayPhase[i] = Random.Range(0f, Mathf.PI * 2f);
                _size[i] = Random.Range(SizeMin, SizeMax);
                _spin[i] = Random.Range(3f, 6f);
                _spinPhase[i] = Random.Range(0f, Mathf.PI * 2f);
                _timeline = Mathf.Max(_timeline, _delay[i] + _life[i]);
            }
            Random.state = old;
        }

        // The edge: same sprite as a child (inherits the full motion), bigger, behind.
        SpriteRenderer _makeOutline(GameObject owner, SpriteRenderer sr, int order) {
            var ot = owner.transform.Find("outline");
            if (!Outline) { if (ot != null) { if (Application.isPlaying) Destroy(ot.gameObject); else DestroyImmediate(ot.gameObject); } return null; }
            var og = ot != null ? ot.gameObject : new GameObject("outline");
            og.transform.SetParent(owner.transform, false);
            og.transform.localScale = Vector3.one * OutlineScale;
            var osr = og.GetComponent<SpriteRenderer>();
            if (osr == null) osr = og.AddComponent<SpriteRenderer>();
            osr.sprite = sr.sprite;
            osr.sortingOrder = order;
            osr.enabled = false;
            return osr;
        }

        /// <summary>Drop the cached choreography and rebuild from the current inspector values.</summary>
        public void Rebuild() {
            Hide();
            _items = null;
            _timeline = 0f;
            for (int i = Count; ; i++) {           // remove surplus children if Count went down
                var c = transform.Find("heart" + i);
                if (c == null) break;
                if (Application.isPlaying) Destroy(c.gameObject);
                else DestroyImmediate(c.gameObject);
            }
            Init();
        }

        void Awake() {
            Init();
        }

        public void Play(Vector3 worldPos) {
            transform.position = worldPos;
            if (RandomSeed) { Seed = Random.Range(0, 1 << 20); Rebuild(); }
            else Init();
            StopAllCoroutines();
            StartCoroutine(_run());
        }

        IEnumerator _run() {
            for (float t = 0f; t < Duration; t += Time.deltaTime) {
                Evaluate(t);
                yield return null;
            }
            Hide();
        }

        public void Hide() {
            if (_items == null) return;
            for (int i = 0; i < _items.Length; i++) {
                if (_items[i] != null) _items[i].enabled = false;
                if (_outlines != null && _outlines[i] != null) _outlines[i].enabled = false;
            }
        }

        /// <summary>Set every heart's state for time t — pure, so editor code can scrub it.</summary>
        public void Evaluate(float t) {
            t *= Speed;   // Speed compresses the whole timeline; Duration reports the scaled length
            for (int i = 0; i < _items.Length; i++) {
                float lt = t - _delay[i];
                var sr = _items[i];
                var osr = _outlines != null ? _outlines[i] : null;
                if (lt < 0f || lt > _life[i]) { sr.enabled = false; if (osr != null) osr.enabled = false; continue; }
                sr.enabled = true;

                float x = _x0[i] + Mathf.Sin(lt * _swayFreq[i] + _swayPhase[i]) * _swayAmp[i] * Mathf.Clamp01(lt);
                float y = _y0[i] + _rise[i] * lt + 0.25f * lt * lt;   // gentle lift acceleration
                sr.transform.localPosition = new Vector3(x, y, 0f);

                float pop = _easeOutBack(Mathf.Clamp01(lt / 0.22f));  // spring in at spawn
                float sy = _size[i] * pop;
                float sx = sy * Mathf.Cos(lt * _spin[i] + _spinPhase[i]);   // the 3D coin-flip
                sr.transform.localScale = new Vector3(sx, sy, 1f);

                var c = sr.color;
                c.a = Mathf.Clamp01(lt / 0.15f) * Mathf.Clamp01((_life[i] - lt) / 0.35f);
                sr.color = c;
                if (osr != null) {
                    osr.enabled = true;
                    var oc = OutlineColor;
                    oc.a = c.a;
                    osr.color = oc;
                }
            }
        }

        static float _easeOutBack(float x) {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float p = x - 1f;
            return 1f + c3 * p * p * p + c1 * p * p;
        }
    }
}
