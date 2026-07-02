using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace QueensPuzzle.EditorTools
{
    /// <summary>
    /// Editor hub for levels: Generate a new unique puzzle, Load a saved one, Save the current
    /// one as an asset, and Play it (phase 1: the game just shows the board).
    ///
    /// Levels ARE measured: every generated/loaded level is rated, and Recheck re-rates on demand.
    /// </summary>
    public class LevelBuilderWindow : EditorWindow
    {
        const string LevelsFolder = "Assets/Levels";   // root; sets live in subfolders, __Play in the root
        const int LoadPickerId = 9210;

        // Level sets = the subfolders of Assets/Levels (e.g. MSet = Meowdoku references,
        // Puzzyby = our own game's levels). Discovered from disk — add a folder, get a set.
        string[] _sets = { "MSet", "Puzzyby" };
        int _loadSetIdx, _saveSetIdx, _exportSetIdx;
        string LoadSet => SetAt(_loadSetIdx);
        string SaveSet => SetAt(_saveSetIdx);
        string ExportSet => SetAt(_exportSetIdx);
        string SetAt(int i) => _sets[Mathf.Clamp(i, 0, _sets.Length - 1)];
        static string SetFolder(string set) => $"{LevelsFolder}/{set}";

        void RefreshSets()
        {
            var found = new List<string>();
            if (System.IO.Directory.Exists(LevelsFolder))
                foreach (var d in System.IO.Directory.GetDirectories(LevelsFolder))
                    found.Add(System.IO.Path.GetFileName(d));
            found.Sort(System.StringComparer.OrdinalIgnoreCase);
            if (found.Count == 0) found.AddRange(new[] { "MSet", "Puzzyby" }); // created on first save
            _sets = found.ToArray();
            _loadSetIdx = SetIndex(EditorPrefs.GetString(PrefLoadSet, "MSet"));
            _saveSetIdx = SetIndex(EditorPrefs.GetString(PrefSaveSet, "Puzzyby"));
            _exportSetIdx = SetIndex(EditorPrefs.GetString(PrefExportSet, "Puzzyby"));
        }

        int SetIndex(string name)
        {
            for (int i = 0; i < _sets.Length; i++) if (_sets[i] == name) return i;
            return 0;
        }

        int _requestedN = 8;
        int _targetWeight;      // what Generate aims for; 0 = instant random board (no steering)
        int _targetPeak;        // optional fingerprint targets; 0 = don't care
        float _targetEvenness;
        int _targetSteps;
        int _levelNumber = 1;   // slot for numbered Load / Save ({set}/{n}.asset)
        int _loadCount;         // consecutive levels from 1 in the load set (cached)
        int _saveCount;         // same for the save set
        bool _saveGap;          // a level exists past the save set's run → a hole in the sequence (error)

        [SerializeField] LevelData _level;
        WeightRater.Report? _report;
        TraceNode[] _trace;   // built on Generate / Load / Recheck for display — not stored on the asset
        string _status = "Pick the parameters and press Generate.";
        Texture2D _queenTex;
        Vector2 _scroll;

        bool _showImport;
        string _importText = "";

        bool _showSolution = true; // collapse the board when you don't want the answer visible
        bool _showSteps = true;    // collapse the solve-steps list
        int _selectedStep = -1;    // -1 = show the full solution; otherwise the board state at that step

        // paint-regions editor: draw a colour layout on the board, then Build solves it into a level
        bool _paintMode;
        int[] _paintRegions;
        int _paintN;
        int _paintColor;

        [MenuItem("QueensPuzzle/Level Builder")]
        public static void Open()
        {
            var w = GetWindow<LevelBuilderWindow>("Level Builder");
            w.minSize = new Vector2(440, 680);
        }

        const string PrefLoadSet = "QP.LevelBuilder.LoadSet";
        const string PrefSaveSet = "QP.LevelBuilder.SaveSet";
        const string PrefExportSet = "QP.LevelBuilder.ExportSet";
        const string PrefTolWeight = "QP.LevelBuilder.TolWeight";
        const string PrefTolPeak = "QP.LevelBuilder.TolPeak";
        const string PrefTolEvenness = "QP.LevelBuilder.TolEvenness";
        const string PrefTolSteps = "QP.LevelBuilder.TolSteps";

        // fingerprint tolerances, in % — how far a generated level may deviate per parameter
        int _tolWeight = 10, _tolPeak = 15, _tolEvenness = 10, _tolSteps = 20;

        void OnEnable()
        {
            RefreshSets();
            _tolWeight = EditorPrefs.GetInt(PrefTolWeight, 10);
            _tolPeak = EditorPrefs.GetInt(PrefTolPeak, 15);
            _tolEvenness = EditorPrefs.GetInt(PrefTolEvenness, 10);
            _tolSteps = EditorPrefs.GetInt(PrefTolSteps, 20);
            _queenTex = BoardVisuals.CreateQueenTexture(64);
            RefreshMaxLevel();

            // a domain reload (entering/leaving play) clears _level — restore the level we last
            // played from its persisted asset so it doesn't vanish from the builder
            if (_level == null)
            {
                string guid = SessionState.GetString(qp.LevelLoader.PlayLevelGuidKey, "");
                string path = string.IsNullOrEmpty(guid) ? null : AssetDatabase.GUIDToAssetPath(guid);
                var lvl = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<LevelData>(path);
                if (lvl != null) SetLevel(lvl);
            }
        }

        void OnFocus() { RefreshSets(); RefreshMaxLevel(); Repaint(); }   // catches folder/level changes done outside Unity

        void RefreshMaxLevel()
        {
            CountSet(SetFolder(LoadSet), out _loadCount, out _);
            CountSet(SetFolder(SaveSet), out _saveCount, out _saveGap);
        }

        static void CountSet(string folder, out int count, out bool gap)
        {
            var present = new HashSet<int>();
            int max = 0;
            if (System.IO.Directory.Exists(folder))
                foreach (var f in System.IO.Directory.GetFiles(folder, "*.asset"))
                    if (int.TryParse(System.IO.Path.GetFileNameWithoutExtension(f), out int k))
                    { present.Add(k); if (k > max) max = k; }

            count = 0;
            while (present.Contains(count + 1)) count++;   // consecutive run from 1
            gap = max > count;                             // a level sits past the gap → broken sequence
        }

        void OnDisable()
        {
            if (_queenTex != null) DestroyImmediate(_queenTex);
        }

        void OnGUI()
        {
            HandleObjectPicker();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawControls();
            DrawImport();
            EditorGUILayout.Space(6);
            _showSolution = EditorGUILayout.Foldout(_showSolution, "Solution (board)", true);
            if (_showSolution)
            {
                _paintMode = EditorGUILayout.ToggleLeft("Paint regions (pick a colour, drag on the board)", _paintMode);
                DrawBoard();
                if (_paintMode) DrawPaintTools();
                else DrawLegend();
            }
            DrawSteps();
            EditorGUILayout.Space(6);
            DrawFooter();
            EditorGUILayout.EndScrollView();
        }

        // ---- controls ----------------------------------------------------------------

        void DrawControls()
        {
            EditorGUILayout.LabelField("Generate", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Target fingerprint", GUILayout.Width(146));
                GUILayout.Label("weight", EditorStyles.miniLabel, GUILayout.Width(42));
                _targetWeight = Mathf.Max(0, EditorGUILayout.IntField(_targetWeight, GUILayout.Width(36)));
                GUILayout.Label("peak", EditorStyles.miniLabel, GUILayout.Width(32));
                _targetPeak = Mathf.Max(0, EditorGUILayout.IntField(_targetPeak, GUILayout.Width(36)));
                GUILayout.Label("even", EditorStyles.miniLabel, GUILayout.Width(32));
                _targetEvenness = Mathf.Clamp(EditorGUILayout.FloatField(_targetEvenness, GUILayout.Width(36)), 0f, 1f);
                GUILayout.Label("steps", EditorStyles.miniLabel, GUILayout.Width(36));
                _targetSteps = Mathf.Max(0, EditorGUILayout.IntField(_targetSteps, GUILayout.Width(36)));
                using (new EditorGUI.DisabledScope(!_report.HasValue))
                    if (GUILayout.Button("⟵ level", EditorStyles.miniButton, GUILayout.Width(56)))
                        CopyFingerprintFromReport();
            }

            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Tolerance %", GUILayout.Width(146));
                GUILayout.Label("weight", EditorStyles.miniLabel, GUILayout.Width(42));
                _tolWeight = Mathf.Clamp(EditorGUILayout.IntField(_tolWeight, GUILayout.Width(36)), 0, 100);
                GUILayout.Label("peak", EditorStyles.miniLabel, GUILayout.Width(32));
                _tolPeak = Mathf.Clamp(EditorGUILayout.IntField(_tolPeak, GUILayout.Width(36)), 0, 100);
                GUILayout.Label("even", EditorStyles.miniLabel, GUILayout.Width(32));
                _tolEvenness = Mathf.Clamp(EditorGUILayout.IntField(_tolEvenness, GUILayout.Width(36)), 0, 100);
                GUILayout.Label("steps", EditorStyles.miniLabel, GUILayout.Width(36));
                _tolSteps = Mathf.Clamp(EditorGUILayout.IntField(_tolSteps, GUILayout.Width(36)), 0, 100);
            }
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetInt(PrefTolWeight, _tolWeight);
                EditorPrefs.SetInt(PrefTolPeak, _tolPeak);
                EditorPrefs.SetInt(PrefTolEvenness, _tolEvenness);
                EditorPrefs.SetInt(PrefTolSteps, _tolSteps);
            }
            EditorGUILayout.LabelField(" ", "weight 0 = random · peak/even/steps 0 = ignore · auto-fills on Load", EditorStyles.miniLabel);

            _requestedN = EditorGUILayout.IntSlider("Board size (N)", _requestedN,
                LevelGenerator.MinSize, LevelGenerator.MaxSize);
            EditorGUILayout.LabelField(" ", $"= {_requestedN} queens", EditorStyles.miniLabel);

            string genLabel = _targetWeight <= 0 ? "Generate (random)" : $"Generate (weight ~{_targetWeight})";
            if (GUILayout.Button(genLabel, GUILayout.Height(28))) Generate();

            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                _loadSetIdx = EditorGUILayout.Popup("Load from", _loadSetIdx, _sets);
                _saveSetIdx = EditorGUILayout.Popup("Save to", _saveSetIdx, _sets);
            }
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(PrefLoadSet, LoadSet);
                EditorPrefs.SetString(PrefSaveSet, SaveSet);
                RefreshMaxLevel();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _levelNumber = Mathf.Max(1, EditorGUILayout.IntField("Level number", _levelNumber));
                GUILayout.Label($"of {_loadCount} in {LoadSet}", EditorStyles.miniLabel, GUILayout.Width(110));
                if (_saveGap)
                {
                    var prev = GUI.color; GUI.color = new Color(1f, 0.5f, 0.4f);
                    GUILayout.Label($"⚠ {SaveSet}: level {_saveCount + 1} missing", EditorStyles.miniBoldLabel);
                    GUI.color = prev;
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("<", GUILayout.Height(24), GUILayout.Width(30)))
                { _levelNumber = Mathf.Max(1, _levelNumber - 1); LoadNumber(); }
                if (GUILayout.Button("Load", GUILayout.Height(24))) LoadNumber();
                if (GUILayout.Button(">", GUILayout.Height(24), GUILayout.Width(30)))
                { _levelNumber++; LoadNumber(); }

                using (new EditorGUI.DisabledScope(_level == null))
                {
                    if (GUILayout.Button("Save", GUILayout.Height(24))) SaveNumber();
                    if (GUILayout.Button("Recheck", GUILayout.Height(24))) Recheck();
                }

                if (GUILayout.Button("Play", GUILayout.Height(24))) Play();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Export levels → Resources", GUILayout.Height(22)))
                    LevelResourcesExporter.Export(SetFolder(ExportSet), ExportSet);
                EditorGUI.BeginChangeCheck();
                _exportSetIdx = EditorGUILayout.Popup(_exportSetIdx, _sets, GUILayout.Width(90));
                if (EditorGUI.EndChangeCheck()) EditorPrefs.SetString(PrefExportSet, ExportSet);
            }
        }

        void HandleObjectPicker()
        {
            if (Event.current.commandName == "ObjectSelectorClosed"
                && EditorGUIUtility.GetObjectPickerControlID() == LoadPickerId)
            {
                if (EditorGUIUtility.GetObjectPickerObject() is LevelData picked)
                {
                    SetLevel(picked);
                    CopyFingerprintFromReport();
                    _status = $"Loaded {picked.name} ({picked.size}x{picked.size}) — weight {_report?.weight}.";
                }
                Repaint();
            }
        }

        // ---- import (build a specific board from its colours) -------------------------

        void DrawImport()
        {
            EditorGUILayout.Space(4);
            _showImport = EditorGUILayout.Foldout(_showImport, "Import from region grid", true);
            if (!_showImport) return;

            EditorGUILayout.LabelField(
                "One row per line. Cells are space-separated (\"A A B\") or one char each (\"AAB\"). " +
                "Any labels work — first-seen order assigns the colours. The queens are solved for you.",
                EditorStyles.wordWrappedMiniLabel);

            _importText = EditorGUILayout.TextArea(_importText, GUILayout.MinHeight(120));

            if (GUILayout.Button("Import", GUILayout.Height(24))) Import();
        }

        // ---- paint regions (draw a colour layout on the board, then Build solves it) ----------

        void EnsurePaintGrid()
        {
            if (_paintRegions != null) return;
            if (_level != null) SeedPaintFromLevel();
            else NewPaintGrid(_requestedN);
        }

        void SeedPaintFromLevel()
        {
            _paintN = _level.size;
            _paintRegions = (int[])_level.regions.Clone();
            _paintColor = Mathf.Clamp(_paintColor, 0, _paintN - 1);
        }

        void NewPaintGrid(int n)
        {
            _paintN = Mathf.Clamp(n, LevelGenerator.MinSize, LevelGenerator.MaxSize);
            _paintRegions = new int[_paintN * _paintN];   // all one colour to start
            _paintColor = 0;
        }

        void DrawPaintBoard(Rect board, float avail)
        {
            EnsurePaintGrid();
            int n = _paintN;
            float cs = avail / n;
            EditorGUI.DrawRect(board, new Color(0.42f, 0.42f, 0.48f)); // grid lines

            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    var cell = new Rect(board.x + c * cs + 1, board.y + r * cs + 1, cs - 2, cs - 2);
                    EditorGUI.DrawRect(cell, BoardVisuals.RegionColor(_paintRegions[r * n + c], n));
                }

            var e = Event.current;
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && board.Contains(e.mousePosition))
            {
                int c = Mathf.Clamp((int)((e.mousePosition.x - board.x) / cs), 0, n - 1);
                int r = Mathf.Clamp((int)((e.mousePosition.y - board.y) / cs), 0, n - 1);
                _paintRegions[r * n + c] = _paintColor;
                e.Use();
                Repaint();
            }
        }

        void DrawPaintTools()
        {
            EnsurePaintGrid();
            int n = _paintN;

            // colour palette — click to pick the active region
            var pr = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            float sw = Mathf.Min(30f, pr.width / n);
            for (int i = 0; i < n; i++)
            {
                var cell = new Rect(pr.x + i * sw, pr.y, sw - 2, 26);
                EditorGUI.DrawRect(cell, BoardVisuals.RegionColor(i, n));
                if (i == _paintColor) DrawOutline(cell, Color.black, 2);
                if (Event.current.type == EventType.MouseDown && cell.Contains(Event.current.mousePosition))
                { _paintColor = i; Event.current.Use(); Repaint(); }
            }

            EditorGUILayout.LabelField($"{n}x{n} — use exactly {n} colours, then Build.", EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button($"Blank {_requestedN}x{_requestedN}", GUILayout.Height(22))) NewPaintGrid(_requestedN);
                using (new EditorGUI.DisabledScope(_level == null))
                    if (GUILayout.Button("From current", GUILayout.Height(22))) SeedPaintFromLevel();
                if (GUILayout.Button("Build", GUILayout.Height(24))) BuildFromPaint();
            }
        }

        void BuildFromPaint()
        {
            var data = LevelImporter.BuildFromRegions(_paintN, (int[])_paintRegions.Clone(), out string error);
            if (data == null) { _status = "Build failed — " + error; Repaint(); return; }
            SetLevel(data);       // SetLevel clears _paintRegions so it re-seeds from the built level next time
            _paintMode = false;   // show the solved board
            _status = $"Built {data.size}x{data.size} — weight {data.weight}, unique ✓. Press Save to keep it.";
            Repaint();
        }

        // ---- board ---------------------------------------------------------------------
        // Shows the full solution by default; when a solve step is selected, shows the board
        // state up to and including that step (queens placed, X's marked) with the step's own
        // cells outlined.

        void DrawBoard()
        {
            float avail = Mathf.Clamp(position.width - 36f, 200f, 440f);
            Rect board = GUILayoutUtility.GetRect(avail, avail, GUILayout.ExpandWidth(false));
            board.x = (position.width - avail) * 0.5f;

            if (_paintMode) { DrawPaintBoard(board, avail); return; }

            if (_level == null)
            {
                EditorGUI.DrawRect(board, new Color(0f, 0f, 0f, 0.06f));
                GUI.Label(board, "No level yet — Generate or Load",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 12 });
                return;
            }

            int n = _level.size;
            float cs = avail / n;
            EditorGUI.DrawRect(board, new Color(0.42f, 0.42f, 0.48f)); // grid lines

            var trace = _trace;
            bool stepMode = _selectedStep >= 0 && trace != null && _selectedStep < trace.Length;
            bool[] queens = null, xMark = null, attacked = null; HashSet<int> highlight = null;
            if (stepMode) ComputeState(_selectedStep, out queens, out xMark, out attacked, out highlight);

            var letterStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = Mathf.Max(8, Mathf.RoundToInt(cs * 0.24f)),
                normal = { textColor = new Color(0f, 0f, 0f, 0.5f) }
            };

            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    int i = r * n + c;
                    var cell = new Rect(board.x + c * cs + 1, board.y + r * cs + 1, cs - 2, cs - 2);
                    EditorGUI.DrawRect(cell, BoardVisuals.RegionColor(_level.RegionAt(r, c), n));
                    GUI.Label(new Rect(cell.x + 2, cell.y + 1, cell.width, cell.height * 0.5f),
                        ((char)('A' + _level.RegionAt(r, c))).ToString(), letterStyle);

                    if (stepMode)
                    {
                        if (attacked[i] && !queens[i] && !xMark[i])
                            EditorGUI.DrawRect(cell, new Color(0f, 0f, 0f, 0.16f)); // dim: ruled out by a queen
                        if (queens[i]) DrawGlyph(cell, _queenTex, new Color(0.13f, 0.13f, 0.17f), 0.72f);
                        else if (xMark[i]) DrawX(cell, new Color(0.6f, 0.06f, 0.06f));
                        if (highlight.Contains(i)) DrawOutline(cell, new Color(1f, 0.85f, 0.1f), 2f);
                    }
                    else if (_level.IsSolutionQueen(r, c))
                    {
                        DrawGlyph(cell, _queenTex, new Color(0.13f, 0.13f, 0.17f), 0.78f);
                    }
                }
            }
        }

        void DrawGlyph(Rect cell, Texture2D tex, Color tint, float scale)
        {
            float sz = cell.width * scale;
            var r = new Rect(cell.center.x - sz / 2f, cell.center.y - sz / 2f, sz, sz);
            Color prev = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit, true);
            GUI.color = prev;
        }

        static void DrawX(Rect cell, Color color)
        {
            var st = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            st.fontSize = Mathf.RoundToInt(cell.height * 0.55f);
            st.normal.textColor = color;
            GUI.Label(cell, "✕", st);
        }

        static void DrawOutline(Rect cell, Color color, float t)
        {
            EditorGUI.DrawRect(new Rect(cell.x, cell.y, cell.width, t), color);
            EditorGUI.DrawRect(new Rect(cell.x, cell.yMax - t, cell.width, t), color);
            EditorGUI.DrawRect(new Rect(cell.x, cell.y, t, cell.height), color);
            EditorGUI.DrawRect(new Rect(cell.xMax - t, cell.y, t, cell.height), color);
        }

        // Replays the trace from the root down to (and including) the selected step, accumulating
        // placed queens and X-marks. A placed queen also dims every cell it attacks.
        void ComputeState(int step, out bool[] queens, out bool[] xMark, out bool[] attacked,
            out HashSet<int> highlight)
        {
            int n = _level.size;
            queens = new bool[n * n];
            xMark = new bool[n * n];
            attacked = new bool[n * n];
            highlight = new HashSet<int>();
            var trace = _trace;

            var path = new List<int>();
            for (int cur = step; cur >= 0 && cur < trace.Length; cur = trace[cur].parent) path.Add(cur);
            path.Reverse();

            foreach (int ni in path)
            {
                var marks = trace[ni].marks;
                if (marks == null) continue;
                foreach (var m in marks)
                {
                    if (m.mark == CellMark.Queen) { queens[m.cell] = true; MarkAttacked(m.cell, attacked); }
                    else xMark[m.cell] = true;
                }
            }

            var sel = trace[step].marks;
            if (sel != null) foreach (var m in sel) highlight.Add(m.cell);
        }

        void MarkAttacked(int cell, bool[] attacked)
        {
            int n = _level.size, r = cell / n, c = cell % n, g = _level.regions[cell];
            for (int cc = 0; cc < n; cc++) attacked[r * n + cc] = true;
            for (int rr = 0; rr < n; rr++) attacked[rr * n + c] = true;
            for (int i = 0; i < n * n; i++) if (_level.regions[i] == g) attacked[i] = true;
            for (int dr = -1; dr <= 1; dr++)
                for (int dc = -1; dc <= 1; dc++)
                {
                    int nr = r + dr, nc = c + dc;
                    if (nr >= 0 && nr < n && nc >= 0 && nc < n) attacked[nr * n + nc] = true;
                }
        }

        // ---- legend & solve steps ----------------------------------------------------

        void DrawLegend()
        {
            if (_level == null) return;
            int n = _level.size;
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Colours (letter = region)", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int g = 0; g < n; g++)
                {
                    var rect = GUILayoutUtility.GetRect(30, 16, GUILayout.Width(30), GUILayout.Height(16));
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y + 1, 13, 13), BoardVisuals.RegionColor(g, n));
                    GUI.Label(new Rect(rect.x + 15, rect.y, 15, 16), ((char)('A' + g)).ToString(), EditorStyles.miniLabel);
                }
            }
        }

        void DrawSteps()
        {
            if (_level == null) return;

            EditorGUILayout.Space(6);
            _showSteps = EditorGUILayout.Foldout(_showSteps, "Solve steps — click a step to see the board there", true);
            if (!_showSteps) return;

            var trace = _trace;
            if (trace == null || trace.Length == 0)
            {
                EditorGUILayout.HelpBox("No solve trace on this level. Press Recheck to (re)build it.", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_selectedStep < 0))
                    if (GUILayout.Button("◀ Prev", GUILayout.Width(70)))
                        { _selectedStep = Mathf.Max(0, _selectedStep - 1); Repaint(); }

                using (new EditorGUI.DisabledScope(_selectedStep >= trace.Length - 1))
                    if (GUILayout.Button("Next ▶", GUILayout.Width(70)))
                        { _selectedStep = _selectedStep < 0 ? 0 : _selectedStep + 1; Repaint(); }

                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(_selectedStep < 0))
                    if (GUILayout.Button("Show full solution", GUILayout.Width(130)))
                        { _selectedStep = -1; Repaint(); }
            }

            var rowStyle = new GUIStyle(EditorStyles.miniButton)
            {
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                fixedHeight = 0,
                richText = false
            };

            for (int i = 0; i < trace.Length; i++)
            {
                var nd = trace[i];
                string prefix = nd.outcome == Outcome.DeadEnd ? "✗ "
                              : nd.outcome == Outcome.Unresolved ? "… " : "";
                string cost = nd.cost > 0 ? (nd.streak ? $"+{nd.cost} (streak ×½)  " : $"+{nd.cost}  ") : "";
                string label = $"{i}.  [{TechniqueTag(nd.technique)}]  {cost}{prefix}{nd.note}";

                Color prevBg = GUI.backgroundColor;
                if (i == _selectedStep) GUI.backgroundColor = new Color(1f, 0.88f, 0.35f);
                if (GUILayout.Button(label, rowStyle)) { _selectedStep = i; Repaint(); }
                GUI.backgroundColor = prevBg;
            }
        }

        static string TechniqueTag(SolveTechnique t)
        {
            switch (t)
            {
                case SolveTechnique.RegionSingle:
                case SolveTechnique.LineSingle: return "place";
                case SolveTechnique.LineToRegion: return "line→region";
                case SolveTechnique.RegionToLine: return "region→line";
                case SolveTechnique.Squeeze: return "squeeze";
                case SolveTechnique.RegionChoke: return "choke";
                case SolveTechnique.SubsetLineToRegion: return "subset L→R";
                case SolveTechnique.SubsetRegionToLine: return "subset R→L";
                case SolveTechnique.Fish: return "fish";
                case SolveTechnique.QueenScope: return "queen";
                case SolveTechnique.Trial: return "guess";
                default: return "step";
            }
        }

        // ---- footer ------------------------------------------------------------------

        static int TechCost(WeightRater.Report rep, SolveTechnique t) =>
            rep.techCost != null ? rep.techCost[(int)t] : 0;

        // One line per technique actually used: uses, its share of the weight, and a bar.
        static void TechRow(string name, int uses, int cost, int total)
        {
            if (uses == 0) return;
            int share = total > 0 ? Mathf.RoundToInt(cost * 100f / total) : 0;
            string bar = new string('▮', Mathf.Clamp(Mathf.RoundToInt(cost * 20f / Mathf.Max(total, 1)), 0, 20));
            EditorGUILayout.LabelField($"{name}:   ×{uses}   —   {cost} pts ({share}%)   {bar}");
        }

        void DrawFooter()
        {
            if (_level != null)
            {
                string weightText = _report.HasValue ? _report.Value.weight.ToString() : "-";
                EditorGUILayout.HelpBox(
                    $"{_level.size}x{_level.size}  ·  seed {_level.seed}  ·  weight {weightText}  ·  unique ✓",
                    MessageType.None);

                if (_report.HasValue)
                {
                    var rep = _report.Value;
                    string shape = rep.evenness >= 0.7f ? "smooth grind" : rep.evenness <= 0.4f ? "peaky — has a wall" : "mixed";
                    EditorGUILayout.LabelField($"Rating breakdown — weight {rep.weight}", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"find (scanning) {rep.findCost}   +   think (tricks) {rep.thinkCost}   +   guesses {rep.guessCost}");
                    EditorGUILayout.LabelField($"peak {rep.peak}   ·   evenness {rep.evenness:0.00}   ·   paid steps {rep.paidSteps}  —  {shape}");
                    EditorGUILayout.Space(4);
                    TechRow("Queen shadow (free)", rep.techUses[(int)SolveTechnique.QueenScope], 0, rep.weight);
                    TechRow("Region single", rep.regionSingles, TechCost(rep, SolveTechnique.RegionSingle), rep.weight);
                    TechRow("Line single", rep.lineSingles, TechCost(rep, SolveTechnique.LineSingle), rep.weight);
                    TechRow("Line→region", rep.lineToRegionUses, TechCost(rep, SolveTechnique.LineToRegion), rep.weight);
                    TechRow("Region→line", rep.regionToLineUses, TechCost(rep, SolveTechnique.RegionToLine), rep.weight);
                    TechRow("Squeeze", rep.squeezeUses, TechCost(rep, SolveTechnique.Squeeze), rep.weight);
                    TechRow("Subset L→R", rep.subsetLineToRegionUses, TechCost(rep, SolveTechnique.SubsetLineToRegion), rep.weight);
                    TechRow("Subset R→L", rep.subsetRegionToLineUses, TechCost(rep, SolveTechnique.SubsetRegionToLine), rep.weight);
                    TechRow("Region choke", rep.regionChokeUses, TechCost(rep, SolveTechnique.RegionChoke), rep.weight);
                    TechRow("Positional fish", rep.fishUses, TechCost(rep, SolveTechnique.Fish), rep.weight);
                    TechRow(rep.maxTrialDepth >= 2 ? $"Guesses (nested ×{rep.maxTrialDepth})" : "Guesses", rep.trials, rep.guessCost, rep.weight);
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField($"Steps: {rep.cycles}     Placed: {rep.placements}     Eliminated: {rep.eliminations}");
                }
            }
            EditorGUILayout.LabelField(_status, EditorStyles.miniLabel);
        }

        // ---- actions -----------------------------------------------------------------

        // Fill the target fingerprint from the currently loaded/rated level.
        void CopyFingerprintFromReport()
        {
            if (!_report.HasValue) return;
            var r = _report.Value;
            _targetWeight = r.weight;
            _targetPeak = r.peak;
            _targetEvenness = Mathf.Round(r.evenness * 100f) / 100f;
            _targetSteps = r.paidSteps;
        }

        LevelFingerprint TargetFingerprint() => new LevelFingerprint
        {
            weight = _targetWeight,
            peak = _targetPeak,
            evenness = _targetEvenness,
            steps = _targetSteps,
            tolWeightPct = _tolWeight,
            tolPeakPct = _tolPeak,
            tolEvennessPct = _tolEvenness,
            tolStepsPct = _tolSteps,
        };

        void Generate()
        {
            int seed = System.Environment.TickCount;
            var fp = TargetFingerprint();
            // warm start: anneal from the loaded board when it matches the requested size
            bool warm = _targetWeight > 0 && _level != null && _level.size == _requestedN;
            LevelData lvl;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (_targetWeight <= 0)
                    lvl = LevelGenerator.Generate(_requestedN, seed, 250, p =>
                        EditorUtility.DisplayProgressBar("Generating level",
                            $"{_requestedN}x{_requestedN} — finding a unique puzzle…", p));
                else
                    lvl = LevelSteerer.Generate(fp, _requestedN, seed,
                        warm ? _level.regions : null, warm ? _level.solutionColumns : null, p =>
                        EditorUtility.DisplayProgressBar("Generating level",
                            $"aiming for weight {fp.weight} — {(warm ? "mutating the loaded board" : "annealing the region map")}…", p));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            sw.Stop();
            if (lvl == null)
            {
                _status = $"Generation failed for {_requestedN}x{_requestedN} — press Generate again.";
                return;
            }

            SetLevel(lvl);
            if (_targetWeight <= 0)
                _status = $"Generated {lvl.size}x{lvl.size} (seed {seed}) — weight {lvl.weight}, unique ✓";
            else
            {
                var rep = _report.Value;
                string hit = WeightAnnealer.OnTarget(rep, fp) ? "✓ on target" : "closest reachable";
                string got = $"weight {rep.weight}"
                    + (fp.peak > 0 ? $" · peak {rep.peak}" : "")
                    + (fp.evenness > 0 ? $" · even {rep.evenness:0.00}" : "")
                    + (fp.steps > 0 ? $" · steps {rep.paidSteps}" : "");
                _status = $"Aimed {fp.weight}/{(fp.peak > 0 ? fp.peak.ToString() : "-")}/{(fp.evenness > 0 ? fp.evenness.ToString("0.00") : "-")}/{(fp.steps > 0 ? fp.steps.ToString() : "-")} → {got} — {hit} ({(warm ? "warm" : "cold")}, {sw.Elapsed.TotalSeconds:0.0}s).";
            }
            Repaint();
        }

        void Import()
        {
            var lvl = LevelImporter.Import(_importText, out string error);
            if (lvl == null) { _status = "Import failed — " + error; Repaint(); return; }

            SetLevel(lvl);
            _status = $"Imported {lvl.size}x{lvl.size} — weight {lvl.weight}, unique ✓. Press Save to keep it.";
            Repaint();
        }

        // ---- numbered load / save (Assets/Levels/{set}/{n}.asset) --------------------

        static string LevelPath(string set, int n) => $"{SetFolder(set)}/{n}.asset";

        void LoadNumber()
        {
            var lvl = AssetDatabase.LoadAssetAtPath<LevelData>(LevelPath(LoadSet, _levelNumber));
            if (lvl == null)
            {
                _status = $"No level {_levelNumber} in {SetFolder(LoadSet)}.";
                EditorUtility.DisplayDialog("Load failed", $"There's no level {_levelNumber} in {SetFolder(LoadSet)}.", "OK");
                Repaint();
                return;
            }
            SetLevel(lvl);
            // mirror the generate controls to the loaded level, so a following Generate matches it
            _requestedN = Mathf.Clamp(lvl.size, LevelGenerator.MinSize, LevelGenerator.MaxSize);
            CopyFingerprintFromReport();
            EditorGUIUtility.PingObject(lvl);
            _status = $"Loaded level {_levelNumber} ({lvl.size}x{lvl.size}) — weight {_report?.weight}.";
            Repaint();
        }

        void SaveNumber()
        {
            if (_level == null) return;
            EnsureSetFolder(SaveSet);
            int n = _levelNumber;

            if (AssetDatabase.LoadAssetAtPath<LevelData>(LevelPath(SaveSet, n)) != null)
            {
                // returns 0 = Override, 1 = Cancel, 2 = Push
                int choice = EditorUtility.DisplayDialogComplex(
                    $"{SaveSet} level {n} already exists",
                    $"Save to {SaveSet} slot {n}?\n\n• Override replaces it.\n• Push inserts here and shifts {n} and up one slot higher.",
                    "Override", "Cancel", "Push");
                if (choice == 1) { _status = "Save cancelled."; return; }
                if (choice == 2) PushFrom(n);
            }

            WriteLevel(LevelPath(SaveSet, n), n);
            RefreshMaxLevel();
            _status = $"Saved level {n} to {SaveSet}.";
        }

        // Free slot {from}: rename every existing level {k} >= from up to {k+1}, top-down so nothing collides.
        void PushFrom(int from)
        {
            for (int k = HighestLevelNumber(); k >= from; k--)
            {
                if (AssetDatabase.LoadAssetAtPath<LevelData>(LevelPath(SaveSet, k)) == null) continue;
                AssetDatabase.RenameAsset(LevelPath(SaveSet, k), (k + 1).ToString());
            }
        }

        int HighestLevelNumber()
        {
            string folder = SetFolder(SaveSet);
            if (!System.IO.Directory.Exists(folder)) return 0;
            int max = 0;
            foreach (var f in System.IO.Directory.GetFiles(folder, "*.asset"))
                if (int.TryParse(System.IO.Path.GetFileNameWithoutExtension(f), out int k) && k > max) max = k;
            return max;
        }

        // Create at path, or overwrite in place; keep the asset's name matched to its number.
        void WriteLevel(string path, int n)
        {
            var existing = AssetDatabase.LoadAssetAtPath<LevelData>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(_level, existing);
                existing.name = n.ToString();
                EditorUtility.SetDirty(existing);
            }
            else
            {
                var copy = Instantiate(_level);
                copy.name = n.ToString();
                AssetDatabase.CreateAsset(copy, path);
            }
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<LevelData>(path));
        }

        void Play()
        {
            const string scenePath = "Assets/Scenes/Gameplay.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            { _status = $"Scene not found: {scenePath}"; return; }

            if (_level == null) { _status = "Generate or load a level first."; return; }

            // hand the working level to MBGameplay across the play-mode reload (via a stable asset GUID)
            SessionState.SetString(qp.LevelLoader.PlayLevelGuidKey, PersistPlayLevel());

            // already running? just rebuild the live board with the new level — no scene reload
            if (EditorApplication.isPlaying)
            {
                var gp = Object.FindAnyObjectByType<qp.MBGameplay>();
                if (gp != null) { gp.Replay(); _status = "Replayed with current level."; }
                else _status = "No MBGameplay in the running scene.";
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
            _status = "Playing Gameplay scene.";
        }

        // Already a saved asset? use it. Otherwise write the working level to a throwaway asset
        // (overwritten in place each play, so its GUID stays stable). Returns the asset GUID.
        string PersistPlayLevel()
        {
            if (AssetDatabase.Contains(_level))
                return AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_level));

            EnsureLevelsFolder();
            const string playName = "__Play";
            const string playPath = LevelsFolder + "/" + playName + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<LevelData>(playPath);
            if (existing == null)
            {
                var copy = Instantiate(_level);
                copy.name = playName; // match the filename, else Unity warns on CreateAsset
                AssetDatabase.CreateAsset(copy, playPath);
            }
            else
            {
                EditorUtility.CopySerialized(_level, existing); // overwrite in place, keep the GUID
                existing.name = playName; // CopySerialized also copies m_Name — restore the filename
                EditorUtility.SetDirty(existing);
            }
            AssetDatabase.SaveAssets();
            return AssetDatabase.AssetPathToGUID(playPath);
        }

        void SetLevel(LevelData lvl)
        {
            _level = lvl;
            _report = lvl != null ? WeightRater.Rate(lvl.size, lvl.regions, lvl.solutionColumns) : (WeightRater.Report?)null;
            _trace = lvl != null ? SolveTracer.Build(lvl.size, lvl.regions, lvl.solutionColumns) : null;
            _importText = lvl != null ? LevelToGridText(lvl) : "";
            _paintRegions = null;   // re-seed the paint grid from the new level on next use
            _selectedStep = -1;
        }

        // The level's regions as a grid of letters (A, B, …), one row per line — the Import format.
        static string LevelToGridText(LevelData lvl)
        {
            int n = lvl.size;
            var rows = new string[n];
            for (int r = 0; r < n; r++)
            {
                var cells = new string[n];
                for (int c = 0; c < n; c++) cells[c] = ((char)('A' + lvl.RegionAt(r, c))).ToString();
                rows[r] = string.Join(" ", cells);   // space-separated: "A A B"
            }
            return string.Join("\n", rows);
        }

        void Recheck()
        {
            var rep = WeightRater.Rate(_level.size, _level.regions, _level.solutionColumns);
            _report = rep;
            _level.weight = rep.weight;
            _trace = SolveTracer.Build(_level.size, _level.regions, _level.solutionColumns);
            if (AssetDatabase.Contains(_level)) { EditorUtility.SetDirty(_level); AssetDatabase.SaveAssets(); }
            _selectedStep = -1;
            _status = $"Rechecked: weight {rep.weight} ({rep.technique}) — {_trace.Length} solve steps.";
            Repaint();
        }

        // ---- helpers -----------------------------------------------------------------

        static void EnsureLevelsFolder()
        {
            if (!AssetDatabase.IsValidFolder(LevelsFolder))
                AssetDatabase.CreateFolder("Assets", "Levels");
        }

        static void EnsureSetFolder(string set)
        {
            EnsureLevelsFolder();
            if (!AssetDatabase.IsValidFolder(SetFolder(set)))
                AssetDatabase.CreateFolder(LevelsFolder, set);
        }
    }
}
