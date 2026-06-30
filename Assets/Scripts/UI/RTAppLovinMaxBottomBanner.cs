using UnityEngine;

namespace Common
{
    /// <summary>
    /// Drives the HEIGHT of a bottom banner placeholder to match an AppLovin MAX banner. Like every
    /// other LayoutsPlus component it only sets size — you anchor the RectTransform to the bottom of
    /// the safe area in the scene (stretch width, anchored bottom). Setting anchors/position in code
    /// is what flipped it in the Device Simulator; letting the scene anchor it does not.
    ///
    /// Setup: child of the safe-area invoker (RTAutoSafeArea / RTSafeArea), anchors = bottom-stretch
    /// (anchorMin (0,0), anchorMax (1,0), pivot (0.5,0)). Drop a placeholder Image on it to preview.
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

            // We ONLY drive the height — anchoring/position is set in the scene (like every other
            // LayoutsPlus component), which is why those never flip in the Device Simulator.
            var dt = new DrivenRectTransformTracker();
            dt.Clear();
            dt.Add(this, RT, DrivenTransformProperties.SizeDeltaY);
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

        // Drives ONLY the height: convert the banner pixels to world units (camera, no SafeArea) and
        // set sizeDelta.y. Position is up to the scene — anchor this RectTransform to the bottom of the
        // safe area (stretch width, anchored bottom) the same way you anchor any LayoutsPlus element.
        protected override void Resize(float p_w, float p_h)
        {
            if (RT == null) RT = GetComponent<RectTransform>();
            var cam = Camera.main;
            if (cam == null) return;

            float bannerPx = _hasRuntimeHeight ? _bannerPx : EstimatedBannerPixels();
            float z = transform.position.z + -(cam.transform.position.z);
            float bannerH = Mathf.Abs(cam.ScreenToWorldPoint(new Vector3(0f, bannerPx, z)).y
                                    - cam.ScreenToWorldPoint(new Vector3(0f, 0f, z)).y);

            RT.sizeDelta = new Vector2(p_w, bannerH);

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
