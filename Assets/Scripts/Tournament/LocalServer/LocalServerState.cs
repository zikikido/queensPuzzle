using System;
using System.Collections.Generic;
using Common;

namespace qp {

    /// <summary>
    /// The local server's OWN database — what a real tournament server would keep in Mongo.
    ///
    /// It is deliberately not <see cref="TournamentState"/>. That one is what the CLIENT knows;
    /// this is what the server knows, and the two are only allowed to meet through a
    /// <see cref="TournamentSyncResult"/>. Mixing them would be comfortable today and expensive
    /// the day a real server arrives, because the client would be leaning on things that live
    /// nowhere but here: which recordings the rivals came from, which win ids were applied,
    /// what the player scored last time.
    ///
    /// Its own PlayerPrefs key, for the same reason.
    /// </summary>
    [Serializable]
    public class LocalServerState {

        static readonly PlayerPrefsHelper.ObjectHolder<LocalServerState> _holder
            = new PlayerPrefsHelper.ObjectHolder<LocalServerState>("qp_local_tournament");

        // ---- the tournament running now --------------------------------------------------

        /// <summary>Which 48-hour window the group below belongs to; "" before the first one.</summary>
        public string currentId = "";
        public long startTicks;
        public long endTicks;

        /// <summary>What the player has scored in it. 0 = they have not joined yet.</summary>
        public int myScore;

        /// <summary>Win ids already applied. The client resends everything it holds, so this is
        /// what makes a resend harmless — and it is cleared with the tournament, since ids from a
        /// closed one can never be applied again anyway.</summary>
        public List<string> acceptedIds = new List<string>();

        public List<LocalRival> rivals = new List<LocalRival>();

        // ---- the last closed tournament ---------------------------------------------------
        // Frozen at the moment it ended, names and final scores only: the recordings have done
        // their job and the table must never disagree with what the player saw a minute earlier.

        public string closedId = "";
        public long closedStartTicks;
        public long closedEndTicks;
        public int closedMyScore;
        public List<LocalEntry> closedTable = new List<LocalEntry>();

        // ---- what the next group is built from ---------------------------------------------

        /// <summary>
        /// What the player scored in each of their last few tournaments, newest first — the
        /// measurement the next group is sized against (see <see cref="RivalGroup"/>). Nothing
        /// else predicts a player's pace, so the first tournament assumes the median and every one
        /// after it knows.
        ///
        /// Several and not just the last one, because one unusual 48 hours should not decide the
        /// next group: a holiday, an illness or a single long evening would each set the bar on
        /// their own, and the player would spend the following tournament against opponents built
        /// for somebody else.
        /// </summary>
        public List<int> recentScores = new List<int>();

        /// <summary>How many to keep — see <see cref="UsualScore"/> for why it is odd.</summary>
        public const int RecentScoresKept = 3;

        /// <summary>
        /// What this player normally scores in 48 hours, or 0 when they have never finished a
        /// tournament.
        ///
        /// The MEDIAN of what we have, not the mean: a mean lets one binge evening drag the bar up
        /// and hand them a tournament built for a player they are not, and lets one quiet week drag
        /// it down and hand them one they cannot lose. The median ignores a single outlier in
        /// either direction and still follows a real change in pace within two tournaments.
        /// </summary>
        public int UsualScore {
            get {
                if (recentScores == null || recentScores.Count == 0) return 0;
                var sorted = new List<int>(recentScores);
                sorted.Sort();
                return sorted[sorted.Count / 2];
            }
        }

        /// <summary>Remember a finished tournament, dropping the oldest once there are enough.</summary>
        public void RecordScore(int score) {
            recentScores.Insert(0, score);
            while (recentScores.Count > RecentScoresKept) recentScores.RemoveAt(recentScores.Count - 1);
        }

        public static LocalServerState Load() => _holder.Value ?? new LocalServerState();

        public void Save() => _holder.Save(this);
    }

    /// <summary>One opponent: a real player's recorded 48 hours, under a generated name.</summary>
    [Serializable]
    public class LocalRival {

        public string name;

        /// <summary>What they end the window on — the number the group was built around.</summary>
        public int total;

        /// <summary>
        /// When each of their wins lands, in minutes from the start of the tournament, and what
        /// each was worth. Two arrays rather than one packed one: the blob packs because it holds
        /// 120,000 wins, and nineteen rivals hold about 760 — there is nothing to save and
        /// everything to read.
        ///
        /// COPIED out of the blob when the group is made, and already shifted to this
        /// tournament's clock. The blob is resampled daily; a running tournament must not notice.
        /// </summary>
        public int[] minutes = new int[0];
        public int[] points = new int[0];

        /// <summary>Their score once <paramref name="elapsed"/> minutes of the window have passed.</summary>
        public int ScoreAt(int elapsed) {
            int score = 0;
            for (int i = 0; i < minutes.Length; i++) {
                if (minutes[i] > elapsed) break;   // in time order
                score += points[i];
            }
            return score;
        }
    }

    /// <summary>A row of a finished table — all that is left of a rival once it is over.</summary>
    [Serializable]
    public class LocalEntry {
        public string name;
        public int score;
        public bool isMe;
    }
}
