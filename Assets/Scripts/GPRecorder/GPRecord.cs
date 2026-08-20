#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QueensPuzzle;
using UnityEngine;

namespace qp {

    /// <summary>One recorded board action: at <see cref="time"/> seconds cell (x, y) changed to <see cref="to"/>.</summary>
    [Serializable]
    public class GPRecordAction {
        public float time;
        public int x, y;
        public MBCell.ECellType to;
    }

    /// <summary>One hand-track key: at <see cref="time"/> the ad hand does <see cref="kind"/> at cell (x, y).
    /// START_MOVE presses down, END_MOVE has glided there from the START cell and lifts,
    /// DOUBLE_CLICK plays the tap-tap that seats a queen. Visual only — never drives the board.</summary>
    [Serializable]
    public class GPHandKey {
        public enum EKind { START_MOVE, END_MOVE, DOUBLE_CLICK }
        public float time;
        public int x, y;
        public EKind kind;
    }

    /// <summary>
    /// A recorded play session for UA ad capture: the level it was played on (embedded, so the
    /// replay survives level-pack changes) plus the timed cell changes. Saved as JSON in
    /// GPRecords/ next to the Assets folder (outside it, so Unity doesn't import the files).
    /// </summary>
    [Serializable]
    public class GPRecord {

        // LevelPack.Level isn't [Serializable] (runtime type decoded from the binary pack) —
        // this mirror of its fields is what actually lands in the JSON.
        [Serializable]
        public class LevelSnapshot {
            public int size, weight;
            public int[] regions, solutionColumns, revealedRows, regionColors;

            public static LevelSnapshot From(LevelPack.Level l) => new LevelSnapshot {
                size = l.size, weight = l.weight, regions = l.regions,
                solutionColumns = l.solutionColumns, revealedRows = l.revealedRows,
                regionColors = l.regionColors
            };

            public LevelPack.Level ToLevel() => new LevelPack.Level {
                size = size, weight = weight, regions = regions, solutionColumns = solutionColumns,
                // JSON round-trips null arrays as empty — Level expects null for "none"
                revealedRows = revealedRows != null && revealedRows.Length > 0 ? revealedRows : null,
                regionColors = regionColors != null && regionColors.Length > 0 ? regionColors : null
            };
        }

        public LevelSnapshot level;
        public List<GPRecordAction> actions = new List<GPRecordAction>();
        public List<GPHandKey> handKeys = new List<GPHandKey>();

        /// <summary>True once a level was captured — a default-constructed (or Unity-deserialized
        /// empty) instance has size 0 and means "no record loaded".</summary>
        public bool IsValid => level != null && level.size > 0;

        public float Duration {
            get {
                float d = actions.Count > 0 ? actions[actions.Count - 1].time : 0f;
                if (handKeys.Count > 0) d = Mathf.Max(d, handKeys[handKeys.Count - 1].time);
                return d;
            }
        }

        /// <summary>Keep both lists time-ordered — the replayer and the timeline both assume it.
        /// Stable (List.Sort is not): equal-time actions (✎ Edit inserts) keep their order.</summary>
        public void Sort() {
            var ordered = actions.OrderBy(a => a.time).ToList();
            actions.Clear();
            actions.AddRange(ordered);
            var hand = handKeys.OrderBy(k => k.time).ToList();
            handKeys.Clear();
            handKeys.AddRange(hand);
        }

        // ---- files: GPRecords/ beside Assets ------------------------------------------

        public static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "GPRecords"));

        public static string[] ListPaths() =>
            Directory.Exists(Dir) ? Directory.GetFiles(Dir, "*.json") : Array.Empty<string>();

        public void Save(string path) {
            Directory.CreateDirectory(Dir);
            Sort();
            File.WriteAllText(path, JsonUtility.ToJson(this, true));
        }

        public static GPRecord Load(string path) =>
            JsonUtility.FromJson<GPRecord>(File.ReadAllText(path));
    }
}
#endif
