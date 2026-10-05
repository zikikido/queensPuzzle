using System.Threading.Tasks;
using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Decides WHEN a sync runs; <see cref="TournamentSyncer"/> decides what one is. Everything
    /// about scheduling lives here: the interval, the clock gate, forcing one, and coming back to
    /// the foreground. Created once by <see cref="TournamentManager.Init"/> and kept across scenes.
    ///
    /// The interval counts from the last ATTEMPT, so forcing a sync (a level win, opening the
    /// leaderboard) pushes the next one away, and a failed attempt waits its turn instead of
    /// hammering the server.
    /// </summary>
    public sealed class MBTournamentSyncRunner : MonoBehaviour {

        const float Interval = 30f;

        static MBTournamentSyncRunner _instance;

        TournamentSyncer _syncer;
        float _lastAttempt = float.NegativeInfinity;   // realtime, so the first one fires at once

        /// <summary>Create the one runner (a second call does nothing).</summary>
        public static void Create(TournamentSyncer syncer) {
            if (_instance != null) return;
            var go = new GameObject("TournamentSync");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<MBTournamentSyncRunner>();
            _instance._syncer = syncer;
        }

        /// <summary>Sync now (a win, a screen opening) — and push the scheduled one away.
        /// Does nothing before the clock is trusted; the tick will pick it up.</summary>
        public static Task RunNow() => _instance != null ? _instance._run() : Task.CompletedTask;

        void OnDestroy() { if (_instance == this) _instance = null; }

        void Start() => InvokeRepeating(nameof(Tick), 0f, 1f);

        void Tick() {
            if (Time.realtimeSinceStartup - _lastAttempt >= Interval) _ = _run();
        }

        // Back from the background: the table is stale and a tournament may have ended meanwhile.
        void OnApplicationFocus(bool focus) { if (focus) _ = _run(); }

        Task _run() {
            // Without trusted server time the feature is offline anyway — don't spend a request.
            if (!MBServerTimeManagerV2.IsTimeSynced) return Task.CompletedTask;
            // One already in flight: it will bring the same answer, and the schedule must not be
            // pushed away by an attempt that never happened.
            if (_syncer.Running) return Task.CompletedTask;
            _lastAttempt = Time.realtimeSinceStartup;
            return _syncer.Run();
        }
    }
}
