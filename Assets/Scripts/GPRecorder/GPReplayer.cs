#if UNITY_EDITOR
using Common;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
            // hand and spotlight show whenever their tracks have keys; both follow PlayheadTime
            if (record.handKeys.Count > 0) GPHand.Ensure(record);
            else GPHand.Remove();
            if (record.spotKeys.Count > 0) GPSpotlight.Ensure(record);
            else GPSpotlight.Remove();
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

    /// <summary>
    /// Tutorial-style spotlight driven by the record's spot track — implemented WITH the real
    /// tutorial: MBToturial.ShowCells lights the cells (curtain + popping holes, correct sorting,
    /// everything the tutorial already does) and Hide() drops it. The only twist: AutoHide is
    /// parked off while our spotlight is up — a targetless "step" counts as done, so the
    /// tutorial's Update would close it instantly — and restored on clear/remove.
    /// </summary>
    public class GPSpotlight : MonoBehaviour {

        public static GPSpotlight Instance { get; private set; }

        public static void Ensure(GPRecord record) {
            if (Instance == null) Instance = new GameObject("$GPSpotlight").AddComponent<GPSpotlight>();
            Instance._record = record;
            Instance._applied = null;   // force a re-apply with the fresh keys
        }

        public static void Remove() {
            if (Instance != null) Destroy(Instance.gameObject);   // OnDestroy clears the spotlight
            Instance = null;
        }

        /// <summary>Force a re-apply on the next frame (🎯 Pick edited a key's cell list).</summary>
        public static void Refresh() {
            if (Instance != null) Instance._applied = null;
        }

        GPRecord _record;
        string _applied;   // signature of the hole set currently shown — re-apply only on change

        void OnDestroy() {
            if (Instance == this) Instance = null;
            Clear();
        }

        static void Clear() {
            var tut = MBToturial.instance;
            if (tut == null) return;
            tut.AutoHide = true;
            tut.SetApplyVisible(true);            // flags only — nothing may turn visible here: the
            tut.SetHandVisible(true);             // tutorial stays awake through the curtain fade-out,
                                                  // so a restored text box would flash on screen.
                                                  // ShowHint re-activates $TextContainer itself.
            if (tut.gameObject.activeSelf) tut.Hide();
        }

        // Our spotlight is curtain + holes ONLY — no hint message bubble, no Apply button
        // (same trimming the first-level tutorial does for its hand-guided steps).
        static void SetTextContainerVisible(MBToturial tut, bool on) {
            var tc = tut.transform.RecursiveFindChild("$TextContainer");
            if (tc != null) tc.gameObject.SetActive(on);
        }

        void Update() {
            var gp = MBGameplay.instance;
            if (_record == null || gp == null || !gp.Ready) return;

            // the hole set at the playhead: every SHOW since the last CLEAR
            float t = GPReplayer.PlayheadTime;
            var indices = new List<int>();
            var sig = new StringBuilder();
            foreach (var k in _record.spotKeys) {   // time-sorted
                if (k.time > t) break;
                if (k.kind == GPSpotKey.EKind.CLEAR) { indices.Clear(); sig.Clear(); continue; }
                for (int i = 0; i < k.Count; i++) {
                    int idx = k.ys[i] * gp.N + k.xs[i];
                    if (indices.Contains(idx)) continue;
                    indices.Add(idx);
                    sig.Append(idx).Append(';');
                }
            }

            string s = sig.ToString();
            if (s == _applied) return;   // unchanged — never re-pop the open holes
            _applied = s;

            var tut = MBToturial.instance;
            if (tut == null) return;
            if (indices.Count == 0) Clear();
            else {
                tut.AutoHide = false;   // our targetless spotlight must not close itself
                tut.ShowCells(indices);
                tut.SetApplyVisible(false);
                tut.SetHandVisible(false);   // the scene leaves $Hand active-self — waking the tutorial would show it
                SetTextContainerVisible(tut, false);
            }
        }
    }

    /// <summary>
    /// 🎯 Pick mode: while active, clicks on the live board toggle cells in a SHOW spot key's
    /// list WITHOUT touching the game — board input is locked via MBGameplay.InputLocks and the
    /// clicks are read here, mapped to cells, and previewed live through the spotlight.
    /// </summary>
    public class GPSpotPicker : MonoBehaviour {

        public static GPSpotPicker Instance { get; private set; }
        public static bool Active => Instance != null;

        public static void Begin(GPSpotKey key) {
            if (Instance == null) {
                Instance = new GameObject("$GPSpotPicker").AddComponent<GPSpotPicker>();
                MBGameplay.instance.InputLocks++;   // clicks must not paint the board
            }
            Instance._key = key;
        }

        public static void End() {
            if (Instance == null) return;
            if (MBGameplay.instance != null) MBGameplay.instance.InputLocks--;
            Destroy(Instance.gameObject);
            Instance = null;
        }

        GPSpotKey _key;

        void Update() {
            if (_key == null || !Input.GetMouseButtonDown(0)) return;
            var gp = MBGameplay.instance;
            var cam = Camera.main;
            if (gp == null || !gp.Ready || cam == null) return;

            Vector3 wp = cam.ScreenToWorldPoint(Input.mousePosition);
            for (int r = 0; r < gp.N; r++)
                for (int c = 0; c < gp.N; c++) {
                    var cell = gp.CellAt(r, c);
                    if (cell == null) continue;
                    float half = cell.GetSize().x * cell.transform.lossyScale.x * 0.5f;
                    var d = cell.transform.position - wp;
                    if (Mathf.Abs(d.x) > half || Mathf.Abs(d.y) > half) continue;
                    _key.ToggleCell(c, r);
                    GPSpotlight.Refresh();   // the curtain holes preview the new list next frame
                    return;
                }
        }
    }
}
#endif
