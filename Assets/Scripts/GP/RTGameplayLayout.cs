using Common;
using Core;
using UnityEngine;

namespace qp {
    /// <summary>
    /// The gameplay screen's vertical split: TopBar / Board / BottomBar inside one parent.
    /// The BOARD gets the RTAspectFitMax treatment — the largest Ratio-respecting rect inside
    /// the parent minus the min margins, centered in that free area. The bars are AUTO: each
    /// fills the whole band between the board's edge and the parent's edge, at the board's
    /// width (portrait — all three share the same width) and horizontally aligned with it.
    /// The three RectTransforms are driven here; each child then reports its own new size
    /// down (plain RTResizeInvoker on it — NOT a listener, this script owns their rects).
    /// </summary>
    [ExecuteInEditMode]
    public class RTGameplayLayout : RTResizeInvoker.ABSMBListener {

        public RectTransform TopBar;
        public RectTransform Board;
        public RectTransform BottomBar;

        [Tooltip("Overlays sized to the WHOLE area (top + board + bottom), so they match the full " +
                 "gameplay screen — e.g. the tutorial, which mirrors the game layout.")]
        public RectTransform[] FullLayout;

        [Header("Board fit (like RTAspectFitMax)")]
        public float Ratio = 1f;      // width / height of the board (1 = square)

        // min margins, fractions of the parent: top/bottom of its height, left/right of its width
        public float TopNormal;
        public float BottomNormal;
        public float LeftNormal;
        public float RightNormal;

        DrivenRectTransformTracker _driven;

        protected override void Resize(float w, float h) {
            if (TopBar == null || Board == null || BottomBar == null) {
                CDebug.LogError($"[RTGameplayLayout] missing a child ref on {transform.GetFullPathName()}");
                return;
            }

            // own rect = exactly the reported parent area — the children's math assumes it
            RT.anchorMin = Vector2.zero;
            RT.anchorMax = Vector2.one;
            RT.offsetMin = Vector2.zero;
            RT.offsetMax = Vector2.zero;
            RT.localScale = Vector3.one;

            float availW = w * (1f - LeftNormal - RightNormal);
            float availH = h * (1f - TopNormal - BottomNormal);
            if (Ratio <= 0f || availW <= 0f || availH <= 0f) return;

            // the board: the largest Ratio rect that fits the free area
            float boardW = Mathf.Min(availW, availH * Ratio);
            float boardH = boardW / Ratio;

            // position: centered in the PARENT; a min margin moves it only when violated,
            // so a non-binding margin has zero effect (0 and 0.01 look identical)
            float cx = Mathf.Clamp(0f, LeftNormal * w - (w - boardW) * 0.5f, (w - boardW) * 0.5f - RightNormal * w);
            float cy = Mathf.Clamp(0f, BottomNormal * h - (h - boardH) * 0.5f, (h - boardH) * 0.5f - TopNormal * h);

            // the bars: everything above / below the board, at the board's width
            float boardTop = cy + boardH * 0.5f, parentTop = h * 0.5f;
            float boardBottom = cy - boardH * 0.5f, parentBottom = -h * 0.5f;

            _driven.Clear();
            _driven.Add(this, RT, DrivenTransformProperties.All);
            Apply(Board, boardW, boardH, cx, cy);
            Apply(TopBar, boardW, parentTop - boardTop, cx, (parentTop + boardTop) * 0.5f);
            Apply(BottomBar, boardW, boardBottom - parentBottom, cx, (boardBottom + parentBottom) * 0.5f);

            // full-area overlays: full height, but the board/bars' width (aligned with them)
            if (FullLayout != null)
                foreach (var rt in FullLayout)
                    if (rt != null) Apply(rt, boardW, h, cx, 0f);
        }

        // Place one child (anchors/pivot centered) and let it report its new size to ITS children.
        void Apply(RectTransform rt, float width, float height, float centerX, float centerY) {
            rt.anchorMin = rt.anchorMax = Vector2.one * 0.5f;
            rt.pivot = Vector2.one * 0.5f;
            rt.localScale = Vector3.one;
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(centerX, centerY);
            _driven.Add(this, rt, DrivenTransformProperties.All);

            var invoker = rt.GetComponent<RTResizeInvoker>();
            if (invoker == null) invoker = rt.gameObject.AddComponent<RTResizeInvoker>();
            invoker.UpdateListener(false);
            invoker.ReporteSize();
        }
    }
}
