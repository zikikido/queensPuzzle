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
            hand._cg.alpha = 0f;               // born invisible — Evaluate shows it at its first key
            hand.PivotAtFingertip();
            return hand;
        }

        // One pressed piece of a drag (POINT→POINT / POINT→END) or a double-click. The hand
        // exists ONLY inside these — between gestures it's gone, so it never shows before a key.
        struct Seg {
            public float t0, t1;
            public Vector3 a, b;
            public bool doubleClick;       // tap-tap in place (a == b) instead of a pressed move
            public bool rampIn, rampOut;   // press down at the start / lift at the end — a corner POINT
                                           // in the middle of a drag keeps the finger down
        }

        readonly List<Seg> _segs = new List<Seg>();
        Transform _hand;     // the cloned $Hand visual (this component sits on its canvas root)
        Transform _finger;
        CanvasGroup _cg;

        // press rhythm — the same numbers MBToturial uses (taken from the playable ad's keyframes)
        const float TapCycle = 1.5f, TapScale = 0.82f, TapAngle = -7f;
        const float PressOut = 0.15f;

        /// <summary>Seconds a drag's first POINT takes to press fully — a first point placed this
        /// much before a board key lands the press on the mark.</summary>
        public const float PressIn = 0.12f;
        static readonly float[] DoubleKeys = { 0f, 0.12f, 0.20f, 0.28f, 0.40f, 1f };
        static readonly float[] DoubleVals = { 0f, 1f, 0f, 1f, 0f, 0f };
        const float DcShow = 0.40f * TapCycle;   // gone the instant the 2nd tap has released (0.6s)

        /// <summary>Seconds from a DOUBLE_CLICK key to its 2nd press (0.28 of the 1.5s cycle) — a
        /// hand double-tap placed this much before a queen key lands the press on the queen.</summary>
        public const float DcSecondPress = 0.28f * TapCycle;

        void Build(GPRecord record) {
            _segs.Clear();
            var gp = MBGameplay.instance;
            if (gp == null) return;

            bool dragging = false;        // inside a drag: first POINT seen, END not yet
            bool first = false;           // the next piece is the drag's first (press down)
            Vector3 dragPos = default;    // where the pressed finger is now
            float dragT = 0f;

            // one pressed piece of the drag; `last` lifts the finger
            void DragTo(float t, Vector3 pos, bool last) {
                float t1 = Mathf.Max(t, dragT + 0.05f);
                _segs.Add(new Seg { t0 = dragT, t1 = t1, a = dragPos, b = pos, rampIn = first, rampOut = last });
                dragT = t1; dragPos = pos; first = false;
                if (last) dragging = false;
            }

            foreach (var k in record.handKeys) {
                var cell = gp.CellAt(k.y, k.x);
                if (cell == null) continue;
                Vector3 p = cell.transform.position;
                switch (k.kind) {
                    case GPHandKey.EKind.END_MOVE:
                        if (dragging) DragTo(k.time, p, true);   // a stray END (no drag open) is ignored
                        break;
                    case GPHandKey.EKind.DOUBLE_CLICK:
                        if (dragging) DragTo(k.time, dragPos, true);
                        _segs.Add(new Seg { t0 = k.time, t1 = k.time + DcShow, a = p, b = p, doubleClick = true });
                        break;
                    default:   // POINT: the hand appears here and presses, or turns a corner mid-drag
                        if (dragging) DragTo(k.time, p, false);
                        else { dragging = true; first = true; dragT = k.time; dragPos = p; }
                        break;
                }
            }
            if (dragging) DragTo(dragT + 0.25f, dragPos, true);   // a drag never closed = a tap

            _segs.Sort((x, y) => x.t0.CompareTo(y.t0));
        }

        void Update() => Evaluate(GPReplayer.PlayheadTime);

        void Evaluate(float t) {
            bool visible = false;
            Vector3 pos = default;
            float press = 0f;
            foreach (var s in _segs) {
                if (t < s.t0 || t > s.t1) continue;
                visible = true;
                float u = Mathf.InverseLerp(s.t0, s.t1, t);
                // a pressed drag moves at CONSTANT speed — the marks it leaves appear at a
                // constant rate, and an eased hand would drift ahead of them mid-sweep
                if (s.doubleClick)
                    u = u < 0.5f ? 2f * u * u : 1f - Mathf.Pow(-2f * u + 2f, 2f) * 0.5f;   // ease in-out
                pos = Vector3.Lerp(s.a, s.b, u);
                if (s.doubleClick)
                    press = SampleEased(DoubleKeys, DoubleVals, Mathf.Clamp01((t - s.t0) / TapCycle));
                else
                    press = Mathf.Min(s.rampIn ? Mathf.Clamp01((t - s.t0) / PressIn) : 1f,
                                      s.rampOut ? Mathf.Clamp01((s.t1 - t) / PressOut) : 1f);
                break;
            }

            if (_cg != null) _cg.alpha = visible ? 1f : 0f;
            if (!visible) return;

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
