using UnityEngine;

namespace qp {
    public static class MBStartup {

        // Runs automatically when the app loads.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() {
            QualitySettings.vSyncCount = 0;      // so vSync doesn't override the target
            Application.targetFrameRate = 60;    // run at 60 FPS
        }
    }
}
