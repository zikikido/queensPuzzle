using System;
using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Tunables for the tournament, shipped as a single asset in Resources ("TournamentConfig").
    /// Only what the CLIENT decides lives here — when the feature opens up, when the card starts
    /// shouting that time is running out, and how many places get a gift. The tournament window
    /// and the group size come from the server, and the ranking is its call alone.
    ///
    /// Singleton: <see cref="Resources.Load"/> may only run on the main thread, and the tournament
    /// code awaits the server (a continuation can land on a background thread). So the asset is
    /// loaded once at boot, before any scene, and every later read is just the cached reference —
    /// safe from any thread. <see cref="Instance"/> is null when the asset is missing = feature off.
    /// </summary>
    [CreateAssetMenu(menuName = "QP/Tournament Config", fileName = "TournamentConfig")]
    public class TournamentConfig : ScriptableObject {

        const string Resource = "TournamentConfig";

        [Tooltip("Display stage number (1-based) that unlocks the tournament — the lock card's text.")]
        public int unlockLevel = 30;

        [Tooltip("Minutes left when the card switches to 'ending soon'.")]
        public int endingSoonMinutes = 60;

        [Tooltip("How many top places get a gift. The prizes themselves are ours, not the server's.")]
        public int prizePlaces = 3;

        [Tooltip("Most wins kept while offline. Past this the oldest are dropped — they belong to a " +
                 "tournament that has closed anyway, and the feature is online-only by design.")]
        public int maxPendingWins = 200;

        public TimeSpan EndingSoonTime => TimeSpan.FromMinutes(endingSoonMinutes);

        // ---- singleton ------------------------------------------------------------------

        static TournamentConfig _instance;

        /// <summary>The loaded asset, or null if it isn't shipped. Readable from any thread.</summary>
        public static TournamentConfig Instance {
            get {
#if UNITY_EDITOR
                if (_instance == null && !Application.isPlaying) _load();   // editor tools, no boot ran
#endif
                return _instance;
            }
        }

        /// <summary>Loaded here, on the main thread, before anything can ask for it.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void _load() {
            _instance = Resources.Load<TournamentConfig>(Resource);
            if (_instance == null)
                CDebug.LogError($"[TournamentConfig] Resources/{Resource}.asset not found - tournament is off.");
        }
    }
}
