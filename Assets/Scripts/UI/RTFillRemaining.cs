using System.Collections.Generic;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// The gameplay region. It is a <see cref="Core.RTResizeInvoker"/> (NOT an ABSMBListener) — it
    /// doesn't get sized by the parent; instead it sizes itself to fill the parent area MINUS the
    /// space its sibling bars take (the bottom banner now, a top banner later), then reports that
    /// region down to its own children (board, HUD, …).
    ///
    /// It listens to each sibling RTResizeInvoker's <c>AfterReporteSize</c>, so every time a banner
    /// finishes resizing this recomputes the fit. A sibling whose centre is above the parent centre
    /// eats from the top; below, from the bottom — so any number of top/bottom bars work.
    /// </summary>
    [ExecuteInEditMode]
    public class RTFillRemaining : Core.RTResizeInvoker
    {
        readonly List<Core.RTResizeInvoker> _siblings = new List<Core.RTResizeInvoker>();

        protected override void Awake()
        {
            base.Awake();
            if (RT == null) RT = GetComponent<RectTransform>();

            RT.localScale = Vector3.one;
            RT.pivot = Vector3.zero; // bottom-left, like RTSafeArea — we position in world space

            var dt = new DrivenRectTransformTracker();
            dt.Clear();
            dt.Add(this, RT, DrivenTransformProperties.All);
        }

        void OnEnable()
        {
            Resubscribe();
            Fit();
        }

        void OnDisable() => Unsubscribe();

        void Resubscribe()
        {
            Unsubscribe();
            var parent = transform.parent;
            if (parent == null) return;

            foreach (Transform child in parent)
            {
                if (child == transform) continue;
                var inv = child.GetComponent<Core.RTResizeInvoker>();
                if (inv == null) continue;
                _siblings.Add(inv);
                inv.AfterReporteSize += Fit;
            }
        }

        void Unsubscribe()
        {
            for (int i = 0; i < _siblings.Count; i++)
                if (_siblings[i] != null) _siblings[i].AfterReporteSize -= Fit;
            _siblings.Clear();
        }

        void Fit()
        {
            var parent = transform.parent as RectTransform;
            if (parent == null) return;

            var pc = new Vector3[4];
            parent.GetWorldCorners(pc); // 0 = bottom-left, 1 = top-left, 2 = top-right, 3 = bottom-right
            float left = pc[0].x, bottomY = pc[0].y, topY = pc[1].y, z = pc[0].z;
            float width = pc[3].x - pc[0].x;
            float parentMidY = (topY + bottomY) * 0.5f;

            var sc = new Vector3[4];
            for (int i = 0; i < _siblings.Count; i++)
            {
                var s = _siblings[i];
                if (s == null) continue;
                var srt = s.transform as RectTransform;
                if (srt == null) continue;

                srt.GetWorldCorners(sc);
                float sBottom = sc[0].y, sTop = sc[1].y;
                if ((sTop + sBottom) * 0.5f >= parentMidY)
                    topY = Mathf.Min(topY, sBottom);    // a bar in the top half lowers our top edge
                else
                    bottomY = Mathf.Max(bottomY, sTop); // a bar in the bottom half raises our bottom edge
            }

            float height = Mathf.Max(0f, topY - bottomY);

            RT.anchorMin = new Vector2(0.5f, 0.5f);
            RT.anchorMax = new Vector2(0.5f, 0.5f);
            RT.sizeDelta = new Vector2(width, height);
            RT.position = new Vector3(left, bottomY, z);

            ReporteSize();
        }
    }
}
