using UnityEngine;

namespace Common
{
    /// <summary>
    /// Reserves a fixed strip at the bottom of the safe area so the native MAX banner overlay never
    /// covers game content. The strip is set ONCE at the device banner height and never changes
    /// during play (stable layout); only a future "remove ads" purchase releases it. The banner
    /// ad's VISIBILITY is driven by MBGameplay (qp.Ads.ShowBanner/HideBanner), not here — this
    /// component only owns the reserved layout space. The overlay is hidden by whoever leaves play
    /// (win/fail/back), so it never leaks into another scene.
    /// </summary>
    [ExecuteInEditMode]
    public class RTAppLovinMaxBottomBanner : Core.RTResizeInvoker.ABSMBListener
    {
        float _bannerPx;          // reserved strip height in screen pixels
        bool _hasRuntimeHeight;

        protected override void Awake()
        {
            base.Awake();
            if (RT == null) RT = GetComponent<RectTransform>(); // safety if not serialized yet

            RT.localScale = Vector3.one;
            RT.pivot = Vector3.zero; // bottom-left, like RTSafeArea — we set position in world space

            var dt = new DrivenRectTransformTracker();
            dt.Clear();
            dt.Add(this, RT, DrivenTransformProperties.All);

            // Reserve the strip at the device banner height — permanent (stable layout).
            SetBannerHeightPixels(qp.Ads.GetBannerHeightPixels());
        }

        /// <summary>Push the reserved strip height (screen pixels). Re-lays out.</summary>
        public void SetBannerHeightPixels(float pixels)
        {
            _bannerPx = Mathf.Max(0f, pixels);
            _hasRuntimeHeight = true;
        }

        // Full width (p_w) × banner height, pinned to the parent's bottom-left in world space — the
        // same technique RTSafeArea uses, so X, Y, width and on-resize all track the parent. Recomputed
        // from the parent's real corners each report. No SafeArea here.
        protected override void Resize(float p_w, float p_h)
        {
            if (RT == null) RT = GetComponent<RectTransform>();
            var cam = Camera.main;
            var parent = transform.parent as RectTransform;
            if (cam == null || parent == null) return;

            float bannerPx = _hasRuntimeHeight ? _bannerPx : qp.Ads.GetBannerHeightPixels();
            float z = transform.position.z + -(cam.transform.position.z);
            float bannerH = Mathf.Abs(cam.ScreenToWorldPoint(new Vector3(0f, bannerPx, z)).y
                                    - cam.ScreenToWorldPoint(new Vector3(0f, 0f, z)).y);

            var c = new Vector3[4];
            parent.GetWorldCorners(c); // [0] = world bottom-left of the safe-area parent

            RT.anchorMin = new Vector2(0.5f, 0.5f);
            RT.anchorMax = new Vector2(0.5f, 0.5f);
            RT.sizeDelta = new Vector2(p_w, bannerH);
            RT.position = c[0]; // pivot (0,0) → banner bottom-left at the parent's bottom-left

            ReporteSize();
        }
    }
}
