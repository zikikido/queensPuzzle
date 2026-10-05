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

        /// <summary>The server's signature for this exact content — always set, including for
        /// "there is no tournament", which has an etag of its own. The client hands it back on the
        /// next sync: the same etag means nothing changed and the payload is left out; a different
        /// one is the new truth, an empty payload included.</summary>
        public string etag = "";

        public TournamentInfo info = new TournamentInfo();
        public TournamentStandings standings = new TournamentStandings();

        /// <summary>There is a tournament here. False after a sync = there is none.</summary>
        public bool Exists => info != null && !string.IsNullOrEmpty(info.id);

        /// <summary>Which tournament this is, or "" for none — null-safe, since the payload comes
        /// off the wire.</summary>
        public string Id => info != null && info.id != null ? info.id : "";
    }

    /// <summary>Everything the client tells the server in one sync.</summary>
    [Serializable]
    public class TournamentSyncRequest {

        /// <summary>Every win the client still holds — the ones it has, every time. Ids the server
        /// already applied are ignored, so there is nothing to freeze and nothing to count twice.</summary>
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

        /// <summary>The ids of the wins the server now holds — both the ones it just applied and
        /// the ones it had already seen. The client drops exactly these and keeps the rest.</summary>
        public string[] acceptedWinIds = new string[0];

        public TournamentSnapshot current = new TournamentSnapshot();
        public TournamentSnapshot lastClosed = new TournamentSnapshot();
    }

    /// <summary>What one level win did to the player's standing — everything the climb popup needs,
    /// from a single call. <see cref="HasClimb"/> is false when there is nothing to animate (no
    /// tournament running, the timer already out, the feature off): the win is still queued and will
    /// count for the tournament it reaches, it just can't be shown on a table that is closing.
    /// Which popup to show is the UI's call, off <see cref="ETournamentStatus"/>.</summary>
    public struct TournamentWinResult {
        public bool joined;      // this win is what put the player in the tournament
        public int scoreAdded;
        public int newScore;
        public int fromIndex;    // row before (0-based; -1 = wasn't in the table)
        public int toIndex;      // row after

        public bool HasClimb => scoreAdded > 0;
    }

    /// <summary>One level win as reported to the server. <see cref="id"/> is what makes a resend
    /// harmless: the server applies a win once and ignores an id it has already seen, so the client
    /// can simply send everything it still holds, newest wins included.
    /// The time it happened is sent too — the server doesn't rank by it (a win counts for the
    /// tournament running when it arrives), it is there as data: bot pacing later, and analytics.</summary>
    [Serializable]
    public class TournamentWin {
        public string id;         // unique, made when the level was won
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

        public TournamentEntry Me =>
            entries != null && myIndex >= 0 && myIndex < entries.Length ? entries[myIndex] : null;
    }
}
