using UnityEngine;

namespace qp {

    /// <summary>
    /// Not a scheduler: a profile is pushed when it is edited, and this only exists for the push
    /// that didn't get through. It waits for the app to come back to the foreground and tries the
    /// unsent profile again — nothing else, no interval, no work while everything is confirmed.
    /// Created by <see cref="ProfileManager.Init"/>.
    /// </summary>
    public sealed class MBProfilePushRetry : MonoBehaviour {

        static MBProfilePushRetry _instance;

        /// <summary>Create the one retry (a second call does nothing).</summary>
        public static void Create() => throw new System.NotImplementedException();

        void OnDestroy() { if (_instance == this) _instance = null; }

        void OnApplicationFocus(bool focus) { if (focus) _ = ProfileManager.PushIfUnsent(); }
    }
}
