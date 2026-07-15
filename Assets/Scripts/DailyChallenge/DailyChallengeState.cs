using System;
using Common;

namespace qp {

    /// <summary>
    /// TODAY's daily challenge, saved as one JSON blob (PlayerPrefs): which day the blob belongs
    /// to, the tier locked at first open (progress gained later that day must not switch the
    /// puzzle), the accumulated solve timer (survives restarts and fails), and the solved result
    /// for the lobby card. A new UTC day makes the blob stale by dayIndex — DailyChallengeManager starts
    /// fresh; an unfinished yesterday simply never counted.
    /// Mutated only by <see cref="DailyChallengeManager"/>.
    /// </summary>
    [Serializable]
    public class DailyChallengeState {

        static readonly PlayerPrefsHelper.ObjectHolder<DailyChallengeState> _holder
            = new PlayerPrefsHelper.ObjectHolder<DailyChallengeState>("qp_daily_state");

        public int dayIndex = -1;   // which calendar day (days since epoch) this blob is for; -1 = never played
        public int tier = -1;       // tier locked for that day
        public float timeSec;       // accumulated ACTIVE play time — pauses off-board, continues across restarts/fails
        public bool solved;
        public int topPct;          // "TOP X %" — fake for now (see DailyChallengeManager.FakeTopPct)

        public static DailyChallengeState Load() => _holder.Value ?? new DailyChallengeState();

        public void Save() => _holder.Save(this);
    }
}
