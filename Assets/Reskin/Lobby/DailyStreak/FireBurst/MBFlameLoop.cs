using System.Collections;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Looping fake-3D fire: flame tongues rise from a spawn box, sway, coin-flip around
    /// their vertical axis and dissolve near the top — respawning forever while enabled.
    /// Per-cycle variation comes from a deterministic hash, so the editor preview
    /// (Init() + Evaluate(t)) matches runtime exactly.
    /// </summary>
    public class MBFlameLoop : MonoBehaviour {

        public Sprite[] Sprites;
        public int Count = 9;
        public int Seed = 3;
        public float Speed = 1f;         // playback speed multiplier
        public float LifeTime = 0.9f;    // seconds per tongue (at Speed 1)
        public float RiseHeight = 0.6f;  // world units a tongue climbs over its life
        public float SizeMin = 0.45f;
        public float SizeMax = 0.85f;
        public Vector2 SpawnArea = new Vector2(0.45f, 0.1f);
        public Color[] Tints = {
            new Color(1f, 0.85f, 0.4f),
            new Color(1f, 0.6f, 0.15f),
            new Color(1f, 0.4f, 0.1f),
        };

        SpriteRenderer[] _items;

        public void Init() {
            if (_items != null) return;
            _items = new SpriteRenderer[Count];
            for (int i = 0; i < Count; i++) {
                var child = transform.Find("flame" + i);
                var go = child != null ? child.gameObject : new GameObject("flame" + i);
                go.transform.SetParent(transform, false);
                var sr = go.GetComponent<SpriteRenderer>();
                if (sr == null) sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = Sprites[i % Sprites.Length];
                sr.sortingOrder = 300 + i;
                sr.enabled = false;
                _items[i] = sr;
            }
        }

        /// <summary>Drop the cached items and rebuild from the current inspector values.</summary>
        public void Rebuild() {
            Hide();
            _items = null;
            for (int i = Count; ; i++) {
                var c = transform.Find("flame" + i);
                if (c == null) break;
                if (Application.isPlaying) Destroy(c.gameObject);
                else DestroyImmediate(c.gameObject);
            }
            Init();
        }

        void OnEnable() {
            Init();
            if (Application.isPlaying) StartCoroutine(_run());
        }

        void OnDisable() {
            Hide();
        }

        IEnumerator _run() {
            float t0 = Time.time;
            while (true) {
                Evaluate(Time.time - t0);
                yield return null;
            }
        }

        /// <summary>Set every tongue's state for time t — pure, so editor code can scrub it.</summary>
        public void Evaluate(float t) {
            t *= Speed;
            float stagger = LifeTime / Count;
            for (int i = 0; i < _items.Length; i++) {
                float local = t - i * stagger;
                var sr = _items[i];
                if (local < 0f) { sr.enabled = false; continue; }

                int cycle = Mathf.FloorToInt(local / LifeTime);
                float lt = (local - cycle * LifeTime) / LifeTime;   // normalized 0..1 within this cycle

                float x0 = (_hash(i, cycle, 1) - 0.5f) * SpawnArea.x;
                float y0 = (_hash(i, cycle, 2) - 0.5f) * SpawnArea.y;
                float size = Mathf.Lerp(SizeMin, SizeMax, _hash(i, cycle, 3));
                float swayPhase = _hash(i, cycle, 4) * Mathf.PI * 2f;
                float spin = Mathf.Lerp(2.5f, 5.5f, _hash(i, cycle, 5));
                float spinPhase = _hash(i, cycle, 6) * Mathf.PI * 2f;
                var tint = Tints[(int)(_hash(i, cycle, 7) * 0.999f * Tints.Length)];

                sr.enabled = true;
                float x = x0 + Mathf.Sin(lt * 7f + swayPhase) * 0.07f * lt;
                float y = y0 + RiseHeight * lt;
                sr.transform.localPosition = new Vector3(x, y, 0f);

                float grow = Mathf.Clamp01(lt / 0.15f);                       // quick grow at spawn
                float taper = 1f - _smooth01(0.45f, 1f, lt);                  // dissolve toward the top
                float sy = size * grow * Mathf.Max(taper, 0.02f);
                // subtle 3D wobble: narrows toward edge-on but never vanishes
                float sx = sy * (0.62f + 0.38f * Mathf.Cos(lt * LifeTime * spin + spinPhase));
                sr.transform.localScale = new Vector3(sx, sy, 1f);

                var c = tint;
                c.a = Mathf.Clamp01(lt / 0.1f) * (1f - _smooth01(0.7f, 1f, lt));
                sr.color = c;
            }
        }

        public void Hide() {
            if (_items == null) return;
            foreach (var sr in _items)
                if (sr != null) sr.enabled = false;
        }

        // edge-style smoothstep (Mathf.SmoothStep interpolates, which is not what fades need)
        static float _smooth01(float e0, float e1, float x) {
            float u = Mathf.Clamp01((x - e0) / (e1 - e0));
            return u * u * (3f - 2f * u);
        }

        float _hash(int i, int cycle, int k) {
            float n = Mathf.Sin(Seed * 91.317f + i * 78.233f + cycle * 37.719f + k * 12.9898f) * 43758.5453f;
            return n - Mathf.Floor(n);
        }
    }
}
