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
    /// NOTE: the difficulty dropdown is a target only — generation does not steer toward it yet.
    /// Levels ARE measured: every generated/loaded level is rated, and Recheck re-rates on demand.
    /// </summary>
    public class LevelBuilderWindow : EditorWindow
    {
        const string LevelsFolder = "Assets/Levels";
        const int LoadPickerId = 9210;

        // What Generate aims for: a tier to steer toward, or Any for an instant random board.
        enum Target { Any, Kitten, Easy, Medium, Hard, Expert }

        int _requestedN = 8;
        Target _target = Target.Medium;
        int _levelNumber = 1;   // slot for numbered Load / Save (Assets/Levels/{n}.asset)
        int _levelCount;        // consecutive saved levels from 1 (cached; refreshed on focus / save)
        bool _levelGap;         // a numbered level exists past that run → a hole in the sequence (error)

        [SerializeField] LevelData _level;
        DifficultyRater.Report? _report;
        TraceNode[] _trace;   // built on Generate / Load / Recheck for display — not stored on the asset
        string _status = "Pick the parameters and press Generate.";
        Texture2D _queenTex;
        Vector2 _scroll;

        bool _showImport;
        string _importText = "";

        bool _showSolution = true; // collapse the board when you don't want the answer visible
        bool _showSteps = true;    // collapse the solve-steps list
        int _selectedStep = -1;    // -1 = show the full solution; otherwise the board state at that step

        [MenuItem("QueensPuzzle/Level Builder")]
        public static void Open()
        {
            var w = GetWindow<LevelBuilderWindow>("Level Builder");
            w.minSize = new Vector2(440, 680);
        }

        void OnEnable()
        {
            _queenTex = BoardVisuals.CreateQueenTexture(64);
            RefreshMaxLevel();

            // a domain reload (entering/leaving play) clears _level — restore the level we last
            // played from its persisted asset so it doesn't vanish from the builder
            if (_level == null)
            {
                string guid = SessionState.GetString(qp.MBGameplay.PlayLevelGuidKey, "");
                string path = string.IsNullOrEmpty(guid) ? null : AssetDatabase.GUIDToAssetPath(guid);
                var lvl = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<LevelData>(path);
                if (lvl != null) SetLevel(lvl);
            }
        }

        void OnFocus() { RefreshMaxLevel(); Repaint(); }   // catches deletes/renames done outside Unity

        void RefreshMaxLevel()
        {
            var present = new HashSet<int>();
            int max = 0;
            if (System.IO.Directory.Exists(LevelsFolder))
                foreach (var f in System.IO.Directory.GetFiles(LevelsFolder, "*.asset"))
                    if (int.TryParse(System.IO.Path.GetFileNameWithoutExtension(f), out int k))
                    { present.Add(k); if (k > max) max = k; }

            int count = 0;
            while (present.Contains(count + 1)) count++;   // consecutive run from 1
            _levelCount = count;
            _levelGap = max > count;                       // a level sits past the gap → broken sequence
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
                DrawBoard();
                DrawLegend();
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

            _target = (Target)EditorGUILayout.EnumPopup("Difficulty", _target);
            EditorGUILayout.LabelField(" ", "Any = instant random · a tier = steered toward it (~1–3s)", EditorStyles.miniLabel);

            _requestedN = EditorGUILayout.IntSlider("Board size (N)", _requestedN,
                LevelGenerator.MinSize, LevelGenerator.MaxSize);
            EditorGUILayout.LabelField(" ", $"= {_requestedN} queens", EditorStyles.miniLabel);

            string genLabel = _target == Target.Any ? "Generate (random)" : $"Generate ({_target})";
            if (GUILayout.Button(genLabel, GUILayout.Height(28))) Generate();

            using (new EditorGUILayout.HorizontalScope())
            {
                _levelNumber = Mathf.Max(1, EditorGUILayout.IntField("Level number", _levelNumber));
                GUILayout.Label($"of {_levelCount} saved", EditorStyles.miniLabel, GUILayout.Width(90));
                if (_levelGap)
                {
                    var prev = GUI.color; GUI.color = new Color(1f, 0.5f, 0.4f);
                    GUILayout.Label($"⚠ level {_levelCount + 1} missing", EditorStyles.miniBoldLabel);
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
                    _status = $"Loaded {picked.name} ({picked.size}x{picked.size}) — measured {_report?.difficulty}.";
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

        // ---- board ---------------------------------------------------------------------
        // Shows the full solution by default; when a solve step is selected, shows the board
        // state up to and including that step (queens placed, X's marked) with the step's own
        // cells outlined.

        void DrawBoard()
        {
            float avail = Mathf.Clamp(position.width - 36f, 200f, 440f);
            Rect board = GUILayoutUtility.GetRect(avail, avail, GUILayout.ExpandWidth(false));
            board.x = (position.width - avail) * 0.5f;

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
                string label = $"{i}.  [{TechniqueTag(nd.technique)}]  {prefix}{nd.note}";

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
                case SolveTechnique.SubsetLineToRegion: return "subset L→R";
                case SolveTechnique.SubsetRegionToLine: return "subset R→L";
                case SolveTechnique.Fish: return "fish";
                case SolveTechnique.QueenScope: return "queen";
                case SolveTechnique.Trial: return "guess";
                default: return "step";
            }
        }

        // ---- footer ------------------------------------------------------------------

        void DrawFooter()
        {
            if (_level != null)
            {
                string scoreText = _report.HasValue ? _report.Value.score.ToString() : "-";
                EditorGUILayout.HelpBox(
                    $"{_level.size}x{_level.size}  ·  seed {_level.seed}  ·  {_level.difficulty}  ·  score {scoreText}  ·  " +
                    $"~{Mathf.RoundToInt(_level.estimatedSolveSeconds)}s  ·  unique ✓",
                    MessageType.None);

                if (_report.HasValue)
                {
                    var rep = _report.Value;
                    EditorGUILayout.LabelField("Rating breakdown — what decided the tier", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"Hardest technique:   {rep.technique}   →   {rep.difficulty}");
                    EditorGUILayout.LabelField($"Score:   {rep.score}");
                    EditorGUILayout.LabelField($"Singles — region:  {rep.regionSingles}     line:  {rep.lineSingles}");
                    EditorGUILayout.LabelField($"Line→region:  {rep.lineToRegionUses}     Region→line:  {rep.regionToLineUses}");
                    EditorGUILayout.LabelField($"Squeeze:  {rep.squeezeUses}     Subset L→R:  {rep.subsetLineToRegionUses}     Subset R→L:  {rep.subsetRegionToLineUses}");
                    EditorGUILayout.LabelField($"Positional fish:  {rep.fishUses}");
                    EditorGUILayout.LabelField($"Trials:  {rep.trials}     Max depth:  {rep.maxTrialDepth}");
                    EditorGUILayout.LabelField($"Cycles:  {rep.cycles}     Placed:  {rep.placements}     Eliminated:  {rep.eliminations}");
                    EditorGUILayout.LabelField($"Estimated solve time:   ~{Mathf.RoundToInt(rep.estimatedSeconds)}s");
                }
            }
            EditorGUILayout.LabelField(_status, EditorStyles.miniLabel);
        }

        // ---- actions -----------------------------------------------------------------

        void Generate()
        {
            int seed = System.Environment.TickCount;
            LevelData lvl;
            try
            {
                if (_target == Target.Any)
                    lvl = LevelGenerator.Generate(_requestedN, seed, 250, p =>
                        EditorUtility.DisplayProgressBar("Generating level",
                            $"{_requestedN}x{_requestedN} — finding a unique puzzle…", p));
                else
                    lvl = LevelSteerer.Generate(ToDifficulty(_target), _requestedN, seed, p =>
                        EditorUtility.DisplayProgressBar("Generating level",
                            $"aiming for {_target} — annealing the region map…", p));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            if (lvl == null)
            {
                _status = $"Generation failed for {_requestedN}x{_requestedN} — press Generate again.";
                return;
            }

            SetLevel(lvl);
            if (_target == Target.Any)
                _status = $"Generated {lvl.size}x{lvl.size} (seed {seed}) — {lvl.difficulty}, unique ✓";
            else
            {
                string hit = lvl.difficulty == ToDifficulty(_target) ? "✓ on target" : "closest reachable";
                _status = $"Aimed for {_target} → {lvl.difficulty}, score {_report?.score} — {hit}.";
            }
            Repaint();
        }

        static Difficulty ToDifficulty(Target t)
        {
            switch (t)
            {
                case Target.Kitten: return Difficulty.Kitten;
                case Target.Easy: return Difficulty.Easy;
                case Target.Medium: return Difficulty.Medium;
                case Target.Hard: return Difficulty.Hard;
                case Target.Expert: return Difficulty.Expert;
                default: return Difficulty.Medium;
            }
        }

        void Import()
        {
            var lvl = LevelImporter.Import(_importText, out string error);
            if (lvl == null) { _status = "Import failed — " + error; Repaint(); return; }

            SetLevel(lvl);
            _status = $"Imported {lvl.size}x{lvl.size} — {lvl.difficulty}, unique ✓. Press Save to keep it.";
            Repaint();
        }

        // ---- numbered load / save (Assets/Levels/{n}.asset) --------------------------

        string LevelPath(int n) => $"{LevelsFolder}/{n}.asset";

        void LoadNumber()
        {
            var lvl = AssetDatabase.LoadAssetAtPath<LevelData>(LevelPath(_levelNumber));
            if (lvl == null)
            {
                _status = $"No level {_levelNumber} in {LevelsFolder}.";
                EditorUtility.DisplayDialog("Load failed", $"There's no level {_levelNumber} in {LevelsFolder}.", "OK");
                Repaint();
                return;
            }
            SetLevel(lvl);
            // mirror the generate controls to the loaded level, so a following Generate matches it
            _requestedN = Mathf.Clamp(lvl.size, LevelGenerator.MinSize, LevelGenerator.MaxSize);
            _target = System.Enum.TryParse(lvl.difficulty.ToString(), out Target t) ? t : Target.Any;
            EditorGUIUtility.PingObject(lvl);
            _status = $"Loaded level {_levelNumber} ({lvl.size}x{lvl.size}) — measured {_report?.difficulty}.";
            Repaint();
        }

        void SaveNumber()
        {
            if (_level == null) return;
            EnsureLevelsFolder();
            int n = _levelNumber;

            if (AssetDatabase.LoadAssetAtPath<LevelData>(LevelPath(n)) != null)
            {
                // returns 0 = Override, 1 = Cancel, 2 = Push
                int choice = EditorUtility.DisplayDialogComplex(
                    $"Level {n} already exists",
                    $"Save to slot {n}?\n\n• Override replaces it.\n• Push inserts here and shifts {n} and up one slot higher.",
                    "Override", "Cancel", "Push");
                if (choice == 1) { _status = "Save cancelled."; return; }
                if (choice == 2) PushFrom(n);
            }

            WriteLevel(LevelPath(n), n);
            RefreshMaxLevel();
            _status = $"Saved level {n}.";
        }

        // Free slot {from}: rename every existing level {k} >= from up to {k+1}, top-down so nothing collides.
        void PushFrom(int from)
        {
            for (int k = HighestLevelNumber(); k >= from; k--)
            {
                if (AssetDatabase.LoadAssetAtPath<LevelData>(LevelPath(k)) == null) continue;
                AssetDatabase.RenameAsset(LevelPath(k), (k + 1).ToString());
            }
        }

        int HighestLevelNumber()
        {
            if (!System.IO.Directory.Exists(LevelsFolder)) return 0;
            int max = 0;
            foreach (var f in System.IO.Directory.GetFiles(LevelsFolder, "*.asset"))
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
            SessionState.SetString(qp.MBGameplay.PlayLevelGuidKey, PersistPlayLevel());

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
            _report = lvl != null ? DifficultyRater.Rate(lvl.size, lvl.regions, lvl.solutionColumns) : (DifficultyRater.Report?)null;
            _trace = lvl != null ? SolveTracer.Build(lvl.size, lvl.regions, lvl.solutionColumns) : null;
            _importText = lvl != null ? LevelToGridText(lvl) : "";
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
            var rep = DifficultyRater.Rate(_level.size, _level.regions, _level.solutionColumns);
            _report = rep;
            _level.difficulty = rep.difficulty;
            _level.estimatedSolveSeconds = rep.estimatedSeconds;
            _trace = SolveTracer.Build(_level.size, _level.regions, _level.solutionColumns);
            if (AssetDatabase.Contains(_level)) { EditorUtility.SetDirty(_level); AssetDatabase.SaveAssets(); }
            _selectedStep = -1;
            _status = $"Rechecked: {rep.difficulty} ({rep.technique}) — {_trace.Length} solve steps.";
            Repaint();
        }

        // ---- helpers -----------------------------------------------------------------

        static void EnsureLevelsFolder()
        {
            if (!AssetDatabase.IsValidFolder(LevelsFolder))
                AssetDatabase.CreateFolder("Assets", "Levels");
        }
    }
}
