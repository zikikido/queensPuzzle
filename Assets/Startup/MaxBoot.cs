using System;
using UnityEngine;

namespace qp {

    // AppLovin MAX boot task. Init runs the Terms/Privacy flow automatically (Google UMP in
    // GDPR regions + iOS ATT), and OnSdkInitializedEvent fires only AFTER the user finishes
    // consent — so Resolved is the signal that consent is known.
    //
    // The `max` boot stage waits on Done with a ~5s cap, which gives the flow "GDPR → wait,
    // else 5s" for free: with runInBackground = false the consent form pauses the app, so the
    // boot coroutine freezes and simply resumes when the user dismisses it (the cap can't fire
    // while nothing polls). With no form (non-GDPR, or offline where the SDK just retries the
    // network ~30s for nothing) the app stays active and the cap releases the boot. Consent that
    // lands after the cap is still applied via WhenResolved, and ads self-gate on their own flag.
    public static class MaxBoot {

        // True once MAX finished initializing and Geography is known.
        public static bool Done { get; private set; }
        public static bool Resolved { get; private set; }

        // Region the consent flow decided the user is in — Unknown until init completes.
        public static MaxSdkBase.ConsentFlowUserGeography Geography { get; private set; }
            = MaxSdkBase.ConsentFlowUserGeography.Unknown;

        public static bool InGdprRegion => Geography == MaxSdkBase.ConsentFlowUserGeography.Gdpr;

        // Callbacks waiting for consent to resolve. Fired once, on the main thread, when MAX
        // initializes — which may be long after boot (slow network) or not at all this session.
        static Action _pending;

        /// <summary>Run cb now if consent is already resolved, else when it resolves.</summary>
        public static void WhenResolved(Action cb) {
            if (cb == null) return;
            if (Resolved) cb();
            else _pending += cb;
        }

        public static void Begin() {
            MaxSdkCallbacks.OnSdkInitializedEvent += OnInitialized;
            MaxSdk.InitializeSdk();   // async: the `max` stage polls Done (see class comment)
        }

        static void OnInitialized(MaxSdkBase.SdkConfiguration config) {
            MaxSdkCallbacks.OnSdkInitializedEvent -= OnInitialized;
            Geography = config.ConsentFlowUserGeography;
            Resolved = true;
            Done = true;
            Debug.Log($"[MaxBoot] MAX initialized — consent geography: {Geography}");
            var pending = _pending;
            _pending = null;
            pending?.Invoke();
        }
    }
}
