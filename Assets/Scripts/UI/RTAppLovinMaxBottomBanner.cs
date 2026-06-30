using UnityEngine;

namespace Common
{
    /// <summary>
    /// A bottom-banner placeholder: full width (p_w) × banner height, pinned to the bottom of the safe
    /// area in world space — the same technique RTSafeArea uses, so its X / Y / size all track the
    /// parent and re-fit whenever the screen changes. Drop a placeholder Image on it to preview where
    /// the real banner will sit.
    ///
    /// Setup: just make it a child of the safe-area invoker (RTAutoSafeArea / RTSafeArea). It positions
    /// itself — no scene anchoring needed.
    ///
    /// Decoupled from the MAX SDK on purpose: the ads/MAX manager pushes the real banner height via
    /// <see cref="SetBannerHeightPixels"/> (e.g. from the OnAdLoaded callback) and calls
    /// <see cref="HideBanner"/> when it's gone — so this compiles with no SDK present. In the editor
    /// (and until a height is pushed) it shows the standard banner dp for the previewed device.
    /// </summary>
    [ExecuteInEditMode]
    public class RTAppLovinMaxBottomBanner : Core.RTResizeInvoker.ABSMBListener
    {
        [Tooltip("Standard MAX banner height in dp — auto-picked by device (phone vs tablet). Drives " +
                 "the editor preview and the reserve before the ad loads; at runtime the real pushed " +
                 "height takes over.")]
        public float PhoneBannerDp = 50f;
        public float TabletBannerDp = 90f;

        float _bannerPx;          // real banner height in screen pixels, pushed at runtime
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
        }

        /// <summary>Push the loaded banner's height (in screen pixels) from your ads/MAX manager —
        /// e.g. <c>MaxSdkUtils.GetAdaptiveBannerHeight() * (Screen.dpi / 160f)</c>. Re-lays out.</summary>
        public void SetBannerHeightPixels(float pixels)
        {
            _bannerPx = Mathf.Max(0f, pixels);
            _hasRuntimeHeight = true;
        }

        /// <summary>Banner removed (no fill / interstitial showing) — release the reserved strip.</summary>
        public void HideBanner() => SetBannerHeightPixels(0f);

        // Full width (p_w) × banner height, pinned to the parent's bottom-left in world space — the
        // same technique RTSafeArea uses, so X, Y, width and on-resize all track the parent. Recomputed
        // from the parent's real corners each report. No SafeArea here.
        protected override void Resize(float p_w, float p_h)
        {
            if (RT == null) RT = GetComponent<RectTransform>();
            var cam = Camera.main;
            var parent = transform.parent as RectTransform;
            if (cam == null || parent == null) return;

            float bannerPx = _hasRuntimeHeight ? _bannerPx : EstimatedBannerPixels();
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

        // Real banner height for the CURRENT screen: standard MAX 50dp phone / 90dp tablet → pixels.
        // The device simulator drives Screen.* in the editor, so this matches the previewed device.
        float EstimatedBannerPixels()
        {
            float density = Screen.dpi > 0f ? Screen.dpi / 160f : 2.625f; // ~xxhdpi fallback
            float minSideDp = Mathf.Min(Screen.width, Screen.height) / density;
            float dp = minSideDp >= 600f ? TabletBannerDp : PhoneBannerDp; // 600dp = Android tablet cutoff
            return dp * density;
        }
    }
}
