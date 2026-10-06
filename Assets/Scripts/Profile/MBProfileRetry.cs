using UnityEngine;

namespace qp {

    /// <summary>
    /// Keeps trying while the profile is unsent. A profile is sent the moment it is edited, so this
    /// only has work when that send didn't get through — no connection yet at boot, or the server
    /// was down. It asks every <see cref="Interval"/> seconds and whenever the app comes back to the
    /// foreground; the pusher itself returns at once when there is nothing to send, so a confirmed
    /// profile costs nothing.
    /// Created by <see cref="ProfileManager.Init"/>.
    /// </summary>
    public sealed class MBProfileRetry : MonoBehaviour {

        const float Interval = 5f;

        static MBProfileRetry _instance;

        ProfilePusher _pusher;

        /// <summary>Create the one retry (a second call does nothing).</summary>
        public static void Create(ProfilePusher pusher) {
            if (_instance != null) return;
            var go = new GameObject("ProfileRetry");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<MBProfileRetry>();
            _instance._pusher = pusher;
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void Start() => InvokeRepeating(nameof(Tick), Interval, Interval);

        void Tick() => _ = _pusher.PushIfUnsent();

        // Back from the background — the connection is often back with it.
        void OnApplicationFocus(bool focus) { if (focus) _ = _pusher.PushIfUnsent(); }
    }
}
