using System.Collections.Generic;
using Singular;
using UnityEngine;

namespace qp {

    // Singular boot task. Manual init on purpose: the SingularSDKObject in the boot scene has
    // InitializeOnAwake OFF, so nothing tracks before this task decides to.
    //
    // Does NOT wait for the consent flow: initializes immediately with partner data sharing
    // OFF (the privacy-safe state, valid even in GDPR before consent), so the install and every
    // session are recorded without stalling the boot on MAX. The real consent decision is applied
    // via MaxBoot.WhenResolved — whenever it lands (seconds later, or not at all this session).
    public static class SingularBoot {

        public static bool Done { get; private set; }

        public static void Begin() {
            // Must be registered BEFORE init. Fires (main thread, via UnitySendMessage) once Singular
            // resolves the install's attribution — only if the callback is enabled for the app on
            // Singular's side (BETA feature; requested from support 2026-08-17). No-op in the Editor.
            SingularSDK.SetSingularDeviceAttributionCallbackHandler(new AttributionHandler());

            // Same id events-server gets as `user_id` — lets Singular's BI postbacks / export logs
            // (custom_user_id) be joined to our events. Set BEFORE init so the install session carries it.
            SingularSDK.SetCustomUserId(Common.UserID.GetUserIDLocal());

            SingularSDK.InitializeSingularSDK();
            SingularSDK.LimitDataSharing(true);   // safe default until consent resolves (same frame → nothing shared before)

            // Apply the real consent whenever MAX resolves the region (may be after boot).
            MaxBoot.WhenResolved(ApplyConsent);

            Debug.Log($"[SingularBoot] Singular initialized (partner sharing limited until consent), source {(AppData.SingularSource.Value != null ? AppData.SingularSource.Value.ToString() : "unknown")}");
            Done = true;
        }

        // Outside a GDPR region → full attribution sharing. In a GDPR region → stays limited
        // (LimitDataSharing = true = opted out).
        // TODO tighten: read the exact UMP/TCF consent bit to flip LimitDataSharing precisely.
        static void ApplyConsent() {
            bool limitSharing = MaxBoot.InGdprRegion;
            if (!limitSharing) SingularSDK.TrackingOptIn();
            SingularSDK.LimitDataSharing(limitSharing);
            Debug.Log($"[SingularBoot] consent applied — geography {MaxBoot.Geography}, limitDataSharing {limitSharing}");
        }

        // Persists the resolved attribution; Analytics attaches it to every events-server event.
        class AttributionHandler : SingularDeviceAttributionCallbackHandler {
            public void OnSingularDeviceAttributionCallback(Dictionary<string, object> info) {
                var source = SingularSource.FromCallback(info);
                AppData.SingularSource.Save(source);
                Debug.Log($"[SingularBoot] attribution resolved — {source}");
            }
        }
    }
}
