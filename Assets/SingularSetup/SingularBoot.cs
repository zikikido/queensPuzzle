using Singular;
using UnityEngine;

namespace qp {

    // Singular boot task. Manual init on purpose: the SingularSDKObject in the boot scene has
    // InitializeOnAwake OFF, so nothing tracks before this task decides to.
    public static class SingularBoot {

        public static bool Done { get; private set; }

        public static void Begin() {
            // TODO consent gate: once the CMP (Google UMP) flow exists, run it first —
            //   consent given → InitializeSingularSDK() + TrackingOptIn() + LimitDataSharing(false)
            //   consent denied (EEA) → skip init entirely
            // and on iOS run the ATT prompt before init as well.
            SingularSDK.InitializeSingularSDK();

            Done = true;
        }
    }
}
