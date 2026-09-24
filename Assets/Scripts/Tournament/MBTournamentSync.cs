using UnityEngine;

namespace qp {

    /// <summary>Drives <see cref="TournamentManager.Sync"/> in the background: every
    /// <see cref="Interval"/> seconds and whenever the app comes back to the foreground.
    /// Created by <see cref="TournamentManager.Init"/> at boot, early — the first sync is held
    /// until server time is trusted and then fires at once, so the answer is usually back before
    /// the lobby appears.</summary>
    public sealed class MBTournamentSync : MonoBehaviour {

        const float Interval = 30f;

        /// <summary>Create the one runner (a second call does nothing).</summary>
        public static void Create() => throw new System.NotImplementedException();
    }
}
