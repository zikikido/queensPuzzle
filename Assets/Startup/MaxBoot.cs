using UnityEngine;

namespace qp {

    // AppLovin MAX boot task. Init runs the Terms/Privacy flow automatically (Google UMP in
    // GDPR regions + iOS ATT), and OnSdkInitializedEvent fires only AFTER the user finishes
    // consent — so this task completing is the signal that consent is resolved. Singular and
    // any other data SDK gate on MaxBoot.Done (see SingularBoot).
    public static class MaxBoot {

        public static bool Done { get; private set; }

        // Region the consent flow decided the user is in — drives the gating downstream.
        // Unknown until init completes.
        public static MaxSdkBase.ConsentFlowUserGeography Geography { get; private set; }
            = MaxSdkBase.ConsentFlowUserGeography.Unknown;

        public static bool InGdprRegion => Geography == MaxSdkBase.ConsentFlowUserGeography.Gdpr;

        public static void Begin() {
            MaxSdkCallbacks.OnSdkInitializedEvent += OnInitialized;
            MaxSdk.InitializeSdk();
        }

        static void OnInitialized(MaxSdkBase.SdkConfiguration config) {
            MaxSdkCallbacks.OnSdkInitializedEvent -= OnInitialized;
            Geography = config.ConsentFlowUserGeography;
            Debug.Log($"[MaxBoot] MAX initialized — consent geography: {Geography}");
            Done = true;
        }
    }
}
