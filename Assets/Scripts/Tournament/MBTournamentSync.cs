using UnityEngine;

namespace qp {

    /// <summary>Drives <see cref="TournamentManager.Sync"/>: shortly after boot, every
    /// <see cref="Interval"/> seconds, and when the app comes back to the foreground.</summary>
    public sealed class MBTournamentSync : MonoBehaviour {

        const float FirstDelay = 3f;
        const float Interval = 30f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap() {
            var go = new GameObject("TournamentSync");
            go.AddComponent<MBTournamentSync>();
            DontDestroyOnLoad(go);
        }

        void Start() => InvokeRepeating(nameof(Tick), FirstDelay, Interval);

        void OnApplicationFocus(bool focus) { if (focus) Tick(); }

        void Tick() => _ = TournamentManager.Sync();
    }
}
