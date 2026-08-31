using System;
using UnityEngine;

namespace qp {

    // AppLovin MAX boot task. Init runs the Terms/Privacy flow automatically (Google UMP in
    // GDPR regions + iOS ATT), and OnSdkInitializedEvent fires only AFTER the user finishes
    // consent — so Resolved is the signal that consent is known.
    //
    // The `max` boot stage waits on Done with a ~5s cap, held open while the consent form is up.
    // The two platforms get there differently:
    //  - Android: the flow pauses the Unity activity itself, so the boot coroutine freezes and
    //    resumes when the user dismisses it — the cap can't fire while nothing polls.
    //  - iOS: the form is a view controller presented INSIDE the still-active app, so the engine
    //    keeps running under it; the max task holds its timeout via NativeModal.IsShowing while
    //    any modal is presented over the Unity view.
    // With no form (non-GDPR, or offline where the SDK just retries the network ~30s for nothing)
    // nothing is presented and the cap releases the boot after 5s. Consent that lands after the
    // cap is still applied via WhenResolved, and ads self-gate on their own flag.
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
