#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Editor-only capture: while armed, every played cell change (MBCell.MarkCell) lands in the
    /// record. MBCell calls <see cref="OnCellMarked"/> directly — no events.
    ///
    /// Two modes:
    ///  - Record: actions are stamped with a running clock. Tape-style overdub — existing keys
    ///    are consumed only as the head passes them (<see cref="ConsumeOverwritten"/>), so
    ///    stopping early keeps the tail of the old recording.
    ///  - Insert (✎ Edit): every action is stamped AT the playhead time; nothing is overwritten.
    /// </summary>
    public static class GPRecorder {

        enum EMode { Record, Insert }

        public static bool IsRecording { get; private set; }
        public static bool IsInserting => IsRecording && _mode == EMode.Insert;
        public static GPRecord Record { get; private set; }

        /// <summary>What a wrong queen costs while recording/replaying — the window keeps it in
        /// sync with the loaded record. Checked by MBGameplay.PlaceQueenAt.</summary>
        public static GPRecord.EFailMode FailMode = GPRecord.EFailMode.NoCost;

        /// <summary>True while a recorder session is driving the board.</summary>
        public static bool SessionActive => IsRecording || GPReplayer.IsReplaying;

        /// <summary>While recording/replaying, completing the board keeps the win's feel (win
        /// sound, haptic, celebration) but skips the popups, analytics and level++ — the session
        /// stays alive for scrubbing and re-records. Turn OFF to capture the real win ending.
        /// Checked by MBGameplay.PlaceQueenAt.</summary>
        public static bool NoWin = false;

        static EMode _mode;
        static float _t0;        // Record: Time.time that maps to record-time 0
        static float _insertAt;  // Insert: the stamp for every new action
        static float _purged;    // Record: clock up to which passed-over old keys were consumed
        static readonly HashSet<GPRecordAction> _fresh = new HashSet<GPRecordAction>();   // this session's adds

        /// <summary>Start overdub-recording into <paramref name="record"/>, the clock running
        /// from <paramref name="fromTime"/> (0 on a fresh record, the playhead on a re-record).</summary>
        public static void Begin(GPRecord record, float fromTime) {
            Record = record;
            _mode = EMode.Record;
            _t0 = Time.time - fromTime;
            _purged = fromTime;
            _fresh.Clear();
            IsRecording = true;
        }

        /// <summary>✎ Edit: every board action is inserted at <paramref name="atTime"/>.</summary>
        public static void BeginInsert(GPRecord record, float atTime) {
            Record = record;
            _mode = EMode.Insert;
            _insertAt = atTime;
            _fresh.Clear();
            IsRecording = true;
        }

        /// <summary>Follow the playhead while in Insert mode.</summary>
        public static void SetInsertTime(float atTime) => _insertAt = atTime;

        /// <summary>Current record-time (drives the window's live playhead).</summary>
        public static float Elapsed => !IsRecording ? 0f : _mode == EMode.Insert ? _insertAt : Time.time - _t0;

        public static GPRecord End() {
            IsRecording = false;
            _fresh.Clear();
            var r = Record;
            Record = null;
            return r;
        }

        /// <summary>Called by MBCell.MarkCell on every played state change.</summary>
        public static void OnCellMarked(MBCell cell, MBCell.ECellType from, MBCell.ECellType to) {
            if (!IsRecording || GPReplayer.IsReplaying) return;
            var a = new GPRecordAction { time = Elapsed, x = cell.X, y = cell.Y, to = to };
            Record.actions.Add(a);
            _fresh.Add(a);
            if (_mode == EMode.Insert) Record.Sort();   // lands mid-list — keep the order invariant
        }

        /// <summary>Overdub purge — old keys the record head has passed are consumed, this
        /// session's fresh ones stay. The window calls this every editor tick while recording.</summary>
        public static void ConsumeOverwritten() {
            if (!IsRecording || _mode != EMode.Record || Record == null) return;
            float now = Time.time - _t0;
            Record.actions.RemoveAll(a => a.time > _purged && a.time <= now && !_fresh.Contains(a));
            _purged = now;
        }
    }
}
#endif
