using UnityEngine;
using Puzzby;

namespace qp {

    // The game's own haptic vocabulary. The Puzzby.Haptics package is game-agnostic; these effects
    // are defined here at boot and then triggered by id from gameplay.
    public enum GameHaptic { Tap, Happy, Win, Wrong }

    public static class MBStartup {

        // Runs automatically when the app loads.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() {
            QualitySettings.vSyncCount = 0;      // so vSync doesn't override the target
            Application.targetFrameRate = 60;    // run at 60 FPS

            Haptics.Enabled = AppData.Haptics.Value;
            DefineHaptics();
        }

        // intensity = how strong (0..1), sharpness = dull/bassy (0) .. crisp/tick (1).
        static void DefineHaptics() {
            Haptics.Define(GameHaptic.Tap,   Haptics.New().Transient(0.85f, 0.8f));                // strong crisp "blop"
            Haptics.Define(GameHaptic.Happy, Haptics.New().Transient(0.5f, 0.9f)
                                                          .Transient(0.9f, 0.9f, 0.08f));           // bouncy double
            Haptics.Define(GameHaptic.Win,   Haptics.New().Transient(0.5f, 0.8f)
                                                          .Transient(1.0f, 0.9f, 0.06f)
                                                          .Transient(0.6f, 0.7f, 0.12f));           // rising celebration
            Haptics.Define(GameHaptic.Wrong, Haptics.New().Transient(1.0f, 0.4f)
                                                          .Transient(0.7f, 0.3f, 0.08f));           // heavy double thud
        }
    }
}
