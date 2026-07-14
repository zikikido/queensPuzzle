using UnityEditor;
using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// Campaign build window: pick a <see cref="CampaignCurveConfig"/>, a level range, and see
    /// the target-weight graph the curve produces for that range.
    ///
    /// Performance: targets are computed once per "Show graph" click (never in OnGUI), the
    /// polyline is decimated to two points per pixel column (max/min per column, so peaks and
    /// breathers both survive) and cached until data or window size changes, and all drawing
    /// is immediate-mode GL — no per-frame allocations.
    /// </summary>
    public class CampaignBuilderWindow : EditorWindow
    {
        const int MaxRange = 50_000;

        CampaignCurveConfig config;
        int from = 1;
        int to = 600;
        bool graphFoldout = true;
        bool buildFoldout = true;
        const string SetsRoot = "Assets/Levels/Sets";
        string[] sets = { "Campaign" };
        int setIdx;
        string SetName => sets[Mathf.Clamp(setIdx, 0, sets.Length - 1)];
        string OutputFolder => $"{SetsRoot}/{SetName}";
        int seedBase = 1;
        bool skipExisting = true;
        int setCount;    // consecutive levels from 1 in the selected set
        bool setGap;     // a level exists past the run → hole in the sequence
        bool overrideOffTol = true;
        int offTolCount = -1;                       // -1 = needs recount
        (int, int, int, Object) offTolKey;          // (from, to, setIdx, config) the count was made for
        int threadCount = Mathf.Max(1, System.Environment.ProcessorCount - 1);

        // shared with worker threads during generation
        int genNextSeed;
        long genAttempts;
        bool genStop;
        int[] genSizes;

        // graph data, rebuilt only on Show graph
        int[] weights;
        byte[] roles;
        int dataFrom, maxW;

        // screen-space cache, rebuilt when data or rect changes
        Vector3[] line;
        Vector3[] milestones;
        Rect cachedRect;

        static Material lineMat;

        [MenuItem("QueensPuzzle/Campaign Builder")]
        static void Open() => GetWindow<CampaignBuilderWindow>("Campaign Builder");

        void OnEnable() => RefreshSets();
        void OnFocus() { RefreshSets(); offTolCount = -1; }   // catches set/level changes done outside the window

        void RefreshSets()
        {
            string current = SetName;
            var found = new System.Collections.Generic.List<string>();
            if (System.IO.Directory.Exists(SetsRoot))
                foreach (var d in System.IO.Directory.GetDirectories(SetsRoot))
                    found.Add(System.IO.Path.GetFileName(d));
            found.Sort(System.StringComparer.OrdinalIgnoreCase);
            if (found.Count == 0) found.Add("Campaign");   // created on first generate
            sets = found.ToArray();
            setIdx = Mathf.Max(0, System.Array.IndexOf(sets, current));
            RefreshSetStats();
        }

        // List every hole in the set's numbering, from 1 up to the highest existing level.
        void ShowMissingLevels()
        {
            var present = new System.Collections.Generic.HashSet<int>();
            int max = 0;
            if (System.IO.Directory.Exists(OutputFolder))
                foreach (var f in System.IO.Directory.GetFiles(OutputFolder, "*.asset"))
                    if (int.TryParse(System.IO.Path.GetFileNameWithoutExtension(f), out int k))
                    { present.Add(k); if (k > max) max = k; }

            var missing = new System.Collections.Generic.List<int>();
            for (int l = 1; l <= max; l++)
                if (!present.Contains(l)) missing.Add(l);

            if (missing.Count == 0)
            {
                EditorUtility.DisplayDialog("Missing levels", $"{SetName}: no gaps — {max} level(s), all present.", "OK");
                return;
            }
            string list = string.Join(", ", missing);
            Debug.Log($"[CampaignBuilder] {SetName} missing {missing.Count} level(s): {list}");
            if (list.Length > 900) list = list.Substring(0, 900) + "… (full list in the Console)";
            EditorUtility.DisplayDialog("Missing levels",
                $"{SetName}: {missing.Count} missing of 1–{max}:\n\n{list}", "OK");
        }

        void RefreshSetStats()
        {
            var present = new System.Collections.Generic.HashSet<int>();
            int max = 0;
            if (System.IO.Directory.Exists(OutputFolder))
                foreach (var f in System.IO.Directory.GetFiles(OutputFolder, "*.asset"))
                    if (int.TryParse(System.IO.Path.GetFileNameWithoutExtension(f), out int k))
                    { present.Add(k); if (k > max) max = k; }
            setCount = 0;
            while (present.Contains(setCount + 1)) setCount++;   // consecutive run from 1
            setGap = max > setCount;
        }

        void OnGUI()
        {
            config = (CampaignCurveConfig)EditorGUILayout.ObjectField("Curve config", config, typeof(CampaignCurveConfig), false);

            EditorGUILayout.BeginHorizontal();
            from = EditorGUILayout.DelayedIntField("Level from", from);
            to = EditorGUILayout.DelayedIntField("to", to);
            EditorGUILayout.EndHorizontal();

            buildFoldout = EditorGUILayout.Foldout(buildFoldout, "Build", true);
            if (buildFoldout)
            {
                EditorGUI.BeginChangeCheck();
                setIdx = EditorGUILayout.Popup("Set", setIdx, sets);
                if (EditorGUI.EndChangeCheck()) { RefreshSetStats(); offTolCount = -1; }
                if (setGap)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var prev = GUI.color; GUI.color = new Color(1f, 0.5f, 0.4f);
                        EditorGUILayout.LabelField(" ", $"⚠ {setCount} level(s) in {SetName} — level {setCount + 1} missing (gap)", EditorStyles.miniBoldLabel);
                        GUI.color = prev;
                        if (GUILayout.Button("Show missing", GUILayout.Width(96)))
                            ShowMissingLevels();
                    }
                }
                else
                    EditorGUILayout.LabelField(" ", $"{setCount} level(s) in {SetName}, no gaps", EditorStyles.miniLabel);
                seedBase = EditorGUILayout.IntField("Seed base", seedBase);
                skipExisting = EditorGUILayout.Toggle("Skip existing", skipExisting);
                overrideOffTol = EditorGUILayout.Toggle("Override off-tolerance", overrideOffTol);
                threadCount = Mathf.Clamp(EditorGUILayout.IntField("Threads", threadCount), 1, 64);
                if (config != null)
                {
                    UpdateOffTolCount();
                    EditorGUILayout.LabelField(" ",
                        $"{offTolCount} existing level(s) outside ±{config.matchTol * 100:0}%", EditorStyles.miniLabel);
                }
                using (new EditorGUI.DisabledScope(config == null))
                    if (GUILayout.Button($"Generate levels {from}–{to}"))
                    {
                        GenerateLevels();
                        GUIUtility.ExitGUI();
                    }
                if (GUILayout.Button("Export levels → Resources"))
                    LevelResourcesExporter.Export(OutputFolder, SetName);
            }

            DrawColorsSection();

            graphFoldout = EditorGUILayout.Foldout(graphFoldout, "Graph", true);
            if (!graphFoldout) return;

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(config == null))
            {
                if (GUILayout.Button("Show graph (targets)"))
                {
                    BuildData();
                    GUIUtility.ExitGUI();   // layout pass didn't include the graph rect yet
                }
                if (GUILayout.Button("Show graph (built levels)"))
                {
                    BuildDataFromLevels();
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (weights == null) return;
            var rect = GUILayoutUtility.GetRect(120f, 8000f, 120f, 8000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawGraph(rect);
        }

        // generate → rate → fit into any open slot at a FIXED ±matchTol — no widening,
        // a board that fits nothing is discarded.
        void GenerateLevels()
        {
            from = Mathf.Max(1, from);
            to = Mathf.Max(from, to);

            var open = new System.Collections.Generic.List<CampaignCurveConfig.LevelTarget>();
            int newCount = 0, offTol = 0, overwrite = 0;
            for (int l = from; l <= to; l++)
            {
                var t = config.GetTarget(l);
                var existing = AssetDatabase.LoadAssetAtPath<LevelData>($"{OutputFolder}/{l}.asset");
                // slot is open when: no asset yet · skip-existing is off · or the existing
                // level sits outside its tolerance window and override is on
                if (existing == null) { open.Add(t); newCount++; }
                else if (overrideOffTol && (existing.weight < t.minWeight || existing.weight > t.maxWeight)) { open.Add(t); offTol++; }
                else if (!skipExisting) { open.Add(t); overwrite++; }
            }
            if (open.Count == 0)
            {
                EditorUtility.DisplayDialog("Campaign Builder", "All levels in this range already exist and are inside tolerance.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Generate levels — summary",
                $"Set:  {SetName}\n" +
                $"Range:  {from}–{to}\n\n" +
                $"To build:  {open.Count} level(s)\n" +
                $"   • new (missing):  {newCount}\n" +
                $"   • override off-tolerance:  {offTol}\n" +
                $"   • overwrite existing:  {overwrite}\n\n" +
                $"Tolerance:  ±{config.matchTol * 100:0}% (fixed)\n" +
                $"Threads:  {threadCount}   ·   Seed base:  {seedBase}",
                "Go", "Cancel"))
                return;

            EnsureFolder(OutputFolder);
            var queue = new System.Collections.Concurrent.ConcurrentQueue<(int[] region, int[] cols, int seed, WeightRater.Report rep, int size)>();
            int made = 0;

            // workers: generate + rate off the main thread (TryGenerateRaw and Rate are thread-safe)
            genNextSeed = seedBase - 1;
            genAttempts = 0;
            genStop = false;
            genSizes = SizesOf(open);
            var workers = new System.Threading.Thread[threadCount];
            for (int w = 0; w < workers.Length; w++)
            {
                workers[w] = new System.Threading.Thread(() =>
                {
                    while (!System.Threading.Volatile.Read(ref genStop))
                    {
                        if (queue.Count > 256) { System.Threading.Thread.Sleep(5); continue; }   // backpressure
                        int[] sizes = System.Threading.Volatile.Read(ref genSizes);
                        if (sizes.Length == 0) break;
                        int s = System.Threading.Interlocked.Increment(ref genNextSeed);
                        int size = sizes[(s - seedBase) % sizes.Length];
                        bool ok = LevelGenerator.TryGenerateRaw(size, s, 250, out int[] region, out int[] cols, out _);
                        System.Threading.Interlocked.Increment(ref genAttempts);
                        if (!ok) continue;
                        var rep = WeightRater.Rate(size, region, cols);
                        if (rep.trials > 0) continue;   // guess wall — discarded on the worker
                        queue.Enqueue((region, cols, s, rep, size));
                    }
                }) { IsBackground = true, Name = $"CampaignGen{w}" };
                workers[w].Start();
            }

            try
            {
                AssetDatabase.StartAssetEditing();   // files are still written per level; Unity imports them once at the end
                while (open.Count > 0)
                {
                    long attempts = System.Threading.Interlocked.Read(ref genAttempts);
                    if (EditorUtility.DisplayCancelableProgressBar("Generating campaign levels",
                        $"filled {made} · open {open.Count} · boards {attempts} · threads {threadCount} · tol ±{config.matchTol * 100:0}%",
                        made / (float)(made + open.Count))) break;

                    bool filled = false;
                    while (open.Count > 0 && queue.TryDequeue(out var c))
                    {
                        int slot = FindSlot(open, c.size, c.rep);
                        if (slot >= 0)
                        {
                            SaveLevel(open[slot], c.region, c.cols, c.rep.weight, c.seed);
                            open.RemoveAt(slot);
                            made++;
                            filled = true;
                        }
                        // no fit at ±matchTol → the board is discarded; the window never widens
                    }

                    if (filled) genSizes = SizesOf(open);   // workers switch to the sizes still needed

                    System.Threading.Thread.Sleep(10);   // let workers produce; keep the editor responsive
                }
            }
            finally
            {
                genStop = true;
                foreach (var t in workers) t.Join(2000);
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                offTolCount = -1;   // recount after the build
                RefreshSetStats();
            }
            Debug.Log($"[CampaignBuilder] filled {made} level(s), {open.Count} still open, {System.Threading.Interlocked.Read(ref genAttempts)} boards generated at tol ±{config.matchTol * 100:0}%");
        }

        // ---- colors: find & fix close colors on touching regions --------------------------

        bool colorsFoldout = true;
        float colorMinDist = 0.09f;   // OKLab distance below which two colors read as "the same"
        System.Collections.Generic.List<(int lvl, int ca, int cb, float d)> colorFindings;
        Vector2 colorScroll;

        void DrawColorsSection()
        {
            colorsFoldout = EditorGUILayout.Foldout(colorsFoldout, "Colors", true);
            if (!colorsFoldout) return;

            colorMinDist = EditorGUILayout.Slider("Min color distance", colorMinDist, 0.03f, 0.20f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Find close colors"))
                {
                    FindCloseColors();
                    GUIUtility.ExitGUI();
                }
                using (new EditorGUI.DisabledScope(colorFindings == null || colorFindings.Count == 0))
                    if (GUILayout.Button($"Fix ({(colorFindings != null ? CountDistinctLevels() : 0)} levels)"))
                    {
                        FixCloseColors();
                        GUIUtility.ExitGUI();
                    }
            }

            if (colorFindings == null) return;
            var palette = qp.SORegionsColors.Instance.Colors;
            EditorGUILayout.LabelField($"{colorFindings.Count} close pair(s) in {CountDistinctLevels()} level(s) of {SetName}",
                colorFindings.Count > 0 ? EditorStyles.miniBoldLabel : EditorStyles.miniLabel);
            if (colorFindings.Count == 0) return;
            colorScroll = EditorGUILayout.BeginScrollView(colorScroll, GUILayout.MaxHeight(140));
            int i = 0;
            while (i < colorFindings.Count)   // one row per level, its pairs joined
            {
                int lvl = colorFindings[i].lvl;
                var parts = new System.Collections.Generic.List<string>();
                while (i < colorFindings.Count && colorFindings[i].lvl == lvl)
                {
                    var f = colorFindings[i++];
                    if (parts.Count < 3) parts.Add($"{palette[f.ca].Name}~{palette[f.cb].Name} {f.d:0.00}");
                    else if (parts.Count == 3) parts.Add("…");
                }
                EditorGUILayout.LabelField($"L{lvl}:  {string.Join(" · ", parts)}", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        int CountDistinctLevels()
        {
            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (var f in colorFindings) seen.Add(f.lvl);
            return seen.Count;
        }

        static Vector3 ToOklab(Color c)
        {
            float Lin(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
            float r = Lin(c.r), g = Lin(c.g), b = Lin(c.b);
            float l = Mathf.Pow(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b, 1f / 3f);
            float m = Mathf.Pow(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b, 1f / 3f);
            float s = Mathf.Pow(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b, 1f / 3f);
            return new Vector3(
                0.2104542553f * l + 0.793617785f * m - 0.0040720468f * s,
                1.9779984951f * l - 2.428592205f * m + 0.4505937099f * s,
                0.0259040371f * l + 0.7827717662f * m - 0.808675766f * s);
        }

        static Vector3[] PaletteLab()
        {
            var colors = qp.SORegionsColors.Instance.Colors;
            var lab = new Vector3[colors.Length];
            for (int i = 0; i < colors.Length; i++) lab[i] = ToOklab(colors[i].Color);
            return lab;
        }

        // region adjacency (orthogonal neighbours) as a bool matrix
        static bool[,] Adjacency(LevelData lvl)
        {
            int n = lvl.size;
            var adj = new bool[n, n];
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    int a = lvl.regions[r * n + c];
                    if (c + 1 < n) { int b = lvl.regions[r * n + c + 1]; if (a != b) { adj[a, b] = adj[b, a] = true; } }
                    if (r + 1 < n) { int b = lvl.regions[(r + 1) * n + c]; if (a != b) { adj[a, b] = adj[b, a] = true; } }
                }
            return adj;
        }

        void FindCloseColors()
        {
            var lab = PaletteLab();
            colorFindings = new System.Collections.Generic.List<(int, int, int, float)>();
            if (!System.IO.Directory.Exists(OutputFolder)) return;
            foreach (var file in System.IO.Directory.GetFiles(OutputFolder, "*.asset"))
            {
                if (!int.TryParse(System.IO.Path.GetFileNameWithoutExtension(file), out int l)) continue;
                var lvl = AssetDatabase.LoadAssetAtPath<LevelData>($"{OutputFolder}/{l}.asset");
                if (lvl == null) continue;
                var adj = Adjacency(lvl);
                int n = lvl.size;
                for (int a = 0; a < n; a++)
                    for (int b = a + 1; b < n; b++)
                    {
                        if (!adj[a, b]) continue;
                        int ca = lvl.ColorOf(a), cb = lvl.ColorOf(b);
                        if (ca >= lab.Length || cb >= lab.Length) continue;
                        float d = ca == cb ? 0f : Vector3.Distance(lab[ca], lab[cb]);
                        if (d < colorMinDist) colorFindings.Add((l, ca, cb, d));
                    }
            }
            colorFindings.Sort((x, y) => x.lvl.CompareTo(y.lvl));
            Repaint();
        }

        // Re-colour every offending level: pick distinct palette colors per region so that no
        // touching pair is closer than the threshold. Prefers each region's current color, so
        // only the clashing regions change.
        void FixCloseColors()
        {
            var lab = PaletteLab();
            var levels = new System.Collections.Generic.SortedSet<int>();
            foreach (var f in colorFindings) levels.Add(f.lvl);

            int done = 0, failed = 0;
            foreach (int l in levels)
            {
                var lvl = AssetDatabase.LoadAssetAtPath<LevelData>($"{OutputFolder}/{l}.asset");
                if (lvl == null) continue;
                var adj = Adjacency(lvl);
                int n = lvl.size;
                var current = new int[n];
                for (int i = 0; i < n; i++) current[i] = lvl.ColorOf(i);

                var assign = new int[n];
                for (int i = 0; i < n; i++) assign[i] = -1;
                if (Solve(0, n, adj, lab, current, assign))
                {
                    bool identity = true;
                    for (int i = 0; i < n; i++) if (assign[i] != i) { identity = false; break; }
                    Undo.RecordObject(lvl, "Fix close colors");
                    lvl.regionColors = identity ? null : assign;
                    EditorUtility.SetDirty(lvl);
                    done++;
                }
                else failed++;   // no assignment exists at this threshold — lower the slider a bit
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[CampaignBuilder] colors fixed on {done} level(s), {failed} unsolvable at dE {colorMinDist:0.00}");
            FindCloseColors();   // refresh the list — should be empty now
        }

        // Backtracking: distinct colors per region, adjacent regions at least colorMinDist apart.
        // Candidate order tries the region's current color first (minimal visual change).
        bool Solve(int region, int n, bool[,] adj, Vector3[] lab, int[] current, int[] assign)
        {
            if (region == n) return true;
            var candidates = new System.Collections.Generic.List<int> { current[region] };
            for (int c = 0; c < lab.Length; c++) if (c != current[region]) candidates.Add(c);

            foreach (int c in candidates)
            {
                bool ok = true;
                for (int other = 0; other < n && ok; other++)
                {
                    if (assign[other] < 0 || other == region) continue;
                    if (assign[other] == c) ok = false;   // colors stay distinct on one board
                    else if (adj[region, other] && Vector3.Distance(lab[c], lab[assign[other]]) < colorMinDist) ok = false;
                }
                if (!ok) continue;
                assign[region] = c;
                if (Solve(region + 1, n, adj, lab, current, assign)) return true;
                assign[region] = -1;
            }
            return false;
        }

        // Existing levels in range whose weight sits outside their slot's tolerance window.
        // Cached — recounts only when range/set/config change, on window focus, or after a build.
        void UpdateOffTolCount()
        {
            var key = (from, to, setIdx, (Object)config);
            if (offTolCount >= 0 && key == offTolKey) return;
            offTolKey = key;
            offTolCount = 0;
            for (int l = from; l <= to; l++)
            {
                var lvl = AssetDatabase.LoadAssetAtPath<LevelData>($"{OutputFolder}/{l}.asset");
                if (lvl == null) continue;
                var t = config.GetTarget(l);
                if (lvl.weight < t.minWeight || lvl.weight > t.maxWeight) offTolCount++;
            }
        }

        static int[] SizesOf(System.Collections.Generic.List<CampaignCurveConfig.LevelTarget> open)
        {
            var sizes = new int[open.Count];
            for (int i = 0; i < open.Count; i++) sizes[i] = open[i].boardSize;
            return sizes;
        }

        // Rarest fit wins (milestone > peak > build > normal > breather) so easy slots
        // don't eat the rare heavy boards.
        int FindSlot(System.Collections.Generic.List<CampaignCurveConfig.LevelTarget> open, int size,
            in WeightRater.Report rep)
        {
            int best = -1, bestPrio = -1;
            for (int i = 0; i < open.Count; i++)
            {
                var t = open[i];
                if (t.boardSize != size) continue;
                if (!CampaignCurve.PassesGates(config, t, rep)) continue;
                int prio = (int)t.role;
                if (prio > bestPrio) { bestPrio = prio; best = i; }
            }
            return best;
        }

        void SaveLevel(in CampaignCurveConfig.LevelTarget t, int[] region, int[] cols, int weight, int seed)
        {
            var data = ScriptableObject.CreateInstance<LevelData>();
            data.size = t.boardSize;
            data.regions = region;
            data.solutionColumns = cols;
            data.weight = weight;
            data.seed = seed;
            AssetDatabase.CreateAsset(data, $"{OutputFolder}/{t.level}.asset");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string[] parts = path.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        // Graph the actual weights of the built level assets in the output folder.
        // Missing levels show as 0 (gaps on the floor).
        void BuildDataFromLevels()
        {
            from = Mathf.Max(1, from);
            to = Mathf.Clamp(to, from, from + MaxRange - 1);
            int n = to - from + 1;
            weights = new int[n];
            roles = new byte[n];
            dataFrom = from;
            maxW = Mathf.Max(1, config.weightCap);
            for (int i = 0; i < n; i++)
            {
                var lvl = AssetDatabase.LoadAssetAtPath<LevelData>($"{OutputFolder}/{from + i}.asset");
                weights[i] = lvl != null ? lvl.weight : 0;
                roles[i] = (byte)(config.milestoneEvery > 0 && (from + i) % config.milestoneEvery == 0
                    ? CampaignCurveConfig.Role.Milestone : CampaignCurveConfig.Role.Normal);
                if (weights[i] > maxW) maxW = weights[i];
            }
            line = null;
            Repaint();
        }

        void BuildData()
        {
            from = Mathf.Max(1, from);
            to = Mathf.Clamp(to, from, from + MaxRange - 1);
            int n = to - from + 1;
            weights = new int[n];
            roles = new byte[n];
            dataFrom = from;
            maxW = Mathf.Max(1, config.weightCap);
            for (int i = 0; i < n; i++)
            {
                var t = config.GetTarget(from + i);
                weights[i] = t.targetWeight;
                roles[i] = (byte)t.role;
                if (t.targetWeight > maxW) maxW = t.targetWeight;
            }
            line = null;   // invalidate screen cache
            Repaint();
        }

        void DrawGraph(Rect r)
        {
            if (Event.current.type != EventType.Repaint) return;

            const float padL = 44, padR = 8, padT = 8, padB = 20;
            var plot = new Rect(r.x + padL, r.y + padT, r.width - padL - padR, r.height - padT - padB);
            if (plot.width < 20 || plot.height < 20) return;
            if (line == null || r != cachedRect) RebuildScreenCache(plot, r);

            bool pro = EditorGUIUtility.isProSkin;
            var ink = pro ? new Color(0.75f, 0.75f, 0.73f) : new Color(0.33f, 0.33f, 0.31f);
            var grid = pro ? new Color(1f, 1f, 1f, 0.07f) : new Color(0f, 0f, 0f, 0.08f);
            var blue = pro ? new Color(0.22f, 0.53f, 0.90f) : new Color(0.16f, 0.47f, 0.84f);
            var red = pro ? new Color(0.90f, 0.40f, 0.40f) : new Color(0.82f, 0.23f, 0.23f);

            EditorGUI.DrawRect(plot, pro ? new Color(0, 0, 0, 0.18f) : new Color(1, 1, 1, 0.5f));

            // gridlines + labels (GUI, repaint-safe)
            var label = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
            label.normal.textColor = ink;
            int step = NiceStep(maxW, 5);
            for (int v = 0; v <= maxW; v += step)
            {
                float y = plot.yMax - v / (float)maxW * plot.height;
                EditorGUI.DrawRect(new Rect(plot.xMin, y, plot.width, 1f), grid);
                GUI.Label(new Rect(r.x, y - 8, padL - 6, 16), v.ToString(), label);
            }

            // cap line (weightCap = difficulty 1.0)
            float capY = plot.yMax - config.weightCap / (float)maxW * plot.height;
            EditorGUI.DrawRect(new Rect(plot.xMin, capY, plot.width, 1f), new Color(red.r, red.g, red.b, 0.55f));

            // curve + milestone dots via GL
            if (!lineMat)
                lineMat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            GUI.BeginClip(new Rect(0, 0, position.width, position.height));
            lineMat.SetPass(0);
            GL.PushMatrix();

            GL.Begin(GL.LINE_STRIP);
            GL.Color(blue);
            for (int i = 0; i < line.Length; i++) GL.Vertex(line[i]);
            GL.End();

            GL.Begin(GL.QUADS);
            GL.Color(red);
            const float s = 3.5f;
            for (int i = 0; i < milestones.Length; i++)
            {
                var p = milestones[i];
                GL.Vertex3(p.x - s, p.y - s, 0);
                GL.Vertex3(p.x + s, p.y - s, 0);
                GL.Vertex3(p.x + s, p.y + s, 0);
                GL.Vertex3(p.x - s, p.y + s, 0);
            }
            GL.End();

            GL.PopMatrix();
            GUI.EndClip();

            // x labels
            var xlab = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter };
            xlab.normal.textColor = ink;
            int count = weights.Length;
            int ticks = Mathf.Min(6, count);
            for (int i = 0; i < ticks; i++)
            {
                int lvl = dataFrom + (int)((count - 1) * i / (float)Mathf.Max(1, ticks - 1));
                float x = plot.xMin + (lvl - dataFrom) / (float)Mathf.Max(1, count - 1) * plot.width;
                GUI.Label(new Rect(x - 24, plot.yMax + 2, 48, 16), lvl.ToString(), xlab);
            }
        }

        // Two points per pixel column (max then min) — spikes and breathers both survive
        // decimation, and the polyline stays ≤ 2 * plot.width points no matter the range.
        void RebuildScreenCache(Rect plot, Rect full)
        {
            cachedRect = full;
            int n = weights.Length;
            int cols = Mathf.Max(1, (int)plot.width);
            var pts = new System.Collections.Generic.List<Vector3>(Mathf.Min(n, cols) * 2);
            var miles = new System.Collections.Generic.List<Vector3>();

            if (n <= cols)
            {
                for (int i = 0; i < n; i++)
                    pts.Add(ToScreen(plot, i, n, weights[i]));
            }
            else
            {
                for (int c = 0; c < cols; c++)
                {
                    int i0 = (int)(c * (long)n / cols), i1 = (int)((c + 1) * (long)n / cols);
                    if (i1 <= i0) i1 = i0 + 1;
                    int lo = int.MaxValue, hi = int.MinValue;
                    for (int i = i0; i < i1; i++)
                    {
                        if (weights[i] < lo) lo = weights[i];
                        if (weights[i] > hi) hi = weights[i];
                    }
                    pts.Add(ToScreen(plot, i0, n, hi));
                    if (lo != hi) pts.Add(ToScreen(plot, i0, n, lo));
                }
            }
            for (int i = 0; i < n; i++)
                if (roles[i] == (byte)CampaignCurveConfig.Role.Milestone)
                    miles.Add(ToScreen(plot, i, n, weights[i]));

            line = pts.ToArray();
            milestones = miles.ToArray();
        }

        Vector3 ToScreen(Rect plot, int i, int n, int w) => new Vector3(
            plot.xMin + i / (float)Mathf.Max(1, n - 1) * plot.width,
            plot.yMax - w / (float)maxW * plot.height,
            0f);

        static int NiceStep(int max, int ticks)
        {
            int raw = Mathf.Max(1, max / Mathf.Max(1, ticks));
            int mag = Mathf.Max(1, (int)Mathf.Pow(10, Mathf.FloorToInt(Mathf.Log10(raw))));
            foreach (float m in new[] { 1f, 2f, 2.5f, 5f, 10f })
                if (raw <= m * mag) return Mathf.Max(1, (int)(m * mag));
            return Mathf.Max(1, 10 * mag);
        }
    }
}
