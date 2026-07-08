using Singular;
using UnityEngine;

namespace qp {

    // Singular boot task. Manual init on purpose: the SingularSDKObject in the boot scene has
    // InitializeOnAwake OFF, so nothing tracks before this task decides to.
    //
    // Registered in a LATER boot stage than MAX (see MBStartup), so by the time Begin() runs the
    // consent flow has already resolved and MaxBoot.Geography is set.
    public static class SingularBoot {

        public static bool Done { get; private set; }

        public static void Begin() {
            // Outside a GDPR region → full attribution. In a GDPR region → still initialize after
            // the flow, but limit partner data sharing (LimitDataSharing = true = opted out).
            // TODO tighten: read the exact UMP/TCF consent bit to flip LimitDataSharing precisely.
            bool limitSharing = MaxBoot.InGdprRegion;

            SingularSDK.InitializeSingularSDK();
            if (!limitSharing) SingularSDK.TrackingOptIn();
            SingularSDK.LimitDataSharing(limitSharing);

            Debug.Log($"[SingularBoot] Singular initialized — geography {MaxBoot.Geography}, limitDataSharing {limitSharing}");
            Done = true;
        }
    }
}
