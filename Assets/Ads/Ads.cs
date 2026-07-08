using Singular;
using System;
using System.Collections;
using UnityEngine;

namespace qp {

    // AppLovin MAX wrapper: banner + interstitial + rewarded, with auto-load/retry and
    // impression-level ad-revenue forwarding to Singular (+ Firebase). Nothing here is wired to
    // gameplay yet — callers use ShowRewarded / ShowInterstitial / ShowBanner / HideBanner.
    //
    // MAX must be initialized first (MaxBoot); Init() only loads ads, it never blocks the boot.
    public static class Ads {

        // ---- ad unit IDs (from the AppLovin dashboard) ----
        // iOS units not created yet — fill when you add an iOS build.
        const string Placeholder = "TODO_AD_UNIT_ID";
        const string RewardedAndroid     = "4380e51f1bc2f8ed";
        const string RewardedIOS         = Placeholder;
        const string InterstitialAndroid = "72a837c75ecbf3b6";
        const string InterstitialIOS     = Placeholder;
        const string BannerAndroid       = "265b0eafdc3e0341";
        const string BannerIOS           = Placeholder;

#if UNITY_IOS
        static readonly string RewardedId = RewardedIOS, InterstitialId = InterstitialIOS, BannerId = BannerIOS;
#else
        static readonly string RewardedId = RewardedAndroid, InterstitialId = InterstitialAndroid, BannerId = BannerAndroid;
#endif

        const int MaxRetryPow = 6;           // backoff caps at 2^6 = 64s
        static readonly Color BannerBg = new Color32(0xDF, 0xF0, 0xF5, 0xFF);   // #DFF0F5 — the game's banner strip

        static AdsRunner _runner;
        static int _rewardedRetry, _interstitialRetry;
        static bool _rewardedEarned;
        static Action<bool> _onRewardedDone;
        static Action _onInterstitialClosed;
        static bool _bannerCreated;

        public static bool IsRewardedReady => Usable(RewardedId) && MaxSdk.IsRewardedAdReady(RewardedId);
        public static bool IsInterstitialReady => Usable(InterstitialId) && MaxSdk.IsInterstitialReady(InterstitialId);

        // Boot task (stage after MAX): subscribe callbacks and kick off loading. Returns
        // immediately — ad loading happens in the background, the boot never waits on it.
        public static void Init() {
            if (_runner == null) {
                var go = new GameObject("$Ads");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _runner = go.AddComponent<AdsRunner>();
            }

            // Revenue → Singular (+ Firebase) for every format (Singular's documented MAX hook).
            MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent += OnRevenuePaid;
            MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent += OnRevenuePaid;
            MaxSdkCallbacks.Banner.OnAdRevenuePaidEvent += OnRevenuePaid;

            WireRewarded();
            WireInterstitial();

            if (Usable(RewardedId)) MaxSdk.LoadRewardedAd(RewardedId);
            if (Usable(InterstitialId)) MaxSdk.LoadInterstitial(InterstitialId);
            CreateBanner();

            if (!Usable(RewardedId) || !Usable(InterstitialId) || !Usable(BannerId))
                Debug.LogWarning("[Ads] Some ad unit IDs are still placeholders — fill them in Ads.cs. Those formats won't load.");
        }

        // ================== rewarded ==================

        static void WireRewarded() {
            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent       += (id, info) => _rewardedRetry = 0;
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent   += (id, err)  => RetryLoad(ref _rewardedRetry, () => MaxSdk.LoadRewardedAd(RewardedId));
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += (id, reward, info) => _rewardedEarned = true;
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += (id, err, info) => FinishRewarded();
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent       += (id, info) => FinishRewarded();
        }

        /// <summary>Show a rewarded ad. onDone(true) if the reward was earned, else onDone(false).</summary>
        public static void ShowRewarded(Action<bool> onDone) {
            if (!IsRewardedReady) { onDone?.Invoke(false); return; }
            _onRewardedDone = onDone;
            _rewardedEarned = false;
            MaxSdk.ShowRewardedAd(RewardedId);
        }

        static void FinishRewarded() {
            var cb = _onRewardedDone; _onRewardedDone = null;
            bool earned = _rewardedEarned; _rewardedEarned = false;
            MaxSdk.LoadRewardedAd(RewardedId);   // preload the next one
            cb?.Invoke(earned);
        }

        // ================== interstitial ==================

        static void WireInterstitial() {
            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent        += (id, info) => _interstitialRetry = 0;
            MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent    += (id, err)  => RetryLoad(ref _interstitialRetry, () => MaxSdk.LoadInterstitial(InterstitialId));
            MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += (id, err, info) => FinishInterstitial();
            MaxSdkCallbacks.Interstitial.OnAdHiddenEvent        += (id, info) => FinishInterstitial();
        }

        /// <summary>Show an interstitial; onClosed fires when it's dismissed (or immediately if none ready).</summary>
        public static void ShowInterstitial(Action onClosed = null) {
            if (!IsInterstitialReady) { onClosed?.Invoke(); return; }
            _onInterstitialClosed = onClosed;
            MaxSdk.ShowInterstitial(InterstitialId);
        }

        static void FinishInterstitial() {
            var cb = _onInterstitialClosed; _onInterstitialClosed = null;
            MaxSdk.LoadInterstitial(InterstitialId);   // preload the next one
            cb?.Invoke();
        }

        // ================== banner ==================

        static void CreateBanner() {
            if (_bannerCreated || !Usable(BannerId)) return;
            MaxSdk.CreateBanner(BannerId, new MaxSdkBase.AdViewConfiguration(MaxSdkBase.AdViewPosition.BottomCenter));
            MaxSdk.SetBannerBackgroundColor(BannerId, BannerBg);
            MaxSdk.HideBanner(BannerId);   // created hidden — shown on demand
            _bannerCreated = true;
        }

        public static bool BannerVisible { get; private set; }
        public static void ShowBanner() { if (_bannerCreated) { MaxSdk.ShowBanner(BannerId); BannerVisible = true; } }
        public static void HideBanner() { if (_bannerCreated) { MaxSdk.HideBanner(BannerId); BannerVisible = false; } }

        // ================== revenue → Singular (+ Firebase) ==================

        static void OnRevenuePaid(string adUnitId, MaxSdkBase.AdInfo info) {
            if (info == null || info.Revenue <= 0) return;

            // Singular — the ROAS piece (fields per Singular's MAX ad-revenue doc, enriched).
            var data = new SingularAdData("AppLovin", "USD", info.Revenue)
                .WithNetworkName(info.NetworkName)
                .WithAdType(info.AdFormat)
                .WithAdUnitId(info.AdUnitIdentifier)
                .WithAdPlacmentName(info.Placement)
                .WithPrecision(info.RevenuePrecision);
            SingularSDK.AdRevenue(data);

#if !IGNORE_FIREBASE
            if (Common.FirebaseBootstrap.FBAvailable) {
                Firebase.Analytics.FirebaseAnalytics.LogEvent("ad_impression",
                    new Firebase.Analytics.Parameter("ad_platform", "AppLovin"),
                    new Firebase.Analytics.Parameter("ad_source", info.NetworkName ?? ""),
                    new Firebase.Analytics.Parameter("ad_format", info.AdFormat ?? ""),
                    new Firebase.Analytics.Parameter("ad_unit_name", info.AdUnitIdentifier ?? ""),
                    new Firebase.Analytics.Parameter("currency", "USD"),
                    new Firebase.Analytics.Parameter("value", info.Revenue));
            }
#endif
            Debug.Log($"[Ads] revenue {info.Revenue:F6} USD — {info.NetworkName} / {info.AdFormat} → Singular");
        }

        // ================== helpers ==================

        static bool Usable(string id) => !string.IsNullOrEmpty(id) && id != Placeholder;

        // Exponential backoff (2^n s, capped), per MAX's recommended retry pattern.
        static void RetryLoad(ref int retry, Action load) {
            retry++;
            float delay = Mathf.Pow(2f, Mathf.Min(MaxRetryPow, retry));
            if (_runner != null) _runner.RunAfter(delay, load);
        }

        // Tiny coroutine host for retry delays and DontDestroyOnLoad ownership.
        class AdsRunner : MonoBehaviour {
            public void RunAfter(float seconds, Action action) => StartCoroutine(After(seconds, action));
            IEnumerator After(float s, Action a) { yield return new WaitForSeconds(s); a?.Invoke(); }
        }
    }
}
