#if UNITY_EDITOR
using System.Collections.Generic;
using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// The ad hand: a clone of the tutorial's $Hand driven by the record's hand track. It follows
    /// GPReplayer.PlayheadTime every frame — replay moves it, and scrubbing moves it too. The
    /// press look uses the same keyframe rhythm as the tutorial (and the playable ad).
    /// </summary>
    public class GPHand : MonoBehaviour {

        public static GPHand Instance { get; private set; }

        /// <summary>Bring the hand up to date with the record's hand track (created on the first
        /// call — a clone of the tutorial's $Hand, parented under a LIVE canvas since the
        /// tutorial hierarchy sleeps).</summary>
        public static void Ensure(GPRecord record) {
            if (Instance == null) Instance = Spawn();
            if (Instance != null) Instance.Build(record);
        }

        public static void Remove() {
            if (Instance != null) Destroy(Instance.gameObject);
            Instance = null;
        }

        static GPHand Spawn() {
            var tut = MBToturial.instance;
            var src = tut != null ? tut.transform.RecursiveFindChild("$Hand") : null;
            if (src == null) { CDebug.LogError("[GPHand] tutorial $Hand not found"); return null; }

            // The clone gets its OWN always-active canvas: the tutorial's canvas sleeps, and any
            // other scene canvas may sort behind the board. Copy the tutorial canvas settings and
            // sort a bit above it — that canvas already draws over the board when the tutorial runs.
            var srcCanvas = src.GetComponentInParent<Canvas>(true);
            var root = new GameObject("$GPHand", typeof(RectTransform), typeof(Canvas));
            var canvas = root.GetComponent<Canvas>();
            if (srcCanvas != null) {
                var srt = srcCanvas.transform as RectTransform;
                var drt = root.transform as RectTransform;
                drt.position = srt.position;
                drt.rotation = srt.rotation;
                drt.localScale = srt.lossyScale;
                drt.sizeDelta = srt.sizeDelta;
                canvas.renderMode = srcCanvas.renderMode;
                canvas.worldCamera = srcCanvas.worldCamera;
                canvas.sortingLayerID = srcCanvas.sortingLayerID;
                canvas.sortingOrder = srcCanvas.sortingOrder + 10;
                canvas.planeDistance = srcCanvas.planeDistance;
            } else {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32000;
            }

            var go = Instantiate(src.gameObject, root.transform, true);
            go.name = "$HandVisual";
            go.SetActive(true);

            var dtt = go.transform.RecursiveFindChild("$DoublieTapTex");
            if (dtt != null) dtt.gameObject.SetActive(false);   // the replay hand never shows the hint text

            var hand = root.AddComponent<GPHand>();
            hand._hand = go.transform;
            hand._finger = go.transform.RecursiveFindChild("$FingerImage");
            hand._cg = go.GetComponent<CanvasGroup>();
            if (hand._cg == null) hand._cg = go.AddComponent<CanvasGroup>();
            hand._cg.blocksRaycasts = false;   // the hand must never eat board touches
            hand._cg.interactable = false;
            hand.PivotAtFingertip();
            return hand;
        }

        // one continuous piece of hand motion
        struct Seg {
            public float t0, t1;
            public Vector3 a, b;
            public int kind;   // 0 = glide (finger up), 1 = pressed sweep, 2 = double-click
        }

        readonly List<Seg> _segs = new List<Seg>();
        Transform _hand;     // the cloned $Hand visual (this component sits on its canvas root)
        Transform _finger;
        CanvasGroup _cg;
        float _showFrom, _hideAfter;

        // press rhythm — the same numbers MBToturial uses (taken from the playable ad's keyframes)
        const float TapCycle = 1.5f, TapScale = 0.82f, TapAngle = -7f;
        const float PressIn = 0.12f, PressOut = 0.15f;
        const float DcLead = 0.45f;   // the double-click cycle starts early so its 2nd press lands on the key
        static readonly float[] DoubleKeys = { 0f, 0.12f, 0.20f, 0.28f, 0.40f, 1f };
        static readonly float[] DoubleVals = { 0f, 1f, 0f, 1f, 0f, 0f };

        void Build(GPRecord record) {
            _segs.Clear();
            var gp = MBGameplay.instance;
            var keys = record.handKeys;
            if (gp == null || keys.Count == 0) { _showFrom = _hideAfter = -1f; return; }

            Vector3 lastPos = default;
            float lastT = float.MinValue;
            bool started = false;         // a START_MOVE waiting for its END_MOVE
            Vector3 startPos = default;
            float startT = 0f;

            void Glide(float toT, Vector3 toPos) {
                if (lastT != float.MinValue && toT > lastT)
                    _segs.Add(new Seg { t0 = lastT, t1 = toT, a = lastPos, b = toPos, kind = 0 });
            }
            void CloseSweep(float t, Vector3 pos) {
                _segs.Add(new Seg { t0 = startT, t1 = Mathf.Max(t, startT + 0.05f), a = startPos, b = pos, kind = 1 });
                started = false;
            }

            foreach (var k in keys) {
                var cell = gp.CellAt(k.y, k.x);
                if (cell == null) continue;
                Vector3 p = cell.transform.position;
                switch (k.kind) {
                    case GPHandKey.EKind.START_MOVE:
                        if (started) CloseSweep(k.time, p);   // stray START — close the open sweep here
                        else Glide(k.time, p);
                        started = true; startT = k.time; startPos = p;
                        lastT = k.time; lastPos = p;
                        break;
                    case GPHandKey.EKind.END_MOVE:
                        if (started) CloseSweep(k.time, p);
                        else Glide(k.time, p);                // stray END — just a waypoint
                        lastT = Mathf.Max(lastT, k.time); lastPos = p;
                        break;
                    case GPHandKey.EKind.DOUBLE_CLICK:
                        float s = k.time - DcLead;
                        if (started) CloseSweep(s, lastPos);
                        Glide(s, p);
                        _segs.Add(new Seg { t0 = s, t1 = s + TapCycle * 0.6f, a = p, b = p, kind = 2 });
                        lastT = s + TapCycle * 0.6f; lastPos = p;
                        break;
                }
            }
            if (started) CloseSweep(startT + 0.25f, startPos);   // unclosed sweep = a tap

            _segs.Sort((x, y) => x.t0.CompareTo(y.t0));
            _showFrom = keys[0].time - 1f;
            _hideAfter = lastT + 1.5f;
        }

        void Update() => Evaluate(GPReplayer.PlayheadTime);

        void Evaluate(float t) {
            bool visible = _segs.Count > 0 && t >= _showFrom && t <= _hideAfter;
            if (_cg != null) _cg.alpha = visible ? 1f : 0f;
            if (!visible) return;

            Vector3 pos = _segs[0].a;
            float press = 0f;
            foreach (var s in _segs) {
                if (t >= s.t1) { pos = s.b; continue; }
                if (t < s.t0) break;
                float u = Mathf.InverseLerp(s.t0, s.t1, t);
                u = u < 0.5f ? 2f * u * u : 1f - Mathf.Pow(-2f * u + 2f, 2f) * 0.5f;   // ease in-out
                pos = Vector3.Lerp(s.a, s.b, u);
                if (s.kind == 1)
                    press = Mathf.Min(Mathf.Clamp01((t - s.t0) / PressIn), Mathf.Clamp01((s.t1 - t) / PressOut));
                else if (s.kind == 2)
                    press = SampleEased(DoubleKeys, DoubleVals, Mathf.Clamp01((t - s.t0) / TapCycle));
                break;
            }

            _hand.position = pos;
            if (_finger != null) {
                _finger.localScale = Vector3.one * Mathf.Lerp(1f, TapScale, press);
                _finger.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, TapAngle, press));
            }
        }

        // The press pivots at the fingertip, not the image centre (same trick as MBToturial).
        void PivotAtFingertip() {
            var rt = _finger as RectTransform;
            if (rt == null) return;
            var want = new Vector2(0.22f, 0.86f);
            if (rt.pivot == want) return;
            var size = rt.rect.size;
            var delta = want - rt.pivot;
            rt.anchoredPosition += new Vector2(delta.x * size.x, delta.y * size.y);
            rt.pivot = want;
        }

        // piecewise sample with an ease-in-out inside each segment (matches CSS ease-in-out)
        static float SampleEased(float[] keys, float[] vals, float t) {
            for (int i = 0; i < keys.Length - 1; i++) {
                if (t < keys[i] || t > keys[i + 1]) continue;
                float span = keys[i + 1] - keys[i];
                if (span <= 0f) return vals[i + 1];
                float u = (t - keys[i]) / span;
                u = u < 0.5f ? 2f * u * u : 1f - Mathf.Pow(-2f * u + 2f, 2f) * 0.5f;
                return Mathf.Lerp(vals[i], vals[i + 1], u);
            }
            return vals[vals.Length - 1];
        }
    }
}
#endif
