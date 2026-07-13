using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace QueensPuzzle.EditorTools
{
    /// <summary>
    /// Places hand-curated "nice" levels into a destination set. Scan rates every level in the
    /// pool set and the destination set, then shows for each nice level where it LOCATES — the
    /// destination slot whose fingerprint (weight / steps / peak / evenness) matches within
    /// tolerance — or, when nothing matches, the closest slots ordered best-first. Each candidate
    /// has a Replace button: the destination slot keeps its number and asset GUID, only the board
    /// (size, regions, solution, weight) is overwritten.
    /// </summary>
    public class NicePlacerWindow : EditorWindow
    {
        const string LevelsFolder = "Assets/Levels";
        const int Candidates = 5;

        [MenuItem("QueensPuzzle/Nice Level Placer")]
        static void Open()
        {
            var w = CreateWindow<NicePlacerWindow>("Nice Placer");
            w.minSize = new Vector2(520, 400);
        }

        class Entry
        {
            public string path;
            public string name;             // file name (usually the slot number)
            public int number;              // parsed slot number, -1 when the name isn't one
            public LevelData lvl;
            public WeightRater.Report rep;
            public int hash;                // ContentHash — the same identity the game's saves trust
        }

        class Row
        {
            public Entry nice;
            public List<(Entry dest, double dist, bool onTarget, string off)> candidates = new List<(Entry, double, bool, string)>();
            public int replacedInto = -1;   // slot number after a Replace this session
            public string existsAt;         // destination slot(s) already holding this exact board
        }

        string[] _sets = { };
        int _poolIdx, _destIdx;
        int _tolWeight = 10, _tolSteps = 20;   // % — a candidate within both "locates" the level
        bool _sameSizeOnly = true;
        List<Row> _rows;
        Vector2 _scroll;
        string _status = "Pick the pool and destination sets, then Scan.";

        void OnEnable() { RefreshSets(); }
        void OnFocus() { RefreshSets(); }

        void RefreshSets()
        {
            var found = new List<string>();
            if (Directory.Exists(LevelsFolder))
                foreach (var dir in Directory.GetDirectories(LevelsFolder))
                    found.Add(Path.GetFileName(dir));
            _sets = found.ToArray();
            for (int i = 0; i < _sets.Length; i++)
            {
                if (_sets[i] == "NiceSet" && _poolIdx == 0) _poolIdx = i;
                if (_sets[i] == "Puzzby" && _destIdx == 0) _destIdx = i;
            }
        }

        void OnGUI()
        {
            if (_sets.Length == 0) { EditorGUILayout.HelpBox($"No set folders under {LevelsFolder}.", MessageType.Info); return; }

            using (new EditorGUILayout.HorizontalScope())
            {
                _poolIdx = EditorGUILayout.Popup("Nice pool", Mathf.Clamp(_poolIdx, 0, _sets.Length - 1), _sets);
                _destIdx = EditorGUILayout.Popup("Destination", Mathf.Clamp(_destIdx, 0, _sets.Length - 1), _sets);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("tolerance %", EditorStyles.miniLabel, GUILayout.Width(70));
                GUILayout.Label("weight", EditorStyles.miniLabel, GUILayout.Width(42));
                _tolWeight = Mathf.Clamp(EditorGUILayout.IntField(_tolWeight, GUILayout.Width(36)), 0, 100);
                GUILayout.Label("steps", EditorStyles.miniLabel, GUILayout.Width(36));
                _tolSteps = Mathf.Clamp(EditorGUILayout.IntField(_tolSteps, GUILayout.Width(36)), 0, 100);
                _sameSizeOnly = EditorGUILayout.ToggleLeft("same board size only", _sameSizeOnly);
            }

            if (GUILayout.Button($"Scan {Pool} → {Dest}", GUILayout.Height(26))) Scan();

            EditorGUILayout.LabelField(_status, EditorStyles.miniLabel);
            if (_rows == null) return;

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var row in _rows) DrawRow(row);
            EditorGUILayout.EndScrollView();
        }

        string Pool => _sets[Mathf.Clamp(_poolIdx, 0, _sets.Length - 1)];
        string Dest => _sets[Mathf.Clamp(_destIdx, 0, _sets.Length - 1)];

        static string Fp(WeightRater.Report r) => $"w {r.weight} · steps {r.paidSteps} · peak {r.peak} · even {r.evenness:0.00}";

        void DrawRow(Row row)
        {
            var n = row.nice;
            EditorGUILayout.Space(6);

            if (row.replacedInto >= 0)
            {
                EditorGUILayout.LabelField($"{n.name} ({n.lvl.size}x{n.lvl.size}) — replaced into slot {row.replacedInto} ✓", EditorStyles.boldLabel);
                return;
            }
            if (row.existsAt != null)
            {
                EditorGUILayout.LabelField(
                    $"{n.name} ({n.lvl.size}x{n.lvl.size}) — already in {Dest}: slot {row.existsAt} (same board, by content hash)",
                    EditorStyles.boldLabel);
                return;
            }

            bool located = row.candidates.Count > 0 && row.candidates[0].onTarget;
            EditorGUILayout.LabelField(
                $"{n.name} ({n.lvl.size}x{n.lvl.size}, {Fp(n.rep)}) — {(located ? "locates ✓" : "no slot in tolerance, closest:")}",
                EditorStyles.boldLabel);

            if (row.candidates.Count == 0)
            {
                EditorGUILayout.LabelField("    no candidates (size filter?)", EditorStyles.miniLabel);
                return;
            }

            foreach (var (dest, dist, onTarget, off) in row.candidates)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(16);
                    EditorGUILayout.LabelField(
                        $"{(onTarget ? "✓" : "  ")} slot {dest.name} ({dest.lvl.size}x{dest.lvl.size}, {Fp(dest.rep)}) · distance {dist:0.00}{off}",
                        GUILayout.MinWidth(330));
                    if (GUILayout.Button("Replace", GUILayout.Width(72))) Replace(row, dest);
                }
            }
        }

        void Scan()
        {
            try
            {
                var pool = LoadSet(Pool, "rating nice pool");
                var dest = LoadSet(Dest, "rating destination");
                _rows = new List<Row>();

                // exact-board lookup: a nice level already in the destination must not go in twice
                var destByHash = new Dictionary<int, List<Entry>>();
                foreach (var d in dest)
                {
                    if (!destByHash.TryGetValue(d.hash, out var list)) destByHash[d.hash] = list = new List<Entry>();
                    list.Add(d);
                }

                foreach (var nice in pool)
                {
                    var row = new Row { nice = nice };
                    if (destByHash.TryGetValue(nice.hash, out var already))
                    {
                        row.existsAt = string.Join(", ", already.ConvertAll(e => e.name));
                        _rows.Add(row);
                        continue;   // no candidates — replacing would duplicate the board
                    }
                    foreach (var d in dest)
                    {
                        if (_sameSizeOnly && d.lvl.size != nice.lvl.size) continue;
                        double dw = Math.Abs(nice.rep.weight - d.rep.weight) / Math.Max(1.0, d.rep.weight);
                        double ds = Math.Abs(nice.rep.paidSteps - d.rep.paidSteps) / Math.Max(1.0, d.rep.paidSteps);
                        double dp = Math.Abs(nice.rep.peak - d.rep.peak) / Math.Max(1.0, d.rep.peak);
                        double de = Math.Abs(nice.rep.evenness - d.rep.evenness);
                        double dist = dw + ds + 0.5 * dp + 0.5 * de;
                        bool onTarget = dw * 100 <= _tolWeight && ds * 100 <= _tolSteps;

                        // for non-matches: say which tolerance gate broke, so the ✓ logic is visible
                        string off = "";
                        if (dw * 100 > _tolWeight) off += $" · w off {dw * 100:0}%>{_tolWeight}%";
                        if (ds * 100 > _tolSteps) off += $" · steps off {ds * 100:0}%>{_tolSteps}%";

                        row.candidates.Add((d, dist, onTarget, off));
                    }
                    // matches within tolerance first, closest inside each group
                    row.candidates.Sort((a, b) => a.onTarget != b.onTarget
                        ? (a.onTarget ? -1 : 1)
                        : a.dist.CompareTo(b.dist));
                    if (row.candidates.Count > Candidates)
                        row.candidates.RemoveRange(Candidates, row.candidates.Count - Candidates);
                    _rows.Add(row);
                }

                int locatedCount = 0, existing = 0;
                foreach (var r in _rows)
                {
                    if (r.existsAt != null) existing++;
                    else if (r.candidates.Count > 0 && r.candidates[0].onTarget) locatedCount++;
                }
                _status = $"{_rows.Count} nice level(s): {existing} already in {Dest}, {locatedCount} locate within tolerance, "
                        + $"{_rows.Count - existing - locatedCount} need a choice.";
            }
            finally { EditorUtility.ClearProgressBar(); }
            Repaint();
        }

        List<Entry> LoadSet(string set, string what)
        {
            var list = new List<Entry>();
            string folder = $"{LevelsFolder}/{set}";
            if (!Directory.Exists(folder)) return list;

            var files = Directory.GetFiles(folder, "*.asset");
            Array.Sort(files, (a, b) =>
            {
                bool na = int.TryParse(Path.GetFileNameWithoutExtension(a), out int ia);
                bool nb = int.TryParse(Path.GetFileNameWithoutExtension(b), out int ib);
                return na && nb ? ia.CompareTo(ib) : string.CompareOrdinal(a, b);
            });

            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i].Replace('\\', '/');
                var lvl = AssetDatabase.LoadAssetAtPath<LevelData>(path);
                if (lvl == null || lvl.regions == null || lvl.solutionColumns == null) continue;
                EditorUtility.DisplayProgressBar("Scan", $"{what} — {Path.GetFileName(path)}…", i / (float)files.Length);
                list.Add(new Entry
                {
                    path = path,
                    name = Path.GetFileNameWithoutExtension(path),
                    number = int.TryParse(Path.GetFileNameWithoutExtension(path), out int num) ? num : -1,
                    lvl = lvl,
                    rep = WeightRater.Rate(lvl.size, lvl.regions, lvl.solutionColumns),
                    hash = lvl.ContentHash(),
                });
            }
            return list;
        }

        void Replace(Row row, Entry dest)
        {
            if (!EditorUtility.DisplayDialog("Replace level",
                $"Overwrite {Dest}/{dest.name} with {Pool}/{row.nice.name}?\n\n" +
                $"{dest.name}: {dest.lvl.size}x{dest.lvl.size}, {Fp(dest.rep)}\n" +
                $"{row.nice.name}: {row.nice.lvl.size}x{row.nice.lvl.size}, {Fp(row.nice.rep)}\n\n" +
                "The slot keeps its number and GUID; only the board changes.", "Replace", "Cancel"))
                return;

            var lvl = dest.lvl;   // overwrite in place — keeps the asset and its references
            lvl.size = row.nice.lvl.size;
            lvl.regions = (int[])row.nice.lvl.regions.Clone();
            lvl.solutionColumns = (int[])row.nice.lvl.solutionColumns.Clone();
            lvl.seed = row.nice.lvl.seed;
            lvl.weight = row.nice.rep.weight;
            lvl.regionColors = row.nice.lvl.regionColors != null ? (int[])row.nice.lvl.regionColors.Clone() : null;
            lvl.revealedRows = null;   // new board — stale reveals would point at wrong rows
            EditorUtility.SetDirty(lvl);
            AssetDatabase.SaveAssets();

            row.replacedInto = dest.number;
            dest.rep = row.nice.rep;    // slot's fingerprint is the nice board's now — keeps other rows honest
            dest.hash = row.nice.hash;  // and the slot now IS this board — a rescan shows it as "already in"
            dest.lvl = lvl;
            _status = $"Replaced {Dest}/{dest.name} with {Pool}/{row.nice.name} — weight {row.nice.rep.weight}.";
            Repaint();
        }
    }
}
