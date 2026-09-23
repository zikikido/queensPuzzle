using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Local stand-in for the tournament server, so the whole feature can be built and tested
    /// before a real server exists. Behaves like the server should:
    ///   - fixed 48h windows since <see cref="Epoch"/> ("T{index}"), a new one right after the previous
    ///   - one group per tournament: the player + (GroupSize-1) bots; a bot shows up only once it has score
    ///   - bots gain score over time at their own seeded pace, frozen at end time
    ///   - wins count for the tournament running when the package arrives; a repeated batchId changes nothing
    /// Its "server clock" is <see cref="TournamentManager.Now"/>, so the debug time jump moves both.
    /// The player's server-side record is saved in PlayerPrefs, like a real server would keep it.
    /// Debug knobs (MBDebugWin): <see cref="DebugDelayMs"/>, <see cref="DebugFail"/>.
    /// </summary>
    public class MockTournamentBackend : ITournamentBackend {

        readonly string _playerId;   // who is asking — like the header/token a real backend would send

        public MockTournamentBackend(string playerId) => _playerId = playerId;

        // ---- server config (would come from the real server's settings) ----
        public const int GroupSize = 20;
        static readonly TimeSpan Duration = TimeSpan.FromHours(48);
        static readonly DateTime Epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        static long _seq;   // answer counter, like a real server's

        // ---- debug knobs ----
        public static int DebugDelayMs;       // every call waits this long (simulates a slow server)
        public static bool DebugFail;         // every call throws (simulates server down)

        static readonly string[] BotNames = {
            "Max", "Bella", "Milo", "Coco", "Luna", "Rocky", "Daisy", "Charlie", "Lola", "Buddy",
            "Rosie", "Teddy", "Bailey", "Ziggy", "Nala", "Oreo", "Pepper", "Biscuit", "Maple", "Scout",
            "LuckyPaw", "BoneHunter", "WoofWoof", "Sparky", "Hazel", "Ollie", "Pumpkin", "Mochi",
        };
        // Skin keys are opaque to a server — these are just what the client catalog will know.
        static readonly string[] BotAvatars = { "dog1", "dog2", "dog3", "dog4", "dog5", "dog6", "dog7", "dog8" };
        static readonly string[] BotFrames = { "basic", "silver", "gold" };

        // ---- the player's record on the "server" ----

        [Serializable]
        class Record {
            public string tournamentId;
            public int score;
            public List<string> batchIds = new List<string>();   // packages already applied
        }

        [Serializable]
        class Store { public List<Record> records = new List<Record>(); }

        static readonly PlayerPrefsHelper.ObjectHolder<Store> _holder
            = new PlayerPrefsHelper.ObjectHolder<Store>("qp_tournament_mock");

        static Store Data => _holder.Value ?? new Store();

        /// <summary>Debug only — forget everything the "server" knows about the player.</summary>
        public static void DebugReset() => _holder.Delete();

        // ---- ITournamentBackend ----

        public async Task<TournamentSyncResult> Sync(TournamentSyncRequest req) {
            await _simulateNetwork();

            var info = _infoAt(TournamentManager.Now);   // the tournament running right now
            var data = Data;
            var rec = _record(data, info.id);

            if (!string.IsNullOrEmpty(req.batchId) && !rec.batchIds.Contains(req.batchId)) {
                rec.batchIds.Add(req.batchId);           // a repeat of the same package changes nothing
                foreach (var w in req.wins) rec.score += w.score;   // wonAtTicks: data only, for later (bot pacing)
                _holder.Save(data);
            }

            var res = new TournamentSyncResult {
                current = _snapshot(info, rec, req.currentEtag),
            };

            var closed = _lastClosedPlayed(data, info);   // always sent; the client tracks what it showed
            if (closed != null)
                res.lastClosed = _snapshot(closed, _record(data, closed.id), req.closedEtag);
            return res;
        }

        /// <summary>A snapshot + its etag — or, when the client already has this exact content,
        /// just the etag, so the table doesn't travel again.</summary>
        TournamentSnapshot _snapshot(TournamentInfo info, Record rec, string clientEtag) {
            var st = _standings(info, rec);          // sets info.isFinal too
            var etag = _etag(info, st);
            return etag == clientEtag
                ? new TournamentSnapshot { etag = etag }
                : new TournamentSnapshot { etag = etag, info = info, standings = st };
        }

        /// <summary>Signature of what the client would see: the tournament, and every row in it.</summary>
        static string _etag(TournamentInfo info, TournamentStandings st) {
            unchecked {
                int h = 17;                       // no string.GetHashCode: it isn't stable across runs,
                h = _hash(h, info.id);            // and the client keeps etags between sessions
                h = h * 31 + (info.isFinal ? 1 : 0);
                h = h * 31 + st.myIndex;
                foreach (var e in st.entries) {
                    h = _hash(h, e.name);
                    h = h * 31 + e.score;
                }
                return h.ToString("x8");
            }
        }

        static int _hash(int h, string s) {
            unchecked {
                foreach (char c in s) h = h * 31 + c;
                return h;
            }
        }

        /// <summary>The most recent tournament that is over and that the player actually played.</summary>
        static TournamentInfo _lastClosedPlayed(Store data, TournamentInfo current) {
            TournamentInfo best = null;
            foreach (var r in data.records) {
                if (r.tournamentId == current.id || r.batchIds.Count == 0) continue;
                var info = _infoById(r.tournamentId);
                if (TournamentManager.Now < info.EndUtc) continue;              // not closed yet
                if (best == null || info.endTicks > best.endTicks) best = info;
            }
            return best;
        }

        // ---- internals ----

        static async Task _simulateNetwork() {
            if (DebugDelayMs > 0) await Task.Delay(DebugDelayMs);
            if (DebugFail) throw new Exception("[MockTournament] server down (debug)");
        }

        static TournamentInfo _infoAt(DateTime utc) =>
            _infoByIndex((long)Math.Floor((utc - Epoch).TotalHours / Duration.TotalHours));

        static TournamentInfo _infoById(string id) => _infoByIndex(long.Parse(id.Substring(1)));

        static TournamentInfo _infoByIndex(long index) {
            var start = Epoch + TimeSpan.FromTicks(Duration.Ticks * index);
            return new TournamentInfo {
                id = "T" + index,
                startTicks = start.Ticks,
                endTicks = (start + Duration).Ticks,
                groupSize = GroupSize,
            };
        }

        static Record _record(Store data, string tournamentId) {
            var rec = data.records.Find(r => r.tournamentId == tournamentId);
            if (rec != null) return rec;
            rec = new Record { tournamentId = tournamentId };
            data.records.Add(rec);
            if (data.records.Count > 4) data.records.RemoveAt(0);   // keep only recent tournaments
            return rec;
        }

        /// <summary>The player's group: bots that already have score + the player once joined.
        /// Sorted by score (desc), ties by id so the order is stable.</summary>
        TournamentStandings _standings(TournamentInfo info, Record rec) {
            var now = TournamentManager.Now;
            var at = now < info.EndUtc ? now : info.EndUtc;   // frozen at end time
            info.isFinal = now >= info.EndUtc;   // closed → the table below is the final ranking
            var st = new TournamentStandings();

            // (row, player id) — the id stays on the server side: it only breaks ties, so the
            // order never jumps, and it is never sent to the client.
            var rows = new List<(TournamentEntry row, string id)>();

            int seed = (int)(long.Parse(info.id.Substring(1)) % 100000);   // stable across runs (string hash isn't)
            double elapsedH = (at - info.StartUtc).TotalHours;
            for (int i = 1; i < info.groupSize; i++) {
                var rnd = new System.Random(seed * 31 + i);
                double joinAfterH = rnd.NextDouble() * 30;          // joins somewhere in the first 30h
                double scorePerH = 0.3 + rnd.NextDouble() * 2.7;    // 0.3..3 score per hour
                double activeH = elapsedH - joinAfterH;
                int score = activeH > 0 ? (int)Math.Floor(activeH * scorePerH) : 0;
                if (score <= 0) continue;
                rows.Add((new TournamentEntry {
                    name = BotNames[rnd.Next(BotNames.Length)],
                    score = score,
                    skins = _skins(BotAvatars[rnd.Next(BotAvatars.Length)], BotFrames[rnd.Next(BotFrames.Length)]),
                }, $"bot{i}"));
            }

            if (rec.batchIds.Count > 0)
                rows.Add((new TournamentEntry { name = "You", score = rec.score, skins = _skins("dog1", "basic") },
                          _playerId));

            rows.Sort((a, b) => a.row.score != b.row.score ? b.row.score.CompareTo(a.row.score)
                                                           : string.CompareOrdinal(a.id, b.id));
            st.entries = new TournamentEntry[rows.Count];
            for (int i = 0; i < rows.Count; i++) {
                st.entries[i] = rows[i].row;
                if (rows[i].id == _playerId) st.myIndex = i;
            }
            return st;
        }

        static List<TournamentSkin> _skins(string avatar, string frame) => new List<TournamentSkin> {
            new TournamentSkin { skinKey = "avatar", skinItemKey = avatar },
            new TournamentSkin { skinKey = "frame", skinItemKey = frame },
        };
    }
}
