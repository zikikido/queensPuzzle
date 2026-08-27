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

    /// <summary>One hand-track key: at <see cref="time"/> the ad hand is at cell (x, y).
    /// A drag is a run of POINTs closed by an END: the first POINT is where the finger appears
    /// and presses down, every next POINT a corner it drags through (finger stays down — e.g.
    /// sweeping the rows AND columns a queen blocks), END where it lifts and vanishes.
    /// DOUBLE_CLICK: the hand appears at the cell and plays the tap-tap that seats a queen.
    /// The hand exists only inside a gesture — never before its first key. Visual only.</summary>
    [Serializable]
    public class GPHandKey {
        public enum EKind { POINT, END_MOVE, DOUBLE_CLICK }
        public float time;
        public int x, y;
        public EKind kind;
    }

    /// <summary>One spotlight-track key: SHOW pops tutorial-style holes over its CELL LIST in the
    /// black curtain (raising the curtain if it's down); CLEAR drops the curtain and closes every
    /// hole. Holes accumulate across SHOW keys until the next CLEAR. Visual only.</summary>
    [Serializable]
    public class GPSpotKey {
        public enum EKind { SHOW, CLEAR }
        public float time;
        public int x, y;        // legacy single cell (records saved before multi-cell) — migrated on Load
        public int[] xs, ys;    // the cells this SHOW lights, parallel arrays (edited with 🎯 Pick)
        public EKind kind;

        public int Count => xs != null ? xs.Length : 0;

        /// <summary>Toggle a cell in/out of the list (🎯 Pick mode clicks).</summary>
        public void ToggleCell(int cx, int cy) {
            var lx = new List<int>(xs ?? Array.Empty<int>());
            var ly = new List<int>(ys ?? Array.Empty<int>());
            for (int i = 0; i < lx.Count; i++) {
                if (lx[i] != cx || ly[i] != cy) continue;
                lx.RemoveAt(i); ly.RemoveAt(i);
                xs = lx.ToArray(); ys = ly.ToArray();
                return;
            }
            lx.Add(cx); ly.Add(cy);
            xs = lx.ToArray(); ys = ly.ToArray();
        }
    }

    /// <summary>One voice-track key: at <see cref="time"/>, play the line called <see cref="name"/>
    /// from the record's active voices file. The content (text, params, wav path) lives in
    /// voicesN.json in the record's folder — the key holds timing only.</summary>
    [Serializable]
    public class GPVoiceKey {
        public float time;
        public string name;
    }

    /// <summary>One voiceover line: text + generation params + where its wav lives
    /// (<see cref="path"/> is relative to the record's folder, e.g. "Voices/A/oops.wav").</summary>
    [Serializable]
    public class GPVoiceLine {
        public string name;
        public string text;
        public float speed = 1f;
        public float stability = 0.45f;
        public float style = 0.3f;
        public string path;
    }

    /// <summary>A voice set (voices1.json, voices2.json, … in the record's folder). Swapping the
    /// record's voicesFile swaps every line's audio while the timeline keys stay put.</summary>
    [Serializable]
    public class GPVoices {
        public string voiceId;   // the ElevenLabs voice this set is generated with
        public List<GPVoiceLine> lines = new List<GPVoiceLine>();

        public GPVoiceLine Find(string name) {
            foreach (var l in lines) if (l.name == name) return l;
            return null;
        }

        public static GPVoices Load(string path) =>
            File.Exists(path) ? JsonUtility.FromJson<GPVoices>(File.ReadAllText(path)) : new GPVoices();

        public void Save(string path) => File.WriteAllText(path, JsonUtility.ToJson(this, true));
    }

    /// <summary>
    /// A recorded play session for UA ad capture: the level it was played on (embedded, so the
    /// replay survives level-pack changes) plus the timed cell changes. New layout: each record
    /// is a FOLDER — GPRecords/&lt;name&gt;/record.json + voicesN.json + Voices/*.wav (legacy flat
    /// GPRecords/*.json files still load). GPRecords sits next to Assets so Unity ignores it.
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
        public List<GPSpotKey> spotKeys = new List<GPSpotKey>();
        public List<GPVoiceKey> voiceKeys = new List<GPVoiceKey>();
        public string voicesFile = "voices1.json";   // the active voice set in the record's folder

        /// <summary>True once a level was captured — a default-constructed (or Unity-deserialized
        /// empty) instance has size 0 and means "no record loaded".</summary>
        public bool IsValid => level != null && level.size > 0;

        public float Duration {
            get {
                float d = actions.Count > 0 ? actions[actions.Count - 1].time : 0f;
                if (handKeys.Count > 0) d = Mathf.Max(d, handKeys[handKeys.Count - 1].time);
                if (spotKeys.Count > 0) d = Mathf.Max(d, spotKeys[spotKeys.Count - 1].time);
                if (voiceKeys.Count > 0) d = Mathf.Max(d, voiceKeys[voiceKeys.Count - 1].time);
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
            var spot = spotKeys.OrderBy(k => k.time).ToList();
            spotKeys.Clear();
            spotKeys.AddRange(spot);
            var voice = voiceKeys.OrderBy(k => k.time).ToList();
            voiceKeys.Clear();
            voiceKeys.AddRange(voice);
        }

        // ---- files: GPRecords/ beside Assets ------------------------------------------

        public static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "GPRecords"));

        /// <summary>The folder a record's voices and wavs live in (its own folder in the new
        /// layout; GPRecords itself for a legacy flat file).</summary>
        public static string FolderOf(string recordPath) => Path.GetDirectoryName(recordPath);

        public static string[] ListPaths() {
            if (!Directory.Exists(Dir)) return Array.Empty<string>();
            var result = new List<string>();
            foreach (var d in Directory.GetDirectories(Dir)) {
                string rp = Path.Combine(d, "record.json");
                if (File.Exists(rp)) result.Add(rp);
            }
            result.AddRange(Directory.GetFiles(Dir, "*.json"));   // legacy flat records
            return result.ToArray();
        }

        /// <summary>Display name: the folder name in the new layout, the file name for legacy.</summary>
        public static string NameOf(string recordPath) =>
            Path.GetFileName(recordPath) == "record.json"
                ? Path.GetFileName(FolderOf(recordPath))
                : Path.GetFileNameWithoutExtension(recordPath);

        /// <summary>Every voice key must resolve to a line in the active voices file, and every
        /// referenced wav must exist on disk. Returns the problems; empty = all good.</summary>
        public List<string> ValidateVoices(string recordPath, GPVoices voices) {
            var problems = new List<string>();
            if (voiceKeys.Count == 0) return problems;
            string folder = FolderOf(recordPath);
            foreach (var k in voiceKeys) {
                var line = voices?.Find(k.name);
                if (line == null) { problems.Add($"voice key '{k.name}' has no line in {voicesFile}"); continue; }
                if (string.IsNullOrEmpty(line.path) || !File.Exists(Path.Combine(folder, line.path)))
                    problems.Add($"'{k.name}': wav missing ({(string.IsNullOrEmpty(line.path) ? "no path" : line.path)})");
            }
            return problems;
        }

        public void Save(string path) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));   // records are folders now
            Sort();
            File.WriteAllText(path, JsonUtility.ToJson(this, true));
        }

        public static GPRecord Load(string path) {
            var r = JsonUtility.FromJson<GPRecord>(File.ReadAllText(path));
            // records saved before multi-cell spots carry a single x,y — lift it into the list
            if (r?.spotKeys != null)
                foreach (var k in r.spotKeys)
                    if (k.kind == GPSpotKey.EKind.SHOW && k.Count == 0) { k.xs = new[] { k.x }; k.ys = new[] { k.y }; }
            return r;
        }
    }
}
#endif
