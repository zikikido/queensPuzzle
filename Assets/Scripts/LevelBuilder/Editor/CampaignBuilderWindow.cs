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
