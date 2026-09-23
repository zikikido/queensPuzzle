using System;
using System.Collections.Generic;

namespace qp {

    /// <summary>A tournament as the server describes it — window and group size. The prizes are
    /// the client's business: the server only ranks players (see <see cref="TournamentConfig.prizePlaces"/>).
    /// Times are UTC ticks (JsonUtility can't serialize DateTime).</summary>
    [Serializable]
    public class TournamentInfo {
        public string id;
        public long startTicks;
        public long endTicks;
        public int groupSize;     // players per group, set by the server
        public bool isFinal;      // the server closed it — its table is the final ranking. Closing is
                                  // instant at end time, so there is no "results are being calculated"

        public DateTime StartUtc => new DateTime(startTicks, DateTimeKind.Utc);
        public DateTime EndUtc => new DateTime(endTicks, DateTimeKind.Utc);
    }

    /// <summary>A tournament plus the player's table in it — what the server hands back for both
    /// the running tournament and the last closed one, so the two can never disagree.</summary>
    [Serializable]
    public class TournamentSnapshot {

        public string etag = "";

        public TournamentInfo info = new TournamentInfo();
        public TournamentStandings standings = new TournamentStandings();

        /// <summary>There is content here. No content + an etag = "still the same as yours",
        /// so the client simply keeps what it already holds.</summary>
        public bool Exists => !string.IsNullOrEmpty(info.id);
    }

    /// <summary>Everything the client tells the server in one sync.</summary>
    [Serializable]
    public class TournamentSyncRequest {
        public string batchId = "";                    // empty = nothing to report
        public TournamentWin[] wins = new TournamentWin[0];

        // The etags of the snapshots the client already holds, exactly as the server sent them.
        // The server skips the payload of anything that hasn't changed since.
        public string currentEtag = "";
        public string closedEtag = "";
    }

    /// <summary>Everything the server answers in one sync: the tournament running now, and the last
    /// closed tournament the player took part in. Both always come back — whether the player has
    /// already been shown the closed one is the client's business, not the server's.</summary>
    [Serializable]
    public class TournamentSyncResult {

        public TournamentSnapshot current = new TournamentSnapshot();
        public TournamentSnapshot lastClosed = new TournamentSnapshot();
    }

    /// <summary>One level win as reported to the server. The time it happened is sent too — the
    /// server doesn't rank by it (a win counts for the tournament running when it arrives), it is
    /// there as data: pacing for the bots later, and analytics.</summary>
    [Serializable]
    public class TournamentWin {
        public int score;
        public long wonAtTicks;   // UTC
    }

    /// <summary>One cosmetic a player has equipped: which slot, and which item in it —
    /// e.g. ("avatar", "husky"), ("frame", "gold"). The server only carries the pair; what it
    /// looks like is the client's local catalog, so new skins need no server change.</summary>
    [Serializable]
    public class TournamentSkin {
        public string skinKey;       // slot: "avatar", "frame", …
        public string skinItemKey;   // the item inside that slot
    }

    /// <summary>One row of a group table, ordered by the server. No player ids — the client finds
    /// its own row by <see cref="TournamentStandings.myIndex"/>.</summary>
    [Serializable]
    public class TournamentEntry {
        public string name;
        public int score;
        public List<TournamentSkin> skins = new List<TournamentSkin>();

        /// <summary>The item equipped in a slot, or null — an unknown slot/item just falls back.</summary>
        public string Skin(string skinKey) => skins.Find(s => s.skinKey == skinKey)?.skinItemKey;
    }

    /// <summary>A group table exactly as the server sent it: rows in rank order, plus where the
    /// player sits in them. Which tournament it belongs to, and whether it is final, live on the
    /// <see cref="TournamentInfo"/> it arrives with (see <see cref="TournamentSnapshot"/>).</summary>
    [Serializable]
    public class TournamentStandings {
        public int myIndex = -1;   // the player's row in entries; -1 = not in this table. Rank shown = myIndex + 1
        public TournamentEntry[] entries = new TournamentEntry[0];

        public bool IsMe(int index) => index == myIndex;

        public TournamentEntry Me => myIndex >= 0 && myIndex < entries.Length ? entries[myIndex] : null;
    }
}
