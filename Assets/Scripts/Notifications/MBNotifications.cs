#if NOTIFICATION_INSTALLER

using UnityEngine;

namespace qp {

    /// <summary>
    /// Tiny lifecycle bridge: a persistent GameObject (spawned at boot by MBStartup) that forwards
    /// Unity's app-lifecycle callbacks to the static <see cref="NotificationManager"/>. All real
    /// logic lives in the manager — this only exists because pause/focus callbacks need a
    /// MonoBehaviour to receive them.
    /// </summary>
    public class MBNotifications : MonoBehaviour {

        void Awake() {
            DontDestroyOnLoad(gameObject);
            NotificationManager.OnAppLoad();
        }

        // OnApplicationPause(true) is the reliable "went to background" signal on both Android and
        // iOS; (false) is the return to foreground. The manager guards against duplicate firing.
        void OnApplicationPause(bool paused) {
            if (paused) NotificationManager.OnAppBackground();
            else NotificationManager.OnAppResume();
        }
    }
}

#endif
