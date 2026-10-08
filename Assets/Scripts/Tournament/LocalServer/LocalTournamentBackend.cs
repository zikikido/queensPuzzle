using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// The tournament server, running on the phone.
    ///
    /// It answers <see cref="ITournamentBackend.Sync"/> exactly as a real one would, and nothing
    /// outside this folder can tell the difference: the manager, the state, the syncer and the UI
    /// see a <see cref="TournamentSyncResult"/> and never learn that the other nineteen players
    /// are recordings. Swapping in a real server is one `new` in TournamentManager.Init.
    ///
    /// So it keeps its own database (<see cref="LocalServerState"/>), decides the schedule itself,
    /// and is the only thing here that reads <see cref="RivalsBlob"/>.
    ///
    /// What it deliberately does NOT do is react to the player. Every rival's whole 48 hours is
    /// decided before the tournament starts and then merely played back. A player can grind for
    /// ten hours or not open the app, and the table moves identically — which is the real reason
    /// it reads as other people. There is nothing to notice, because there is nothing responding.
    /// </summary>
    public sealed class LocalTournamentBackend : ITournamentBackend {

        /// <summary>
        /// A tournament lasts this long, counted from the player's FIRST WIN in it.
        ///
        /// Not from a shared grid. A real server fills a group out of the next players to win, so
        /// by the time you are in one the other nineteen are already there — the group is born
        /// full rather than filling up over two days. The matching rule is that your two days
        /// start when you join, and a player who wins in the 47th hour of somebody else's window
        /// is not handed a one-hour tournament.
        ///
        /// There is nothing to synchronise with anyway: every player's rivals are recordings of
        /// their own, so a global window would buy nothing and cost exactly that problem.
        /// </summary>
        const int WindowHours = 48;
        const int WindowMinutes = WindowHours * 60;

        /// <summary>A real answer takes a moment, so this one does too — otherwise the UI gets
        /// built against timings that will never happen again once there is a network in the way.</summary>
        const int MinLatencyMs = 100, MaxLatencyMs = 300;

        readonly string _userId;
        readonly System.Random _latency = new System.Random();
        LocalServerState _state;

        public LocalTournamentBackend(string userId) {
            _userId = userId;
        }

        public async Task<TournamentSyncResult> Sync(TournamentSyncRequest req) {
            await Task.Delay(_latency.Next(MinLatencyMs, MaxLatencyMs));

            // Server time is not a nicety here: without it a player could move the device clock
            // forward, run the tournament out and collect a prize. Refusing is also what a real
            // server looks like from the client's side — unreachable, try later.
            if (!MBServerTimeManagerV2.IsTimeSynced)
                throw new Exception("no trusted clock yet");

            DateTime now = MBServerTimeManagerV2.UTCNow;
            if (_state == null) _state = LocalServerState.Load();

            // Retire the one that is over and have the next one waiting, both before the wins are
            // counted — a win that arrives after the end belongs to the tournament now running,
            // not to the one it just missed.
            _closeIfOver(now);
            if (!_running) _open(now);

            var accepted = _applyWins(req);
            _state.Save();

            return new TournamentSyncResult {
                acceptedWinIds = accepted.ToArray(),
                current = _currentSnapshot(now),
                lastClosed = _closedSnapshot(),
            };
        }

        // ---- the schedule ----------------------------------------------------------------

        bool _running => !string.IsNullOrEmpty(_state.currentId);

        /// <summary>Freeze a tournament whose time is up. Nothing opens here — only a win does
        /// that.</summary>
        void _closeIfOver(DateTime now) {
            if (!_running || now.Ticks < _state.endTicks) return;
            _freezeClosed();

            _state.currentId = "";
            _state.myScore = 0;
            _state.acceptedIds.Clear();
            _state.rivals.Clear();
        }

        /// <summary>
        /// Start one, right now, with the group already in it.
        ///
        /// This runs on the first sync after the last one ended, which in practice is the moment
        /// the player opens the game — so there is never a time when there is no tournament to
        /// look at. They are simply not in it yet; the first win puts them in.
        ///
        /// Everything a tournament is — its window, its nineteen rivals, their whole two days —
        /// is decided here, once, and then only played back.
        /// </summary>
        void _open(DateTime now) {
            int usual = _state.UsualScore > 0 ? _state.UsualScore : RivalGroup.TypicalScore;

            _state.currentId = "t" + now.Ticks;
            _state.startTicks = now.Ticks;
            _state.endTicks = now.AddHours(WindowHours).Ticks;
            _state.myScore = 0;
            _state.acceptedIds.Clear();
            _state.rivals = RivalGroup.Build(usual, RivalsBlob.OwnerOf(_userId), ProfileManager.Name);

            Debug.Log($"[LocalTournament] {_state.currentId} opened with {_state.rivals.Count} rivals " +
                      $"(built around {usual} points)");
        }

        /// <summary>
        /// Keep the table of the tournament that just ended — names and final scores only.
        ///
        /// Frozen rather than recomputed because the player saw it a minute before it closed, and
        /// a results screen that disagrees with the last thing they looked at reads as a cheat
        /// even when the arithmetic is right.
        /// </summary>
        void _freezeClosed() {
            if (_state.myScore <= 0) return;   // never joined: nothing of theirs to show

            _state.closedId = _state.currentId;
            _state.closedStartTicks = _state.startTicks;
            _state.closedEndTicks = _state.endTicks;
            _state.closedMyScore = _state.myScore;
            _state.closedTable = _table(WindowMinutes, _state.myScore);
            _state.RecordScore(_state.myScore);   // what the next groups are sized against
        }

        // ---- wins ------------------------------------------------------------------------

        /// <summary>
        /// Apply what the client sent and acknowledge all of it. A win already applied is
        /// acknowledged again and added once — that is what lets the client resend everything it
        /// holds and never think about it.
        /// </summary>
        List<string> _applyWins(TournamentSyncRequest req) {
            var accepted = new List<string>();
            if (req?.wins == null) return accepted;

            foreach (var win in req.wins) {
                if (win == null || string.IsNullOrEmpty(win.id)) continue;
                accepted.Add(win.id);
                if (_state.acceptedIds.Contains(win.id)) continue;

                _state.acceptedIds.Add(win.id);
                _state.myScore += Mathf.Max(0, win.score);
            }
            return accepted;
        }

        // ---- the tables --------------------------------------------------------------------

        /// <summary>
        /// Everyone who has scored by <paramref name="minutes"/> into the window, in rank order.
        ///
        /// Rivals arrive within the first few minutes, not across two days — their recordings are
        /// shifted to the moment the group formed (see <see cref="RivalGroup"/>), the same moment
        /// the player's own first win landed. So the table is full almost immediately, which is
        /// what a group filled out of the next players to win looks like.
        /// </summary>
        List<LocalEntry> _table(int minutes, int myScore) {
            var rows = new List<LocalEntry>(_state.rivals.Count + 1);
            foreach (var rival in _state.rivals) {
                int score = rival.ScoreAt(minutes);
                if (score <= 0) continue;   // hasn't played yet — not in the table
                rows.Add(new LocalEntry { name = rival.name, score = score });
            }

            if (myScore > 0)
                rows.Add(new LocalEntry { name = ProfileManager.Name, score = myScore, isMe = true });

            // Ties break by name so the order is stable between syncs: a row that swaps places
            // with nothing happening is the kind of thing players notice and nobody can explain.
            rows.Sort((a, b) => b.score != a.score
                ? b.score.CompareTo(a.score)
                : string.CompareOrdinal(a.name, b.name));
            return rows;
        }

        TournamentSnapshot _currentSnapshot(DateTime now) {
            // No recordings baked means no opponents, and a group of one is not a tournament.
            // Reporting none is honest, and the client already knows what to show for it.
            if (_state.rivals.Count == 0) return _noTournament();

            var start = new DateTime(_state.startTicks, DateTimeKind.Utc);
            int minutes = Mathf.Clamp((int)(now - start).TotalMinutes, 0, WindowMinutes);
            var rows = _table(minutes, _state.myScore);

            return _snapshot(_state.currentId, _state.startTicks, _state.endTicks, rows, false);
        }

        TournamentSnapshot _closedSnapshot() {
            if (string.IsNullOrEmpty(_state.closedId)) return _noTournament();
            return _snapshot(_state.closedId, _state.closedStartTicks, _state.closedEndTicks,
                             _state.closedTable, true);
        }

        static TournamentSnapshot _noTournament() => new TournamentSnapshot { etag = "none" };

        TournamentSnapshot _snapshot(
            string id, long startTicks, long endTicks, List<LocalEntry> rows, bool isFinal) {

            var entries = new TournamentEntry[rows.Count];
            int myIndex = -1;
            for (int i = 0; i < rows.Count; i++) {
                entries[i] = new TournamentEntry { name = rows[i].name, score = rows[i].score };
                if (rows[i].isMe) myIndex = i;
            }

            return new TournamentSnapshot {
                etag = _etag(id, isFinal, rows),
                info = new TournamentInfo {
                    id = id,
                    startTicks = startTicks,
                    endTicks = endTicks,
                    groupSize = RivalGroup.Size + 1,
                    isFinal = isFinal,
                },
                standings = new TournamentStandings { myIndex = myIndex, entries = entries },
            };
        }

        /// <summary>
        /// A signature over exactly what the client would display, so an unchanged table costs
        /// nothing to re-send and a changed one always arrives. FNV-1a because it has to mean the
        /// same thing on every run — string.GetHashCode does not.
        /// </summary>
        static string _etag(string id, bool isFinal, List<LocalEntry> rows) {
            const uint FnvOffset = 2166136261, FnvPrime = 16777619;
            uint hash = FnvOffset;

            void Feed(string s) {
                for (int i = 0; i < s.Length; i++) { hash ^= s[i]; hash *= FnvPrime; }
                hash ^= '|'; hash *= FnvPrime;
            }

            Feed(id);
            Feed(isFinal ? "final" : "live");
            foreach (var row in rows) { Feed(row.name); Feed(row.score.ToString()); }
            return hash.ToString("x8");
        }
    }
}
