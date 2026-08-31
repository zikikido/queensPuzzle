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

    /// <summary>One image overlay shown for the whole video (logo, claim, end-card badge…).
    /// Only the images the user picked are stored — each with its own height/position.</summary>
    [Serializable]
    public class GPAdImage {
        public string name;            // sprite asset name, from the AdsImagePortrait folder
        public float height = 0.12f;   // fraction of screen height
        public float pos = 0.85f;      // 0 = bottom … 1 = top
        public Color bg = new Color(0f, 0f, 0f, 0f);   // the strip behind the image (clear = none)
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
        public float endTime;   // replay runs at least to here (END marker on the ruler) — 0 = last key

        /// <summary>What a wrong queen costs during a recorder session.</summary>
        public enum EFailMode {
            NoCost,      // no bone lost, never fails — unlimited wrong tries (default)
            NoPopup,     // bones drop and the puppies cry, but the fail popup never shows
            Normal       // the real game: bones drop, running out fails the board
        }

        // per-record session flags (all applied by the window while the session runs)
        public EFailMode failMode = EFailMode.NoCost;
        public bool noWin = true;        // finishing plays the win sound but no popups — session stays alive
        public bool hideTop;             // back arrow + level title + settings gear
        public bool hideRules;           // the three rule cards
        public bool hideBoosters;        // the bottom booster buttons
        public bool hideCounters;        // the puppy count + bones row

        // the AdsVoiceTextPortrait overlay scene: subtitles typed letter-by-letter with each
        // voice line. Height and position are screen fractions (pos 0 = bottom, 1 = top).
        public bool showAdText;
        public Color adTextBg = new Color(0f, 0f, 0f, 0.85f);
        public Color adTextColor = Color.white;
        public float adTextHeight = 0.12f;
        public float adTextPos = 0.08f;

        /// <summary>How the end card enters.</summary>
        public enum EEndCardAnim { Fade, ScaleUp, SlideUp, SlideDown }

        // the AdsEndCardPortrait scene — its own key on the timeline; the replay always runs on
        // past it (see EndCardHold), so the card is never cut off
        public bool showEndCard;
        public Color endCardBg = Color.white;
        public float endCardTime;                // when the card enters (its marker on the ruler)
        public EEndCardAnim endCardAnim = EEndCardAnim.Fade;
        public float endCardAnimTime = 0.5f;     // how long the entrance takes

        public const float EndCardHold = 3f;     // seconds of card kept after it enters

        // the game's BG music under the take (recorder sessions start in Gameplay, where the
        // Loading scene's music player never exists)
        public bool music;
        public int musicTrack;              // index into the BGMusicPlaylist
        public float musicVolume = 0.35f;   // under the voice, so the lines stay clear

        // image overlays (AdsImagePortrait scene) — one entry per picked image
        public List<GPAdImage> adImages = new List<GPAdImage>();
        // last placement used for each image name — switching a row's image restores its own
        // height/pos/bg instead of keeping the previous image's
        public List<GPAdImage> adImagePresets = new List<GPAdImage>();

        /// <summary>Remember this overlay's placement under its image name.</summary>
        public void StoreImagePreset(GPAdImage img) {
            if (img == null || string.IsNullOrEmpty(img.name)) return;
            var p = adImagePresets.Find(a => a.name == img.name);
            if (p == null) { p = new GPAdImage { name = img.name }; adImagePresets.Add(p); }
            p.height = img.height;
            p.pos = img.pos;
            p.bg = img.bg;
        }

        /// <summary>Apply the placement remembered for <paramref name="name"/> (defaults if new).</summary>
        public void ApplyImagePreset(GPAdImage img, string name) {
            img.name = name;
            var p = adImagePresets.Find(a => a.name == name);
            var src = p ?? new GPAdImage();
            img.height = src.height;
            img.pos = src.pos;
            img.bg = src.bg;
        }

        /// <summary>True once a level was captured — a default-constructed (or Unity-deserialized
        /// empty) instance has size 0 and means "no record loaded".</summary>
        public bool IsValid => level != null && level.size > 0;

        public float Duration {
            get {
                float d = actions.Count > 0 ? actions[actions.Count - 1].time : 0f;
                if (handKeys.Count > 0) d = Mathf.Max(d, handKeys[handKeys.Count - 1].time);
                if (spotKeys.Count > 0) d = Mathf.Max(d, spotKeys[spotKeys.Count - 1].time);
                if (voiceKeys.Count > 0) d = Mathf.Max(d, voiceKeys[voiceKeys.Count - 1].time);
                d = Mathf.Max(d, endTime);
                if (showEndCard) d = Mathf.Max(d, endCardTime + EndCardHold);   // the end follows the card
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
