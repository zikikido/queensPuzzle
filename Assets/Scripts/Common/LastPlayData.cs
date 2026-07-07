using System;
using Common;

namespace qp {

    /// <summary>
    /// Everything about the CURRENT attempt, saved as one JSON blob (PlayerPrefs) so an app exit
    /// mid-level restores exactly where the player stopped: the board plus the attempt's meta —
    /// boost uses, added lives, bones lost. Valid only while forLevelIdx matches AppData.LevelIdx;
    /// counters reset per attempt (a fresh board = a fresh LastPlayData).
    /// Access via AppData.LastPlayData; call Save() after changing fields outside SaveBoard.
    /// </summary>
    [Serializable]
    public class LastPlayData {

        // Common's JSON-in-PlayerPrefs saver does the persistence
        static readonly PlayerPrefsHelper.ObjectHolder<LastPlayData> _holder
            = new PlayerPrefsHelper.ObjectHolder<LastPlayData>("qp_last_play");

        public int forLevelIdx = -1;   // which level this save belongs to; -1 = no valid save
        public string board = "";      // one char per cell (0/X/Q/W); "" = no saved board

        // the attempt's meta — reported on game_start/win/lose
        public int hintsUsed;
        public int queenBoostsUsed;
        public int undosUsed;
        public int livesAdded;         // fail-continue grants (+3 each, later: video/coins)
        public int bonesLost;          // lives lost; not derivable from cells (continue keeps wrong queens)

        public static LastPlayData Load() => _holder.Value ?? new LastPlayData();

        public void Save() => _holder.Save(this);

        /// <summary>A zeroed attempt on the given level, saved immediately.</summary>
        public static LastPlayData StartFresh(int levelIdx) {
            var data = new LastPlayData { forLevelIdx = levelIdx };
            data.Save();
            return data;
        }

        /// <summary>Nothing to restore anymore (level won, failed, or debug reset).</summary>
        public void Invalidate() {
            forLevelIdx = -1;
            board = "";
            Save();
        }

        // The fail-popup limbo: the attempt is invalidated on fail (quitting restarts fresh),
        // but Continue must bring it back — so it waits in memory between the two.
        static LastPlayData _stash;

        /// <summary>Keep an in-memory copy of this attempt (call before Invalidate).</summary>
        public void Stash() => _stash = (LastPlayData)MemberwiseClone();

        /// <summary>The stashed attempt, alive and saved again; falls back to Load() when empty.</summary>
        public static LastPlayData Unstash() {
            var data = _stash ?? Load();
            _stash = null;
            data.Save();
            return data;
        }

#if !IGNORE_FIREBASE
        /// <summary>The attempt's meta as Firebase event parameters.</summary>
        public Firebase.Analytics.Parameter[] ToParams() => new[] {
            new Firebase.Analytics.Parameter("hints", hintsUsed),
            new Firebase.Analytics.Parameter("queen_boosts", queenBoostsUsed),
            new Firebase.Analytics.Parameter("undos", undosUsed),
            new Firebase.Analytics.Parameter("lives_added", livesAdded),
            new Firebase.Analytics.Parameter("bones_lost", bonesLost),
        };
#endif
    }
}
