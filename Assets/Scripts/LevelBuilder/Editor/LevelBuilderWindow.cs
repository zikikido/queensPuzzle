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
    public partial class LevelBuilderWindow : EditorWindow
    {
        const string LevelsFolder = "Assets/Levels/Sets";   // root; sets live in subfolders, __Play in the root
        const int LoadPickerId = 9210;

        // Level sets = the subfolders of Assets/Levels (e.g. MSet = Meowdoku references,
        // Puzzby = our own game's levels). Discovered from disk — add a folder, get a set.
        string[] _sets = { "MSet", "Puzzby" };
        int _loadSetIdx, _saveSetIdx;
        // per-window selection, serialized so it survives domain reloads; empty = first open → prefs
        [SerializeField] string _loadSetName, _saveSetName;
        string LoadSet => SetAt(_loadSetIdx);
        string SaveSet => SetAt(_saveSetIdx);
        string SetAt(int i) => _sets[Mathf.Clamp(i, 0, _sets.Length - 1)];
        static string SetFolder(string set) => $"{LevelsFolder}/{set}";

        void RefreshSets()
        {
            // this window's own choice wins; the shared prefs only seed a freshly opened window
            string load = string.IsNullOrEmpty(_loadSetName) ? EditorPrefs.GetString(PrefLoadSet, "MSet") : _loadSetName;
            string save = string.IsNullOrEmpty(_saveSetName) ? EditorPrefs.GetString(PrefSaveSet, "Puzzby") : _saveSetName;

            var found = new List<string>();
            if (System.IO.Directory.Exists(LevelsFolder))
                foreach (var d in System.IO.Directory.GetDirectories(LevelsFolder))
                    found.Add(System.IO.Path.GetFileName(d));
            found.Sort(System.StringComparer.OrdinalIgnoreCase);
            if (found.Count == 0) found.AddRange(new[] { "MSet", "Puzzby" }); // created on first save
            _sets = found.ToArray();
            _loadSetIdx = SetIndex(load);
            _saveSetIdx = SetIndex(save);
            _loadSetName = LoadSet;
            _saveSetName = SaveSet;
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
        float _targetStart;
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
        bool _showBreakdown;       // collapse the rating breakdown — the "Aimed …" status stays visible
        int _selectedStep = -1;    // -1 = show the full solution; otherwise the board state at that step

        // paint-regions editor: draw a colour layout on the board, then Build solves it into a level.
        // Cells may be left EMPTY (-1): Fill searches queens + completes the empty cells around the
        // painted drawing, which stays pixel-exact.
        bool _paintMode;
        int[] _paintRegions;
        int _paintN;
        int _paintColor;           // -1 = the "empty" colour (erase back to unpainted)
        int _fillAttempts = 500;   // Fill re-rolls (solution + growth + repair) before giving up

        [MenuItem("QueensPuzzle/Level Builder")]
        public static void Open()
        {
            var w = GetWindow<LevelBuilderWindow>("Level Builder");
            w.minSize = new Vector2(440, 680);
        }

        // A second (third…) independent window — e.g. one on the MSet reference, one saving to Puzzby.
        [MenuItem("QueensPuzzle/New Level Builder Window")]
        public static void OpenNew()
        {
            var w = CreateWindow<LevelBuilderWindow>("Level Builder");
            w.minSize = new Vector2(440, 680);
        }

        const string PrefLoadSet = "QP.LevelBuilder.LoadSet";
        const string PrefSaveSet = "QP.LevelBuilder.SaveSet";
        const string PrefTolWeight = "QP.LevelBuilder.TolWeight";
        const string PrefTolPeak = "QP.LevelBuilder.TolPeak";
        const string PrefTolEvenness = "QP.LevelBuilder.TolEvenness";
        const string PrefTolSteps = "QP.LevelBuilder.TolSteps";
        const string PrefTolStart = "QP.LevelBuilder.TolStart";
        const string PrefWarmStart = "QP.LevelBuilder.WarmStart";

        // fingerprint tolerances, in % — how far a generated level may deviate per parameter
        int _tolWeight = 10, _tolPeak = 15, _tolEvenness = 10, _tolSteps = 20, _tolStart = 15;
        bool _warmStart = true; // Generate mutates the loaded board (when sizes match) instead of a random start

        void OnEnable()
        {
            RefreshSets();
            _tolWeight = EditorPrefs.GetInt(PrefTolWeight, 10);
            _tolPeak = EditorPrefs.GetInt(PrefTolPeak, 15);
            _tolEvenness = EditorPrefs.GetInt(PrefTolEvenness, 10);
            _tolSteps = EditorPrefs.GetInt(PrefTolSteps, 20);
            _tolStart = EditorPrefs.GetInt(PrefTolStart, 15);
            _warmStart = EditorPrefs.GetBool(PrefWarmStart, true);
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
                GUILayout.Label("start", EditorStyles.miniLabel, GUILayout.Width(32));
                _targetStart = Mathf.Clamp(EditorGUILayout.FloatField(_targetStart, GUILayout.Width(36)), 0f, 1f);
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
                GUILayout.Label("start", EditorStyles.miniLabel, GUILayout.Width(32));
                _tolStart = Mathf.Clamp(EditorGUILayout.IntField(_tolStart, GUILayout.Width(36)), 0, 100);
            }
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetInt(PrefTolWeight, _tolWeight);
                EditorPrefs.SetInt(PrefTolPeak, _tolPeak);
                EditorPrefs.SetInt(PrefTolEvenness, _tolEvenness);
                EditorPrefs.SetInt(PrefTolSteps, _tolSteps);
                EditorPrefs.SetInt(PrefTolStart, _tolStart);
            }
            EditorGUILayout.LabelField(" ", "weight 0 = random · peak/even/steps/start 0 = ignore · auto-fills on Load", EditorStyles.miniLabel);

            EditorGUI.BeginChangeCheck();
            _warmStart = EditorGUILayout.ToggleLeft("Warm start — mutate the loaded board (same size) instead of a random start", _warmStart);
            if (EditorGUI.EndChangeCheck()) EditorPrefs.SetBool(PrefWarmStart, _warmStart);

            _requestedN = EditorGUILayout.IntSlider("Board size (N)", _requestedN,
                LevelGenerator.MinSize, LevelGenerator.MaxSize);
            EditorGUILayout.LabelField(" ", $"= {_requestedN} queens", EditorStyles.miniLabel);

            string genLabel = _genTask != null ? "Generating… (Cancel in the progress bar)"
                : _targetWeight <= 0 ? "Generate (random)" : $"Generate (weight ~{_targetWeight})";
            using (new EditorGUI.DisabledScope(_genTask != null))
                if (GUILayout.Button(genLabel, GUILayout.Height(28))) Generate();

            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                _loadSetIdx = EditorGUILayout.Popup("Load from", _loadSetIdx, _sets);
                _saveSetIdx = EditorGUILayout.Popup("Save to", _saveSetIdx, _sets);
            }
            if (EditorGUI.EndChangeCheck())
            {
                _loadSetName = LoadSet;
                _saveSetName = SaveSet;
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
            _paintRegions = new int[_level.regions.Length];
            for (int i = 0; i < _paintRegions.Length; i++)
                _paintRegions[i] = _level.ColorOf(_level.regions[i]);   // paint holds PALETTE indexes
            _paintColor = Mathf.Clamp(_paintColor, -1, PaletteCount - 1);
        }

        // the game's full colour palette (paint brushes); board colours resolve through it
        static int PaletteCount
        {
            get
            {
                var p = qp.SORegionsColors.Instance;
                return p != null && p.Colors != null && p.Colors.Length > 0 ? p.Colors.Length : LevelGenerator.MaxSize;
            }
        }

        // palette indexes → region ids 0..k-1 (ascending palette order); -1 (empty) passes through
        static int[] RemapPaint(int[] paint, out int[] idToColor)
        {
            var distinct = new SortedSet<int>();
            foreach (int v in paint) if (v >= 0) distinct.Add(v);
            idToColor = new int[distinct.Count];
            var map = new Dictionary<int, int>();
            int next = 0;
            foreach (int v in distinct) { idToColor[next] = v; map[v] = next; next++; }
            var ids = new int[paint.Length];
            for (int i = 0; i < paint.Length; i++) ids[i] = paint[i] < 0 ? -1 : map[paint[i]];
            return ids;
        }

        static bool IsIdentity(int[] a)
        {
            for (int i = 0; i < a.Length; i++) if (a[i] != i) return false;
            return true;
        }

        void NewPaintGrid(int n)
        {
            _paintN = Mathf.Clamp(n, LevelGenerator.MinSize, LevelGenerator.MaxSize);
            _paintRegions = new int[_paintN * _paintN];
            for (int i = 0; i < _paintRegions.Length; i++) _paintRegions[i] = -1;   // all empty — draw, then Fill
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
                    int id = _paintRegions[r * n + c];
                    EditorGUI.DrawRect(cell, id < 0 ? EmptyCellColor : BoardVisuals.RegionColor(id, n));
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

            // colour palette — the WHOLE game palette (a board uses any n of them) + the empty brush.
            // Painted colours are saved onto the level (regionColors) and shown in game.
            int palette = PaletteCount;
            const float sw = 26f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((EditorGUIUtility.currentViewWidth - 16f) / sw));
            int slots = palette + 1;
            int paletteRows = (slots + perRow - 1) / perRow;
            var pr = GUILayoutUtility.GetRect(0, paletteRows * 28, GUILayout.ExpandWidth(true));
            for (int i = 0; i < slots; i++)
            {
                var cell = new Rect(pr.x + (i % perRow) * sw, pr.y + (i / perRow) * 28, sw - 2, 26);
                bool isEmpty = i == palette;
                if (isEmpty)
                {
                    EditorGUI.DrawRect(cell, EmptyCellColor);
                    GUI.Label(cell, "×", EditorStyles.centeredGreyMiniLabel);
                }
                else
                    EditorGUI.DrawRect(cell, BoardVisuals.RegionColor(i, n));
                int brush = isEmpty ? -1 : i;
                if (brush == _paintColor) DrawOutline(cell, Color.black, 2);
                if (Event.current.type == EventType.MouseDown && cell.Contains(Event.current.mousePosition))
                { _paintColor = brush; Event.current.Use(); Repaint(); }
            }

            EditorGUILayout.LabelField($"{n}x{n} — pick any {n} of the {palette} colours (they save with the level). " +
                "Build needs a full board; Fill completes empty (×) cells.", EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button($"Blank {_requestedN}x{_requestedN}", GUILayout.Height(22))) NewPaintGrid(_requestedN);
                using (new EditorGUI.DisabledScope(_level == null))
                    if (GUILayout.Button("From current", GUILayout.Height(22))) SeedPaintFromLevel();
                if (GUILayout.Button("Build", GUILayout.Height(24))) BuildFromPaint();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("attempts", EditorStyles.miniLabel, GUILayout.Width(54));
                _fillAttempts = Mathf.Clamp(EditorGUILayout.IntField(_fillAttempts, GUILayout.Width(60)), 1, 100000);
                if (GUILayout.Button("Fill — complete the empty cells", GUILayout.Height(24))) FillFromPaint();
            }
        }

        // the unpainted look, on the board and in the palette
        static readonly Color EmptyCellColor = new Color(0.16f, 0.16f, 0.18f);

        void FillFromPaint()
        {
            var ids = RemapPaint(_paintRegions, out int[] heroColors);
            if (heroColors.Length > _paintN)
            {
                _status = $"Build failed — {heroColors.Length} colours painted, but a {_paintN}x{_paintN} board has only {_paintN} regions.";
                Repaint();
                return;
            }

            LevelData data;
            string error;
            try
            {
                data = StencilFiller.Fill(_paintN, ids, _fillAttempts, out error, p =>
                    EditorUtility.DisplayProgressBar("Fill",
                        $"completing the drawing — attempt {(int)(p * _fillAttempts) + 1}/{_fillAttempts}…", p));
            }
            finally { EditorUtility.ClearProgressBar(); }

            if (data == null) { _status = "Build failed — " + error; Repaint(); return; }

            // painted regions keep their palette colours; filled-in regions take unused ones in order
            var colors = new int[_paintN];
            System.Array.Copy(heroColors, colors, heroColors.Length);
            var taken = new HashSet<int>(heroColors);
            int nextClr = 0;
            for (int id = heroColors.Length; id < _paintN; id++)
            {
                while (taken.Contains(nextClr)) nextClr++;
                colors[id] = nextClr;
                taken.Add(nextClr);
            }
            data.regionColors = IsIdentity(colors) ? null : colors;

            SetLevel(data);
            _paintMode = false;   // show the solved board
            _status = $"Filled {data.size}x{data.size} — weight {data.weight}, unique ✓. Press Save to keep it.";
            Repaint();
        }

        void BuildFromPaint()
        {
            var ids = RemapPaint(_paintRegions, out int[] idToColor);
            LevelData data = null;
            string error;
            if (System.Array.IndexOf(ids, -1) >= 0)
                error = "the board has empty (×) cells — press Fill instead.";
            else
                data = LevelImporter.BuildFromRegions(_paintN, ids, out error);
            if (data == null) { _status = "Build failed — " + error; Repaint(); return; }
            data.regionColors = IsIdentity(idToColor) ? null : idToColor;
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
                    EditorGUI.DrawRect(cell, BoardVisuals.RegionColor(_level.ColorOf(_level.RegionAt(r, c)), n));
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
                        bool revealed = _level.IsRevealedRow(r);
                        DrawGlyph(cell, _queenTex,
                            revealed ? new Color(0.95f, 0.6f, 0.1f) : new Color(0.13f, 0.13f, 0.17f), 0.78f);
                        if (revealed) DrawOutline(cell, new Color(0.95f, 0.6f, 0.1f), 2f);
                    }
                }
            }

            // click a solution queen to toggle "revealed at start" (the orange ones open pre-placed)
            var e = Event.current;
            if (!stepMode && e.type == EventType.MouseDown && board.Contains(e.mousePosition))
            {
                int cc = Mathf.Clamp((int)((e.mousePosition.x - board.x) / cs), 0, n - 1);
                int rr = Mathf.Clamp((int)((e.mousePosition.y - board.y) / cs), 0, n - 1);
                if (_level.IsSolutionQueen(rr, cc)) { ToggleRevealed(rr); e.Use(); }
            }
            if (!stepMode)
                EditorGUILayout.LabelField("Click a queen to toggle revealed-at-start (orange)",
                    EditorStyles.centeredGreyMiniLabel);
        }

        void ToggleRevealed(int row)
        {
            var list = new List<int>(_level.revealedRows ?? new int[0]);
            if (!list.Remove(row)) list.Add(row);
            list.Sort();
            _level.revealedRows = list.ToArray();
            if (EditorUtility.IsPersistent(_level)) EditorUtility.SetDirty(_level);
            Repaint();
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
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y + 1, 13, 13), BoardVisuals.RegionColor(_level.ColorOf(g), n));
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

            // stepping into a collapsed detour with Prev/Next must reveal it
            if (_selectedStep >= 0 && _selectedStep < trace.Length)
                for (int p = trace[_selectedStep].parent; p >= 0; p = trace[p].parent)
                    if (IsDetour(trace[p])) _expandedBranches.Add(p);

            for (int i = 0; i < trace.Length; i++)
            {
                var nd = trace[i];
                if (HiddenByCollapse(trace, i)) continue;

                // indent: deeper for every refuted-branch ancestor; branch rows half-step extra.
                // The winning branch and everything after it stay on the main line (indent 0).
                int detours = 0;
                for (int p = nd.parent; p >= 0; p = trace[p].parent) if (IsDetour(trace[p])) detours++;
                float indent = detours * 28f + (nd.kind == NodeKind.TrialBranch ? 14f : 0f);

                bool detourBranch = IsDetour(nd);
                string prefix = nd.kind == NodeKind.TrialBranch ? (detourBranch ? "✗ " : "✓ ")
                              : nd.outcome == Outcome.DeadEnd ? "✗ "
                              : nd.outcome == Outcome.Unresolved ? "… " : "";
                string cost = nd.cost > 0 ? (nd.streak ? $"+{nd.cost} (streak ×½)  " : $"+{nd.cost}  ") : "";
                string label = $"{i}.  [{TechniqueTag(nd.technique)}]  {cost}{prefix}{nd.note}";
                if (detourBranch && !_expandedBranches.Contains(i))
                    label += $"   ({SubtreeSize(trace, i)} hidden steps — dead end)";
                else if (nd.kind == NodeKind.TrialBranch && !detourBranch)
                    label += "   (the real path — continues below)";

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(indent);
                    if (detourBranch)
                    {
                        bool open = _expandedBranches.Contains(i);
                        if (GUILayout.Button(open ? "▾" : "▸", GUILayout.Width(22)))
                        {
                            if (open) _expandedBranches.Remove(i); else _expandedBranches.Add(i);
                            Repaint();
                        }
                    }

                    Color prevBg = GUI.backgroundColor;
                    if (i == _selectedStep) GUI.backgroundColor = new Color(1f, 0.88f, 0.35f);
                    else if (nd.kind == NodeKind.TrialRoot) GUI.backgroundColor = new Color(1f, 0.8f, 0.55f);
                    else if (detourBranch) GUI.backgroundColor = new Color(1f, 0.7f, 0.7f);
                    else if (nd.kind == NodeKind.TrialBranch) GUI.backgroundColor = new Color(0.72f, 1f, 0.72f);
                    else if (detours > 0) GUI.backgroundColor = new Color(1f, 0.9f, 0.9f);
                    if (GUILayout.Button(label, rowStyle)) { _selectedStep = i; Repaint(); }
                    GUI.backgroundColor = prevBg;
                }
            }
        }

        // ---- trace tree helpers: refuted trial branches are collapsible detours ------

        readonly HashSet<int> _expandedBranches = new HashSet<int>();

        // a refuted trial branch = a what-if detour; the winning branch continues the main line
        static bool IsDetour(TraceNode nd) => nd.kind == NodeKind.TrialBranch && nd.outcome != Outcome.Continues;

        bool HiddenByCollapse(TraceNode[] trace, int idx)
        {
            for (int p = trace[idx].parent; p >= 0; p = trace[p].parent)
                if (IsDetour(trace[p]) && !_expandedBranches.Contains(p)) return true;
            return false;
        }

        // nodes whose ancestor chain passes through `root` (the branch's whole exploration)
        static int SubtreeSize(TraceNode[] trace, int root)
        {
            int count = 0;
            for (int j = root + 1; j < trace.Length; j++)
            {
                bool under = false;
                for (int p = trace[j].parent; p >= 0; p = trace[p].parent)
                    if (p == root) { under = true; break; }
                if (under) count++;
                else if (count > 0) break;   // DFS order — once we leave the subtree it never resumes
            }
            return count;
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
                    _showBreakdown = EditorGUILayout.Foldout(_showBreakdown, $"Rating breakdown — weight {rep.weight}", true);
                }
                if (_report.HasValue && _showBreakdown)
                {
                    var rep = _report.Value;
                    string shape = rep.evenness >= 0.7f ? "smooth grind" : rep.evenness <= 0.4f ? "peaky — has a wall" : "mixed";
                    string opening = rep.startShare > 0.4f ? "STARTS HARD" : rep.startShare < 0.2f ? "easy opening" : "ok opening";
                    EditorGUILayout.LabelField($"find (scanning) {rep.findCost}   +   think (tricks) {rep.thinkCost}   +   guesses {rep.guessCost}");
                    EditorGUILayout.LabelField($"peak {rep.peak}   ·   evenness {rep.evenness:0.00}   ·   paid steps {rep.paidSteps}  —  {shape}");
                    EditorGUILayout.LabelField($"start {rep.startShare:P0} of the weight in the first third  —  {opening}");
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
            _targetStart = Mathf.Round(r.startShare * 100f) / 100f;
        }

        LevelFingerprint TargetFingerprint() => new LevelFingerprint
        {
            weight = _targetWeight,
            peak = _targetPeak,
            evenness = _targetEvenness,
            steps = _targetSteps,
            startShare = _targetStart,
            tolWeightPct = _tolWeight,
            tolPeakPct = _tolPeak,
            tolEvennessPct = _tolEvenness,
            tolStepsPct = _tolSteps,
            tolStartPct = _tolStart,
        };

        // Single-level Generate runs on a worker task so the editor never blocks; the cancelable
        // progress bar is polled on the editor loop, and Cancel reaches the worker cooperatively —
        // its progress callback throws on the next tick.
        struct GenResult { public bool ok; public int[] region, sol; public int weight; }
        System.Threading.Tasks.Task<GenResult> _genTask;
        volatile float _genProgress;
        volatile bool _genCancel;
        int _genSeed, _genN;
        bool _genWarm;
        LevelFingerprint _genFp;
        string _genMsg;
        System.Diagnostics.Stopwatch _genSw;

        void Generate()
        {
            if (_genTask != null) return;   // one at a time

            int seed = System.Environment.TickCount;
            var fp = TargetFingerprint();
            // warm start: anneal from the loaded board when it matches the requested size
            bool warm = _warmStart && _targetWeight > 0 && _level != null && _level.size == _requestedN;
            int n = _requestedN;
            int targetWeight = _targetWeight;
            int[] warmRegion = warm ? (int[])_level.regions.Clone() : null;
            int[] warmSol = warm ? (int[])_level.solutionColumns.Clone() : null;

            _genSeed = seed; _genN = n; _genFp = fp; _genWarm = warm;
            _genProgress = 0f; _genCancel = false;
            _genMsg = targetWeight <= 0
                ? $"{n}x{n} — finding a unique puzzle…"
                : $"aiming for weight {fp.weight} — {(warm ? "mutating the loaded board" : "annealing the region map")}…";
            _genSw = System.Diagnostics.Stopwatch.StartNew();

            // worker-safe: only writes a field, and turns Cancel into a throw at the next tick
            System.Action<float> progress = p =>
            {
                _genProgress = p;
                if (_genCancel) throw new System.OperationCanceledException();
            };

            _genTask = System.Threading.Tasks.Task.Run(() =>
            {
                var res = new GenResult();
                res.ok = targetWeight <= 0
                    ? LevelGenerator.TryGenerateRaw(n, seed, 250, out res.region, out res.sol, out res.weight, progress)
                    : LevelSteerer.TrySteerRaw(fp, n, seed, warmRegion, warmSol, out res.region, out res.sol, out res.weight, progress);
                return res;
            });
            EditorApplication.update += PollGenerate;
        }

        void PollGenerate()
        {
            if (_genTask == null) { EditorApplication.update -= PollGenerate; return; }   // domain-reload safety

            if (!_genTask.IsCompleted)
            {
                if (!_genCancel && EditorUtility.DisplayCancelableProgressBar("Generating level", _genMsg, _genProgress))
                    _genCancel = true;   // the worker throws on its next progress tick
                return;
            }

            EditorApplication.update -= PollGenerate;
            EditorUtility.ClearProgressBar();
            var task = _genTask;
            _genTask = null;
            _genSw.Stop();

            if (_genCancel)
            {
                _status = "Generation canceled.";
                Repaint();
                return;
            }
            if (task.IsFaulted)
            {
                _status = "Generation failed — " + (task.Exception?.InnerException?.Message ?? "unknown error");
                Repaint();
                return;
            }
            var res = task.Result;
            if (!res.ok)
            {
                _status = $"Generation failed for {_genN}x{_genN} — press Generate again.";
                Repaint();
                return;
            }

            // back on the main thread: assemble the asset-side object and adopt it
            var lvl = ScriptableObject.CreateInstance<LevelData>();
            lvl.size = _genN;
            lvl.regions = res.region;
            lvl.solutionColumns = res.sol;
            lvl.seed = _genSeed;
            lvl.weight = res.weight;

            SetLevel(lvl);
            if (_genFp.weight <= 0)
                _status = $"Generated {lvl.size}x{lvl.size} (seed {_genSeed}) — weight {lvl.weight}, unique ✓";
            else
            {
                var rep = _report.Value;
                var fp = _genFp;
                string hit = WeightAnnealer.OnTarget(rep, fp) ? "✓ on target" : "closest reachable";
                string got = $"weight {rep.weight}"
                    + (fp.peak > 0 ? $" · peak {rep.peak}" : "")
                    + (fp.evenness > 0 ? $" · even {rep.evenness:0.00}" : "")
                    + (fp.steps > 0 ? $" · steps {rep.paidSteps}" : "")
                    + (fp.startShare > 0 ? $" · start {rep.startShare:0.00}" : "");
                _status = $"Aimed {fp.weight}/{(fp.peak > 0 ? fp.peak.ToString() : "-")}/{(fp.evenness > 0 ? fp.evenness.ToString("0.00") : "-")}/{(fp.steps > 0 ? fp.steps.ToString() : "-")} → {got} — {hit} ({(_genWarm ? "warm" : "cold")}, {_genSw.Elapsed.TotalSeconds:0.0}s).";
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

            // a playtest always starts fresh — never restore the saved board of the real progression
            qp.AppData.LastPlayData.Invalidate();

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
            AnnotateGuessCosts(_trace);
            _importText = lvl != null ? LevelToGridText(lvl) : "";
            _paintRegions = null;   // re-seed the paint grid from the new level on next use
            _selectedStep = -1;
        }

        // Price every guess from the tree itself, so the numbers always match what the list
        // shows: a refuted branch = setup + the work inside its dead end (+ setup per nested
        // guess); the guess row (TrialRoot) = the sum of its refuted branches. Processed in
        // reverse so branch prices exist before their root sums them.
        static void AnnotateGuessCosts(TraceNode[] trace)
        {
            if (trace == null) return;
            for (int i = trace.Length - 1; i >= 0; i--)
            {
                if (trace[i].kind == NodeKind.TrialBranch)
                    trace[i].cost = trace[i].outcome != Outcome.Continues
                        ? WeightRater.GuessSetup + DeadEndWork(trace, i)
                        : WeightRater.GuessSetup;   // the true cell: its test proves nothing but is still paid
                else if (trace[i].kind == NodeKind.TrialRoot)
                {
                    int sum = 0;
                    for (int j = i + 1; j < trace.Length; j++)
                        if (trace[j].parent == i && trace[j].kind == NodeKind.TrialBranch)
                            sum += trace[j].cost;
                    trace[i].cost = sum;
                }
            }
        }

        // Deduction work inside a branch's exploration, plus a setup fee for each nested guess.
        static int DeadEndWork(TraceNode[] trace, int root)
        {
            int sum = 0; bool entered = false;
            for (int j = root + 1; j < trace.Length; j++)
            {
                bool under = false;
                for (int p = trace[j].parent; p >= 0; p = trace[p].parent)
                    if (p == root) { under = true; break; }
                if (!under) { if (entered) break; continue; }   // DFS order — leaving means done
                entered = true;
                if (trace[j].kind == NodeKind.TrialRoot) sum += WeightRater.GuessSetup;
                else if (trace[j].kind != NodeKind.TrialBranch) sum += trace[j].cost;
            }
            return sum;
        }

        // Guided path = no ancestor is a refuted trial branch (those are the what-if detours).
        static bool OnGuidedPath(TraceNode[] trace, int idx)
        {
            for (int p = trace[idx].parent; p >= 0; p = trace[p].parent)
                if (trace[p].kind == NodeKind.TrialBranch && trace[p].outcome != Outcome.Continues) return false;
            return true;
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
            AnnotateGuessCosts(_trace);
            if (AssetDatabase.Contains(_level)) { EditorUtility.SetDirty(_level); AssetDatabase.SaveAssets(); }
            _selectedStep = -1;
            _status = $"Rechecked: weight {rep.weight} ({rep.technique}) — {_trace.Length} solve steps.";
            Repaint();
        }

        // ---- helpers -----------------------------------------------------------------

        static void EnsureLevelsFolder()
        {
            if (AssetDatabase.IsValidFolder(LevelsFolder)) return;
            if (!AssetDatabase.IsValidFolder("Assets/Levels"))
                AssetDatabase.CreateFolder("Assets", "Levels");
            AssetDatabase.CreateFolder("Assets/Levels", "Sets");
        }

        static void EnsureSetFolder(string set)
        {
            EnsureLevelsFolder();
            if (!AssetDatabase.IsValidFolder(SetFolder(set)))
                AssetDatabase.CreateFolder(LevelsFolder, set);
        }
    }
}
