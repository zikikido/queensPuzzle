using UnityEditor;
using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// Daily Challenge pool builder: pick a <see cref="DailyCurveConfig"/> and fill each tier's
    /// pool (Assets/Levels/Sets/Daily{tier}, auto-created) through the same
    /// <see cref="PoolBuildRunner"/> the campaign uses — same generator, rater and gates, only
    /// the target list differs. Living under Sets/ also puts the daily pools in the campaign
    /// window's set dropdown (graphs, color fix) — just don't campaign-generate into them.
    ///
    /// Seeds live far above the campaign's (default 100M + 10M per tier) so a daily board is
    /// never the byte-identical twin of a campaign level. Export to per-tier packs is a separate
    /// step (the current exporter ships one levels.bytes only).
    /// </summary>
    public class DailyBuilderWindow : EditorWindow
    {
        const string SetsRoot = "Assets/Levels/Sets";
        const int SeedStride = 10_000_000;   // per-tier seed offset — keeps tier pools disjoint

        DailyCurveConfig config;
        int seedBase = 100_000_000;
        bool skipExisting = true;
        bool overrideOffTol = true;
        int threadCount = Mathf.Max(1, System.Environment.ProcessorCount - 1);

        [MenuItem("QueensPuzzle/Daily Builder")]
        static void Open() => GetWindow<DailyBuilderWindow>("Daily Builder");

        void OnGUI()
        {
            config = (DailyCurveConfig)EditorGUILayout.ObjectField("Daily config", config, typeof(DailyCurveConfig), false);
            seedBase = EditorGUILayout.IntField("Seed base", seedBase);
            skipExisting = EditorGUILayout.Toggle("Skip existing", skipExisting);
            overrideOffTol = EditorGUILayout.Toggle("Override off-tolerance", overrideOffTol);
            threadCount = Mathf.Clamp(EditorGUILayout.IntField("Threads", threadCount), 1, 64);

            if (config == null || config.campaign == null)
            {
                EditorGUILayout.HelpBox(config == null
                    ? "Assign a DailyCurve asset (Create → QueensPuzzle → Daily Curve)."
                    : "The daily config needs its campaign reference (scale + gates).", MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            for (int i = 0; i < config.tiers.Length; i++)
            {
                var tr = config.tiers[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    string range = tr.progressMax == int.MaxValue ? $"{tr.progressMin}+" : $"{tr.progressMin}–{tr.progressMax}";
                    EditorGUILayout.LabelField($"{tr.name}  ·  progress {range}  ·  diff {tr.diffMin:0.00}–{tr.diffMax:0.00}", GUILayout.MinWidth(240));
                    EditorGUILayout.LabelField($"{CountBuilt(FolderOf(tr))}/{tr.PoolSize}", GUILayout.Width(70));
                    if (GUILayout.Button("Generate", GUILayout.Width(80)))
                    {
                        GenerateTier(i);
                        GUIUtility.ExitGUI();
                    }
                }
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Generate all tiers"))
            {
                for (int i = 0; i < config.tiers.Length; i++)
                    if (!GenerateTier(i)) break;   // a cancel stops the batch
                GUIUtility.ExitGUI();
            }
        }

        string FolderOf(DailyCurveConfig.Tier tr) => $"{SetsRoot}/Daily{tr.name}";

        static int CountBuilt(string folder)
        {
            if (!System.IO.Directory.Exists(folder)) return 0;
            int n = 0;
            foreach (var f in System.IO.Directory.GetFiles(folder, "*.asset"))
                if (int.TryParse(System.IO.Path.GetFileNameWithoutExtension(f), out _)) n++;
            return n;
        }

        // false = the user cancelled (stops a Generate-all batch)
        bool GenerateTier(int i)
        {
            var tr = config.tiers[i];
            string folder = FolderOf(tr);
            var scan = PoolBuildRunner.Scan(config.GetTierTargets(i), folder, skipExisting, overrideOffTol);
            if (scan.open.Count == 0)
            {
                EditorUtility.DisplayDialog("Daily Builder", $"{tr.name}: pool is full and inside tolerance.", "OK");
                return true;
            }

            int tierSeed = seedBase + i * SeedStride;
            if (!EditorUtility.DisplayDialog("Generate daily pool — summary",
                $"Tier:  {tr.name}\n" +
                $"Pool:  {tr.PoolSize} slot(s) ({tr.weeks} weeks)\n\n" +
                $"To build:  {scan.open.Count}\n" +
                $"   • new (missing):  {scan.newCount}\n" +
                $"   • override off-tolerance:  {scan.offTol}\n" +
                $"   • overwrite existing:  {scan.overwrite}\n\n" +
                $"Tolerance:  ±{config.campaign.matchTol * 100:0}% (fixed)\n" +
                $"Threads:  {threadCount}   ·   Seed base:  {tierSeed}",
                "Go", "Cancel"))
                return false;

            var (made, attempts) = PoolBuildRunner.Run(config.campaign.Gates, scan.open, folder,
                new PoolBuildRunner.Options
                {
                    threadCount = threadCount,
                    seedBase = tierSeed,
                    title = $"Generating daily pool — {tr.name}",
                    tolLabel = $"tol ±{config.campaign.matchTol * 100:0}%",
                });
            Debug.Log($"[DailyBuilder] {tr.name}: filled {made}, {scan.open.Count} still open, {attempts} boards generated");
            return scan.open.Count == 0;   // slots left ⇒ the run was cancelled mid-way
        }
    }
}
