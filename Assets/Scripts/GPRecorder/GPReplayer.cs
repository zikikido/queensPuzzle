#if UNITY_EDITOR
using System.Collections;
using System.IO;
using QueensPuzzle;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Editor-only replay: fires the recorded actions at their times through MBGameplay's replay
    /// entries, so the board behaves exactly as if a player made the moves (anims, sounds, win).
    /// A replay run is started by the GPRecorderWindow: it saves the record, puts its path into
    /// SessionState and (re)enters play mode — LevelLoader then serves the embedded level and
    /// MBGameplay skips the board restore.
    /// </summary>
    public static class GPReplayer {

        // SessionState keys (same handover pattern as LevelLoader.PlayLevelGuidKey).
        public const string ReplayRecordKey = "qp.gpRecordPath";   // JSON path of the record to replay
        public const string FreshBoardKey = "qp.gpFreshBoard";     // fresh-record run: no board restore

        public static bool IsReplaying { get; private set; }
        public static float PlayheadTime { get; private set; }   // current replay clock, for the window

        static Coroutine _run;

        public static bool ReplayPending =>
            !string.IsNullOrEmpty(UnityEditor.SessionState.GetString(ReplayRecordKey, ""));

        /// <summary>True when the next board build must NOT restore a saved attempt — replays and
        /// fresh ad recordings both need the level to start empty. Checked by MBGameplay.</summary>
        public static bool WantsFreshBoard =>
            ReplayPending || UnityEditor.SessionState.GetBool(FreshBoardKey, false);

        /// <summary>The level embedded in the pending replay's record, or null when no replay is
        /// pending. Called by LevelLoader.LoadLevel — the record's level wins in-editor.</summary>
        public static LevelPack.Level LoadPendingLevel() {
            string path = UnityEditor.SessionState.GetString(ReplayRecordKey, "");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try { return GPRecord.Load(path)?.level?.ToLevel(); }
            catch (System.Exception e) {
                Common.CDebug.LogError("[GPReplayer] record unreadable: " + e.Message);
                return null;
            }
        }

        /// <summary>True when the record can drive the CURRENT play session directly — play mode
        /// up, board ready, and the same puzzle on the board. Then Seek / Play / a re-record all
        /// work in place, with no play-mode restart.</summary>
        public static bool CanDrive(GPRecord record) =>
            Application.isPlaying && MBGameplay.instance != null && MBGameplay.instance.Ready
            && record != null && record.IsValid && MBGameplay.instance.Level != null
            && MBGameplay.instance.Level.ContentHash() == record.level.ToLevel().ContentHash();

        /// <summary>Video-style scrub: show the board exactly as it was at time <paramref name="t"/> —
        /// empty board + revealed queens, with every action ≤ t baked in. Instant, no anims.</summary>
        public static void Seek(GPRecord record, float t) {
            var gp = MBGameplay.instance;
            if (gp == null || !gp.Ready) return;
            // the hand shows whenever the record has hand keys; it follows PlayheadTime on its own
            if (record.handKeys.Count > 0) GPHand.Ensure(record);
            else GPHand.Remove();
            int n = record.level.size;
            var states = new MBCell.ECellType[n * n];
            if (record.level.revealedRows != null)
                foreach (int row in record.level.revealedRows)
                    states[row * n + record.level.solutionColumns[row]] = MBCell.ECellType.QUEEN;
            foreach (var a in record.actions) {
                if (a.time > t) break;   // the list is time-sorted
                states[a.y * n + a.x] = a.to;
            }
            PlayheadTime = t;
            gp.ReplaySetBoard(states);
        }

        /// <summary>Play the record from <paramref name="fromTime"/> to its end: the board jumps
        /// to that point instantly (Seek), then the remaining actions fire at their times.</summary>
        public static void Play(GPRecord record, float fromTime) {
            Stop();
            _run = MBGameplay.instance.StartCoroutine(Run(record, fromTime));
        }

        public static void Stop() {
            if (_run != null && MBGameplay.instance != null) MBGameplay.instance.StopCoroutine(_run);
            _run = null;
            if (IsReplaying && MBGameplay.instance != null) MBGameplay.instance.InputLocks--;
            IsReplaying = false;
        }

        static IEnumerator Run(GPRecord record, float fromTime) {
            var gp = MBGameplay.instance;
            IsReplaying = true;
            gp.InputLocks++;   // the replay drives the board — block real touches
            while (!gp.Ready) yield return null;   // wait out the bloom reveal

            Seek(record, fromTime);
            float t0 = Time.time - fromTime;
            foreach (var a in record.actions) {
                if (a.time <= fromTime) continue;   // baked in by the seek
                while (Time.time - t0 < a.time) { PlayheadTime = Time.time - t0; yield return null; }
                PlayheadTime = a.time;
                // one bad action (an edited record can hold anything) must not kill the run —
                // an uncaught throw here would also leak the InputLocks++ above
                try { Apply(gp, a); }
                catch (System.Exception e) { Common.CDebug.LogError($"[GPReplayer] action at {a.time:0.00}s ({a.to} on {a.x},{a.y}) failed: {e.Message}"); }
            }

            IsReplaying = false;
            gp.InputLocks--;
            _run = null;
        }

        static void Apply(MBGameplay gp, GPRecordAction a) {
            switch (a.to) {
                case MBCell.ECellType.X:
                case MBCell.ECellType.EMPTY:
                    gp.ReplayMark(a.x, a.y, a.to);
                    break;
                default:   // QUEEN / WRONG_QUEEN — the outcome is re-derived by the normal logic
                    gp.ReplayQueenTap(a.x, a.y);
                    break;
            }
        }
    }
}
#endif
