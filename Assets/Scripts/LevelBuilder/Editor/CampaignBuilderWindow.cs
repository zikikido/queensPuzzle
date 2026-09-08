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

        // [SerializeField] on the settings fields — plain fields are wiped by every domain reload
        // (any script compile), which is what kept snapping the range back to 1–600.
        [SerializeField] CampaignCurveConfig config;
        [SerializeField] int from = 1;
        [SerializeField] int to = 600;
        bool graphFoldout = true;
        bool buildFoldout = true;
        const string SetsRoot = "Assets/Levels/Sets";
        string[] sets = { "Campaign" };
        [SerializeField] string setName = "Campaign";
        int setIdx;
        string SetName => setName;
        string OutputFolder => $"{SetsRoot}/{SetName}";
        [SerializeField] int seedBase = 1;
        [SerializeField] bool skipExisting = true;
        int setCount;    // consecutive levels from 1 in the selected set
        bool setGap;     // a level exists past the run → hole in the sequence
        [SerializeField] bool overrideOffTol = true;
        int offTolCount = -1;                       // -1 = needs recount
        (int, int, int, Object) offTolKey;          // (from, to, setIdx, config) the count was made for
        int threadCount = Mathf.Max(1, System.Environment.ProcessorCount - 1);

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

        void OnEnable() { wantsMouseMove = true; RefreshSets(); }
        void OnFocus() { RefreshSets(); offTolCount = -1; }   // catches set/level changes done outside the window

        void RefreshSets()
        {
            var found = new System.Collections.Generic.List<string>();
            if (System.IO.Directory.Exists(SetsRoot))
                foreach (var d in System.IO.Directory.GetDirectories(SetsRoot))
                    found.Add(System.IO.Path.GetFileName(d));
            found.Sort(System.StringComparer.OrdinalIgnoreCase);
            if (found.Count == 0) found.Add("Campaign");   // created on first generate
            sets = found.ToArray();
            setIdx = Mathf.Max(0, System.Array.IndexOf(sets, setName));
            setName = sets[setIdx];
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
                if (EditorGUI.EndChangeCheck()) { setName = sets[Mathf.Clamp(setIdx, 0, sets.Length - 1)]; RefreshSetStats(); offTolCount = -1; }
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
            using (new EditorGUI.DisabledScope(config == null))   // targets need a curve
                if (GUILayout.Button("Show graph (targets)"))
                {
                    BuildData();
                    GUIUtility.ExitGUI();   // layout pass didn't include the graph rect yet
                }
            // built levels are read off the assets — no curve needed
            if (GUILayout.Button("Show graph (built levels)"))
            {
                BuildDataFromLevels();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            if (weights == null) return;
            var rect = GUILayoutUtility.GetRect(120f, 8000f, 120f, 8000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawGraph(rect);
        }

        // generate → rate → fit into any open slot at a FIXED ±matchTol — no widening,
        // a board that fits nothing is discarded. The build core lives in PoolBuildRunner.
        void GenerateLevels()
        {
            from = Mathf.Max(1, from);
            to = Mathf.Max(from, to);

            var targets = new System.Collections.Generic.List<CampaignCurveConfig.LevelTarget>(to - from + 1);
            for (int l = from; l <= to; l++) targets.Add(config.GetTarget(l));
            var scan = PoolBuildRunner.Scan(targets, OutputFolder, skipExisting, overrideOffTol);
            if (scan.open.Count == 0)
            {
                EditorUtility.DisplayDialog("Campaign Builder", "All levels in this range already exist and are inside tolerance.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Generate levels — summary",
                $"Set:  {SetName}\n" +
                $"Range:  {from}–{to}\n\n" +
                $"To build:  {scan.open.Count} level(s)\n" +
                $"   • new (missing):  {scan.newCount}\n" +
                $"   • override off-tolerance:  {scan.offTol}\n" +
                $"   • overwrite existing:  {scan.overwrite}\n\n" +
                $"Tolerance:  ±{config.matchTol * 100:0}% (fixed)\n" +
                $"Threads:  {threadCount}   ·   Seed base:  {seedBase}",
                "Go", "Cancel"))
                return;

            var (made, attempts) = PoolBuildRunner.Run(config.Gates, scan.open, OutputFolder,
                new PoolBuildRunner.Options
                {
                    threadCount = threadCount,
                    seedBase = seedBase,
                    title = "Generating campaign levels",
                    tolLabel = $"tol ±{config.matchTol * 100:0}%",
                });

            offTolCount = -1;   // recount after the build
            RefreshSetStats();
            Debug.Log($"[CampaignBuilder] filled {made} level(s), {scan.open.Count} still open, {attempts} boards generated at tol ±{config.matchTol * 100:0}%");
        }

        // ---- colors: find & fix confusable region colors ----------------------------------
        //
        // Two colours can confuse a player in two different ways, and each needs its own rule:
        //  - physically alike (OKLab distance < colorMinDist): the eye can't tell them apart at
        //    all, so they must never share a board;
        //  - same colour *family* (shared XKCD name, or hue angles too close): easy to tell apart
        //    side by side, but the player identifies regions categorically — "the pink one" — so
        //    a light pink and a dark pink still collide. Such pairs may share a board but must
        //    never touch. The two family tests cover each other's blind spots: names miss pairs
        //    straddling a naming boundary, hue misses pairs people collapse into one name.

        bool colorsFoldout = true;
        float colorMinDist = 0.09f;   // OKLab distance below which two colors read as "the same"
        float nameMinDist = 0.20f;    // XKCD name distance below which two colors share a name
        float hueMinDeg = 18f;        // hue angle difference below which two colors share a family
        System.Collections.Generic.List<(int lvl, string desc)> colorFindings;
        string colorFindingsTitle;
        Vector2 colorScroll;

        // XKCD colour-survey name distance between every SORegionsColors pair: 0 = people give
        // both colours the same name, 1 = never the same name. Computed offline from the C3
        // dataset (Heer & Stone) — recompute whenever a palette colour changes.
        static readonly float[,] nameDist =
        {
            { 0.000f, 1.000f, 1.000f, 1.000f, 0.995f, 0.946f, 1.000f, 1.000f, 0.944f, 1.000f, 1.000f, 1.000f, 0.991f, 1.000f, 1.000f, 0.011f, 0.999f },
            { 1.000f, 0.000f, 0.984f, 0.943f, 0.995f, 0.999f, 0.941f, 0.794f, 1.000f, 0.903f, 0.976f, 0.636f, 1.000f, 0.326f, 0.893f, 1.000f, 0.991f },
            { 1.000f, 0.984f, 0.000f, 1.000f, 0.625f, 1.000f, 1.000f, 0.128f, 0.990f, 1.000f, 0.158f, 0.158f, 0.942f, 1.000f, 0.259f, 1.000f, 0.095f },
            { 1.000f, 0.943f, 1.000f, 0.000f, 1.000f, 0.930f, 0.029f, 1.000f, 1.000f, 0.043f, 1.000f, 1.000f, 1.000f, 0.306f, 1.000f, 1.000f, 1.000f },
            { 0.995f, 0.995f, 0.625f, 1.000f, 0.000f, 1.000f, 1.000f, 0.847f, 0.547f, 1.000f, 0.643f, 0.834f, 0.354f, 1.000f, 0.804f, 0.989f, 0.439f },
            { 0.946f, 0.999f, 1.000f, 0.930f, 1.000f, 0.000f, 0.947f, 1.000f, 0.996f, 0.975f, 1.000f, 1.000f, 1.000f, 0.982f, 1.000f, 0.950f, 1.000f },
            { 1.000f, 0.941f, 1.000f, 0.029f, 1.000f, 0.947f, 0.000f, 1.000f, 1.000f, 0.015f, 1.000f, 0.999f, 1.000f, 0.290f, 1.000f, 0.999f, 1.000f },
            { 1.000f, 0.794f, 0.128f, 1.000f, 0.847f, 1.000f, 1.000f, 0.000f, 1.000f, 0.993f, 0.379f, 0.052f, 0.997f, 0.902f, 0.423f, 1.000f, 0.242f },
            { 0.944f, 1.000f, 0.990f, 1.000f, 0.547f, 0.996f, 1.000f, 1.000f, 0.000f, 1.000f, 0.994f, 1.000f, 0.182f, 1.000f, 1.000f, 0.918f, 0.918f },
            { 1.000f, 0.903f, 1.000f, 0.043f, 1.000f, 0.975f, 0.015f, 0.993f, 1.000f, 0.000f, 1.000f, 0.987f, 1.000f, 0.248f, 0.997f, 1.000f, 1.000f },
            { 1.000f, 0.976f, 0.158f, 1.000f, 0.643f, 1.000f, 1.000f, 0.379f, 0.994f, 1.000f, 0.000f, 0.307f, 0.947f, 1.000f, 0.036f, 1.000f, 0.352f },
            { 1.000f, 0.636f, 0.158f, 1.000f, 0.834f, 1.000f, 0.999f, 0.052f, 1.000f, 0.987f, 0.307f, 0.000f, 0.996f, 0.813f, 0.309f, 1.000f, 0.291f },
            { 0.991f, 1.000f, 0.942f, 1.000f, 0.354f, 1.000f, 1.000f, 0.997f, 0.182f, 1.000f, 0.947f, 0.996f, 0.000f, 1.000f, 0.992f, 0.978f, 0.850f },
            { 1.000f, 0.326f, 1.000f, 0.306f, 1.000f, 0.982f, 0.290f, 0.902f, 1.000f, 0.248f, 1.000f, 0.813f, 1.000f, 0.000f, 0.949f, 1.000f, 1.000f },
            { 1.000f, 0.893f, 0.259f, 1.000f, 0.804f, 1.000f, 1.000f, 0.423f, 1.000f, 0.997f, 0.036f, 0.309f, 0.992f, 0.949f, 0.000f, 1.000f, 0.502f },
            { 0.011f, 1.000f, 1.000f, 1.000f, 0.989f, 0.950f, 0.999f, 1.000f, 0.918f, 1.000f, 1.000f, 1.000f, 0.978f, 1.000f, 1.000f, 0.000f, 0.998f },
            { 0.999f, 0.991f, 0.095f, 1.000f, 0.439f, 1.000f, 1.000f, 0.242f, 0.918f, 1.000f, 0.352f, 0.291f, 0.850f, 1.000f, 0.502f, 0.998f, 0.000f },
        };

        // Pairs both family tests call "far" but a human eye confirmed as confusable — the veto
        // list of last resort, edited in the window and persisted in EditorPrefs. Ships with
        // magenta #EA73FA ~ salmon #FF808E and purple #B185CF ~ periwinkle #9DAFFF.
        const string ByEyePrefsKey = "CampaignBuilder.forbiddenByEye";
        System.Collections.Generic.List<Vector2Int> forbiddenByEye;
        int byEyeA, byEyeB;

        // Project-level pairs; the prefs list only ever adds on top of these, so removing one
        // for good means removing it here.
        static readonly Vector2Int[] shippedByEye =
        {
            new Vector2Int(3, 13),   // magenta ~ salmon
            new Vector2Int(1, 11),   // purple ~ periwinkle
            new Vector2Int(10, 12),  // muted blue ~ muted green
            new Vector2Int(4, 8),    // mint ~ light green
            new Vector2Int(4, 16),   // mint ~ pale cyan
        };

        System.Collections.Generic.List<Vector2Int> ByEyePairs()
        {
            if (forbiddenByEye != null) return forbiddenByEye;
            forbiddenByEye = new System.Collections.Generic.List<Vector2Int>(shippedByEye);
            foreach (var tok in EditorPrefs.GetString(ByEyePrefsKey, "")
                         .Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                var ab = tok.Split('~');
                if (ab.Length == 2 && int.TryParse(ab[0], out int a) && int.TryParse(ab[1], out int b)
                    && !forbiddenByEye.Contains(new Vector2Int(a, b)))
                    forbiddenByEye.Add(new Vector2Int(a, b));
            }
            return forbiddenByEye;
        }

        void SaveByEyePairs()
        {
            var parts = new string[forbiddenByEye.Count];
            for (int i = 0; i < parts.Length; i++) parts[i] = $"{forbiddenByEye[i].x}~{forbiddenByEye[i].y}";
            EditorPrefs.SetString(ByEyePrefsKey, parts.Length == 0 ? "none" : string.Join(";", parts));
        }

        // Assumes a saturated palette: a near-grey colour has no meaningful hue angle, so the
        // hue test would need a chroma guard if a pale colour ever joins the palette.
        bool ForbiddenNeighbor(int a, int b, Vector3[] lab)
        {
            if (a >= nameDist.GetLength(0) || b >= nameDist.GetLength(0)) return false;
            if (nameDist[a, b] < nameMinDist) return true;
            if (Mathf.Abs(Mathf.DeltaAngle(HueDeg(lab[a]), HueDeg(lab[b]))) < hueMinDeg) return true;
            foreach (var p in ByEyePairs())
                if ((p.x == a && p.y == b) || (p.x == b && p.y == a)) return true;
            return false;
        }

        static float HueDeg(Vector3 lab) => Mathf.Atan2(lab.z, lab.y) * Mathf.Rad2Deg;

        void DrawColorsSection()
        {
            colorsFoldout = EditorGUILayout.Foldout(colorsFoldout, "Colors", true);
            if (!colorsFoldout) return;

            colorMinDist = EditorGUILayout.Slider("Min OKLab distance", colorMinDist, 0.03f, 0.20f);
            nameMinDist = EditorGUILayout.Slider("Min name distance", nameMinDist, 0.03f, 0.40f);
            hueMinDeg = EditorGUILayout.Slider("Min hue diff (deg)", hueMinDeg, 5f, 45f);

            // the human veto list — forbidden as neighbours no matter what the sliders say
            EditorGUILayout.LabelField("Forbidden by eye", EditorStyles.miniBoldLabel);
            var pal = qp.SORegionsColors.Instance.Colors;
            var byEye = ByEyePairs();
            for (int p = 0; p < byEye.Count; p++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var pr = byEye[p];
                    var rect = GUILayoutUtility.GetRect(36, 16, GUILayout.Width(36));
                    if (pr.x < pal.Length && pr.y < pal.Length)
                    {
                        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 16, 16), pal[pr.x].Color);
                        EditorGUI.DrawRect(new Rect(rect.x + 18, rect.y, 16, 16), pal[pr.y].Color);
                        EditorGUILayout.LabelField($"{pr.x}~{pr.y}  {pal[pr.x].Name}~{pal[pr.y].Name}", EditorStyles.miniLabel);
                    }
                    else EditorGUILayout.LabelField($"{pr.x}~{pr.y}  (outside palette)", EditorStyles.miniLabel);
                    if (GUILayout.Button("✕", GUILayout.Width(22))) { byEye.RemoveAt(p--); SaveByEyePairs(); }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                var names = new string[pal.Length];
                for (int p = 0; p < pal.Length; p++) names[p] = $"{p} {pal[p].Name}";
                byEyeA = EditorGUILayout.Popup(byEyeA, names);
                byEyeB = EditorGUILayout.Popup(byEyeB, names);
                if (GUILayout.Button("Add pair", GUILayout.Width(70)) && byEyeA != byEyeB)
                {
                    var pr = new Vector2Int(Mathf.Min(byEyeA, byEyeB), Mathf.Max(byEyeA, byEyeB));
                    if (!byEye.Contains(pr)) { byEye.Add(pr); SaveByEyePairs(); }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Find close colors (OKLab)"))
                {
                    FindColorIssues(global: true, neighbors: false);
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("Find forbidden neighbors (XKCD + hue)"))
                {
                    FindColorIssues(global: false, neighbors: true);
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("Recolor all levels"))
                {
                    RecolorAllLevels();
                    GUIUtility.ExitGUI();
                }
            }

            if (colorFindings == null) return;
            int violations = 0;
            foreach (var f in colorFindings) if (f.lvl > 0) violations++;
            EditorGUILayout.LabelField(
                $"{violations} {colorFindingsTitle} in {CountDistinctLevels()} level(s) of {SetName}" +
                (colorFindings.Count > violations ? $"  +{colorFindings.Count - violations} borderline" : ""),
                violations > 0 ? EditorStyles.miniBoldLabel : EditorStyles.miniLabel);
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
                    if (parts.Count < 3) parts.Add(f.desc);
                    else if (parts.Count == 3) parts.Add("…");
                }
                EditorGUILayout.LabelField(
                    lvl > 0 ? $"L{lvl}:  {string.Join(" · ", parts)}" : string.Join(" · ", parts),
                    EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        int CountDistinctLevels()
        {
            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (var f in colorFindings) if (f.lvl > 0) seen.Add(f.lvl);
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

        // global: any two colours on the board closer than colorMinDist in OKLab — such a pair
        // must not share a board at all. neighbors: touching regions whose colours share a name
        // per forbiddenNeighbors (or are the same colour outright).
        void FindColorIssues(bool global, bool neighbors)
        {
            var lab = PaletteLab();
            var palette = qp.SORegionsColors.Instance.Colors;
            colorFindings = new System.Collections.Generic.List<(int, string)>();
            colorFindingsTitle = global && neighbors ? "color issue(s)"
                : global ? "close pair(s)" : "forbidden neighbor pair(s)";
            // the neighbors button also reports near-threshold pairs, with the levels they touch in
            var border = neighbors && !global ? BorderlinePairs(lab) : null;
            var borderHits = new System.Collections.Generic.Dictionary<(int, int), System.Collections.Generic.List<string>>();
            if (!System.IO.Directory.Exists(OutputFolder)) return;
            foreach (var file in System.IO.Directory.GetFiles(OutputFolder, "*.asset"))
            {
                if (!int.TryParse(System.IO.Path.GetFileNameWithoutExtension(file), out int l)) continue;
                var lvl = AssetDatabase.LoadAssetAtPath<LevelData>($"{OutputFolder}/{l}.asset");
                if (lvl == null) continue;
                int n = lvl.size;
                var adj = neighbors ? Adjacency(lvl) : null;
                for (int a = 0; a < n; a++)
                    for (int b = a + 1; b < n; b++)
                    {
                        int ca = lvl.ColorOf(a), cb = lvl.ColorOf(b);
                        if (ca >= lab.Length || cb >= lab.Length) continue;
                        if (global)
                        {
                            float d = ca == cb ? 0f : Vector3.Distance(lab[ca], lab[cb]);
                            if (d < colorMinDist)
                                colorFindings.Add((l, $"{palette[ca].Name}~{palette[cb].Name} {d:0.00}"));
                        }
                        if (neighbors && adj[a, b])
                        {
                            if (ca == cb || ForbiddenNeighbor(ca, cb, lab))
                                colorFindings.Add((l, $"{(char)('A' + a)}~{(char)('A' + b)} {palette[ca].Name}~{palette[cb].Name}"));
                            else if (border != null && border.ContainsKey((Mathf.Min(ca, cb), Mathf.Max(ca, cb))))
                            {
                                var key = (Mathf.Min(ca, cb), Mathf.Max(ca, cb));
                                if (!borderHits.TryGetValue(key, out var hits)) borderHits[key] = hits = new System.Collections.Generic.List<string>();
                                if (hits.Count < 3) hits.Add($"L{l} {(char)('A' + a)}~{(char)('A' + b)}");
                            }
                        }
                    }
            }
            colorFindings.Sort((x, y) => x.lvl.CompareTo(y.lvl));
            if (border != null)
            {
                int row = 0;
                foreach (var kv in border)
                {
                    string where = borderHits.TryGetValue(kv.Key, out var hits)
                        ? string.Join(", ", hits) : "not adjacent in this set";
                    colorFindings.Add((--row, $"borderline  {kv.Value}   ({where})"));
                }
            }
            Repaint();
        }

        // Recolour levels that violate either rule: a pair anywhere on the board closer than
        // colorMinDist in OKLab, or a forbidden family pair on touching regions. Clean levels
        // keep the colours players already saw. Same-name pairs may share a board — they just
        // never touch — which leaves far more palette slack for big boards than the old
        // everything-far-from-everything pass.
        void RecolorAllLevels()
        {
            if (!System.IO.Directory.Exists(OutputFolder)) return;
            var files = System.IO.Directory.GetFiles(OutputFolder, "*.asset");
            if (!EditorUtility.DisplayDialog("Recolor all levels",
                    $"Rewrite region colours on violating level(s) of {files.Length} in {SetName}?\n\n" +
                    $"Only boards that break the rules are touched: colours at least {colorMinDist:0.00} apart,\n" +
                    "and same-name colours never touch.\n" +
                    "Re-export the level pack afterwards.", "Recolor", "Cancel"))
                return;

            var lab = PaletteLab();
            int done = 0, failed = 0, skipped = 0;
            try
            {
                for (int i = 0; i < files.Length; i++)
                {
                    if (!int.TryParse(System.IO.Path.GetFileNameWithoutExtension(files[i]), out int l)) continue;
                    if (EditorUtility.DisplayCancelableProgressBar("Recoloring",
                            $"level {l}  ({done} done, {skipped} clean, {failed} failed)", (float)i / files.Length))
                        break;

                    var lvl = AssetDatabase.LoadAssetAtPath<LevelData>($"{OutputFolder}/{l}.asset");
                    if (lvl == null) continue;

                    var adj = Adjacency(lvl);
                    if (!HasColorIssues(lvl, lab, adj)) { skipped++; continue; }   // clean — keep shipped colours

                    var assign = PickSpreadColors(lvl.size, l, lab, adj);
                    if (assign == null) { failed++; continue; }   // threshold too high for this size

                    bool identity = true;
                    for (int r = 0; r < lvl.size; r++) if (assign[r] != r) { identity = false; break; }
                    Undo.RecordObject(lvl, "Recolor level");
                    lvl.regionColors = identity ? null : assign;
                    EditorUtility.SetDirty(lvl);
                    done++;
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            AssetDatabase.SaveAssets();
            Debug.Log($"[CampaignBuilder] recoloured {done} level(s), {skipped} already clean, {failed} unsolvable at {colorMinDist:0.00}. " +
                      "Re-export the level pack to ship it.");
            FindColorIssues(global: true, neighbors: true);   // should come back empty
        }

        // Palette pairs that PASS every rule but sit within a safety margin of a threshold.
        // The rules are step functions over a continuous perception, so a pair this close to
        // a line is "unchecked", not "clean" — review by eye and veto via the by-eye list.
        System.Collections.Generic.Dictionary<(int, int), string> BorderlinePairs(Vector3[] lab)
        {
            var palette = qp.SORegionsColors.Instance.Colors;
            var border = new System.Collections.Generic.Dictionary<(int, int), string>();
            for (int a = 0; a < lab.Length; a++)
                for (int b = a + 1; b < lab.Length; b++)
                {
                    float ok = Vector3.Distance(lab[a], lab[b]);
                    if (ok < colorMinDist || ForbiddenNeighbor(a, b, lab)) continue;   // already caught
                    float nm = a < nameDist.GetLength(0) && b < nameDist.GetLength(0) ? nameDist[a, b] : 1f;
                    float hd = Mathf.Abs(Mathf.DeltaAngle(HueDeg(lab[a]), HueDeg(lab[b])));
                    if (ok >= colorMinDist + 0.03f && nm >= nameMinDist + 0.05f && hd >= hueMinDeg + 4f) continue;
                    border[(a, b)] = $"{a}~{b} {palette[a].Name}~{palette[b].Name}  OKLab {ok:0.00} · name {nm:0.00} · hue {hd:0}°";
                }
            return border;
        }

        // True when the board breaks either rule — a close (or identical) pair anywhere,
        // or a forbidden family pair touching.
        bool HasColorIssues(LevelData lvl, Vector3[] lab, bool[,] adj)
        {
            int n = lvl.size;
            for (int a = 0; a < n; a++)
                for (int b = a + 1; b < n; b++)
                {
                    int ca = lvl.ColorOf(a), cb = lvl.ColorOf(b);
                    if (ca >= lab.Length || cb >= lab.Length) continue;
                    if (ca == cb || Vector3.Distance(lab[ca], lab[cb]) < colorMinDist) return true;
                    if (adj[a, b] && ForbiddenNeighbor(ca, cb, lab)) return true;
                }
            return false;
        }

        /// <summary>
        /// Region→colour assignment: n palette colours that are far apart AND look like a set,
        /// arranged so no forbiddenNeighbors pair lands on touching regions. Seeded by level
        /// number, so a level always gets the same colours while neighbours get different ones.
        ///
        /// Distance alone is not enough: a set can be perfectly distinguishable and still ugly —
        /// hues bunched on one side of the wheel, or a pale cream sitting next to a dark green.
        /// So we generate many valid sets and keep the one that also reads as balanced.
        /// </summary>
        int[] PickSpreadColors(int n, int seed, Vector3[] lab, bool[,] adj)
        {
            if (n > lab.Length) return null;
            var rng = new System.Random(seed);
            int[] best = null;
            float bestScore = float.NegativeInfinity;

            for (int attempt = 0; attempt < 600; attempt++)
            {
                var set = TrySpreadSet(n, rng, lab);
                if (set == null) continue;
                float s = HarmonyScore(set, lab);
                if (s <= bestScore) continue;
                var assign = ArrangeOnRegions(set, adj, lab);
                if (assign == null) continue;   // no arrangement keeps forbidden pairs apart
                bestScore = s; best = assign;
                if (attempt > 250) break;   // enough candidates seen
            }
            return best;   // null => no spread set at this threshold, lower the slider
        }

        /// One level under the same rules and default thresholds as the campaign pass —
        /// LevelBuilderWindow's per-level Recolor button. Null when no colour set fits.
        public static int[] RecolorSingle(LevelData lvl, int seed)
        {
            var win = CreateInstance<CampaignBuilderWindow>();
            try { return win.PickSpreadColors(lvl.size, seed, PaletteLab(), Adjacency(lvl)); }
            finally { DestroyImmediate(win); }
        }

        /// Place the picked colours on the regions so no forbidden pair touches.
        /// Exhaustive backtracking — null only when no permutation works for this set.
        int[] ArrangeOnRegions(int[] set, bool[,] adj, Vector3[] lab)
        {
            int n = set.Length;
            var assign = new int[n];
            var used = new bool[n];

            bool Place(int region)
            {
                if (region == n) return true;
                for (int i = 0; i < n; i++)
                {
                    if (used[i]) continue;
                    bool ok = true;
                    for (int r = 0; r < region && ok; r++)
                        if (adj[region, r] && ForbiddenNeighbor(set[i], assign[r], lab)) ok = false;
                    if (!ok) continue;
                    used[i] = true; assign[region] = set[i];
                    if (Place(region + 1)) return true;
                    used[i] = false;
                }
                return false;
            }

            return Place(0) ? assign : null;
        }

        /// One greedy attempt: shuffle the palette, take colours that clear colorMinDist.
        int[] TrySpreadSet(int n, System.Random rng, Vector3[] lab)
        {
            var order = new int[lab.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            for (int i = order.Length - 1; i > 0; i--)   // Fisher-Yates
            {
                int j = rng.Next(i + 1);
                int tmp = order[i]; order[i] = order[j]; order[j] = tmp;
            }

            var picked = new int[n];
            int count = 0;
            foreach (int c in order)
            {
                bool ok = true;
                for (int k = 0; k < count && ok; k++)
                    if (Vector3.Distance(lab[c], lab[picked[k]]) < colorMinDist) ok = false;
                if (!ok) continue;
                picked[count++] = c;
                if (count == n) return picked;
            }
            return null;
        }

        /// Higher is prettier. Rewards separation and even hue spacing; punishes mixing very
        /// light with very dark, and mixing vivid with washed-out.
        float HarmonyScore(int[] set, Vector3[] lab)
        {
            int n = set.Length;

            float minDist = float.MaxValue;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    minDist = Mathf.Min(minDist, Vector3.Distance(lab[set[i]], lab[set[j]]));

            // lightness and chroma consistency — the two that decide whether a board looks like a set
            float lSum = 0f, cSum = 0f;
            var chroma = new float[n];
            for (int i = 0; i < n; i++)
            {
                var v = lab[set[i]];
                chroma[i] = Mathf.Sqrt(v.y * v.y + v.z * v.z);
                lSum += v.x; cSum += chroma[i];
            }
            float lMean = lSum / n, cMean = cSum / n, lVar = 0f, cVar = 0f;
            for (int i = 0; i < n; i++)
            {
                float dl = lab[set[i]].x - lMean; lVar += dl * dl;
                float dc = chroma[i] - cMean;     cVar += dc * dc;
            }
            float lSd = Mathf.Sqrt(lVar / n), cSd = Mathf.Sqrt(cVar / n);

            // hue evenness: gaps around the wheel should be similar
            var hues = new float[n];
            for (int i = 0; i < n; i++)
            {
                var v = lab[set[i]];
                float h = Mathf.Atan2(v.z, v.y) * Mathf.Rad2Deg;
                hues[i] = h < 0f ? h + 360f : h;
            }
            System.Array.Sort(hues);
            float ideal = 360f / n, gapVar = 0f;
            for (int i = 0; i < n; i++)
            {
                float gap = (i == n - 1 ? hues[0] + 360f : hues[i + 1]) - hues[i];
                gapVar += (gap - ideal) * (gap - ideal);
            }
            float gapSd = Mathf.Sqrt(gapVar / n) / ideal;

            return minDist * 2.0f - gapSd * 0.20f - lSd * 0.55f - cSd * 0.55f;
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
            maxW = config != null ? Mathf.Max(1, config.weightCap) : 1;
            for (int i = 0; i < n; i++)
            {
                var lvl = AssetDatabase.LoadAssetAtPath<LevelData>($"{OutputFolder}/{from + i}.asset");
                weights[i] = lvl != null ? lvl.weight : 0;
                roles[i] = (byte)(config != null && config.milestoneEvery > 0 && (from + i) % config.milestoneEvery == 0
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
            const float padL = 44, padR = 8, padT = 8, padB = 20;
            var plot = new Rect(r.x + padL, r.y + padT, r.width - padL - padR, r.height - padT - padB);
            if (plot.width < 20 || plot.height < 20) return;

            var e = Event.current;
            if (e.type == EventType.MouseMove && plot.Contains(e.mousePosition)) Repaint();
            if (e.type != EventType.Repaint) return;
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

            // cap line (weightCap = difficulty 1.0) — only meaningful with a curve loaded
            if (config != null)
            {
                float capY = plot.yMax - config.weightCap / (float)maxW * plot.height;
                EditorGUI.DrawRect(new Rect(plot.xMin, capY, plot.width, 1f), new Color(red.r, red.g, red.b, 0.55f));
            }

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

            DrawGraphHover(plot, ink, blue, red);
        }

        void DrawGraphHover(Rect plot, Color ink, Color blue, Color red)
        {
            if (weights == null || weights.Length == 0 || !plot.Contains(Event.current.mousePosition)) return;

            int n = weights.Length;
            int i = Mathf.Clamp(Mathf.RoundToInt((Event.current.mousePosition.x - plot.xMin) / Mathf.Max(1f, plot.width) * Mathf.Max(1, n - 1)), 0, n - 1);
            int level = dataFrom + i;
            int weight = weights[i];
            var p = ToScreen(plot, i, n, weight);
            bool milestone = roles != null && i < roles.Length && roles[i] == (byte)CampaignCurveConfig.Role.Milestone;

            var marker = milestone ? red : blue;
            EditorGUI.DrawRect(new Rect(p.x - 1f, plot.yMin, 2f, plot.height), new Color(marker.r, marker.g, marker.b, 0.28f));
            EditorGUI.DrawRect(new Rect(p.x - 4f, p.y - 4f, 8f, 8f), marker);

            string text = milestone
                ? $"Level {level}  |  weight {weight}  |  milestone"
                : $"Level {level}  |  weight {weight}";
            var style = new GUIStyle(EditorStyles.helpBox)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 11,
                padding = new RectOffset(7, 7, 3, 3)
            };
            style.normal.textColor = ink;

            Vector2 size = style.CalcSize(new GUIContent(text));
            var box = new Rect(Event.current.mousePosition.x + 12f, Event.current.mousePosition.y - size.y - 10f, size.x + 10f, size.y + 6f);
            if (box.xMax > plot.xMax) box.x = Event.current.mousePosition.x - box.width - 12f;
            if (box.yMin < plot.yMin) box.y = Event.current.mousePosition.y + 12f;
            GUI.Label(box, text, style);
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
