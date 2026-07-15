using UnityEditor;
using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// The shared pool build core, extracted from the campaign builder so any target source can
    /// drive it (campaign levels, daily tiers). Generate → rate → fit into any open slot at the
    /// target's FIXED window — no widening, a board that fits nothing is discarded.
    ///
    /// Inputs are deliberately dumb: <see cref="GenerationGates"/> + a list of LevelTargets whose
    /// weight windows are already baked in. Callers own all dialogs; this owns the worker threads,
    /// the progress bar and the asset writes ({outputFolder}/{target.level}.asset).
    /// </summary>
    public static class PoolBuildRunner
    {
        public struct Options
        {
            public int threadCount;
            public int seedBase;
            public string title;      // progress-bar title
            public string tolLabel;   // display only, e.g. "tol ±10%"
        }

        public sealed class ScanResult
        {
            public readonly System.Collections.Generic.List<CampaignCurveConfig.LevelTarget> open
                = new System.Collections.Generic.List<CampaignCurveConfig.LevelTarget>();
            public int newCount, offTol, overwrite;
        }

        /// <summary>
        /// Which targets still need a level: no asset yet · skip-existing off · or the existing
        /// level sits outside its tolerance window and override is on.
        /// </summary>
        public static ScanResult Scan(System.Collections.Generic.IList<CampaignCurveConfig.LevelTarget> targets,
            string outputFolder, bool skipExisting, bool overrideOffTol)
        {
            var r = new ScanResult();
            foreach (var t in targets)
            {
                var existing = AssetDatabase.LoadAssetAtPath<LevelData>($"{outputFolder}/{t.level}.asset");
                if (existing == null) { r.open.Add(t); r.newCount++; }
                else if (overrideOffTol && (existing.weight < t.minWeight || existing.weight > t.maxWeight)) { r.open.Add(t); r.offTol++; }
                else if (!skipExisting) { r.open.Add(t); r.overwrite++; }
            }
            return r;
        }

        // shared with worker threads during generation
        sealed class SharedState
        {
            public int nextSeed;
            public long attempts;
            public bool stop;
            public int[] sizes;
        }

        /// <summary>
        /// Fills the open slots until done or the user cancels the progress bar. Mutates
        /// <paramref name="open"/> (filled slots are removed). Returns (levels filled, boards generated).
        /// </summary>
        public static (int made, long attempts) Run(GenerationGates gates,
            System.Collections.Generic.List<CampaignCurveConfig.LevelTarget> open,
            string outputFolder, Options opt)
        {
            EnsureFolder(outputFolder);
            var queue = new System.Collections.Concurrent.ConcurrentQueue<(int[] region, int[] cols, int seed, WeightRater.Report rep, int size)>();
            int made = 0;

            // workers: generate + rate off the main thread (TryGenerateRaw and Rate are thread-safe)
            var st = new SharedState { nextSeed = opt.seedBase - 1, sizes = SizesOf(open) };
            var workers = new System.Threading.Thread[opt.threadCount];
            for (int w = 0; w < workers.Length; w++)
            {
                workers[w] = new System.Threading.Thread(() =>
                {
                    while (!System.Threading.Volatile.Read(ref st.stop))
                    {
                        if (queue.Count > 256) { System.Threading.Thread.Sleep(5); continue; }   // backpressure
                        int[] sizes = System.Threading.Volatile.Read(ref st.sizes);
                        if (sizes.Length == 0) break;
                        int s = System.Threading.Interlocked.Increment(ref st.nextSeed);
                        int size = sizes[(s - opt.seedBase) % sizes.Length];
                        bool ok = LevelGenerator.TryGenerateRaw(size, s, 250, out int[] region, out int[] cols, out _);
                        System.Threading.Interlocked.Increment(ref st.attempts);
                        if (!ok) continue;
                        var rep = WeightRater.Rate(size, region, cols);
                        if (rep.trials > 0) continue;   // guess wall — discarded on the worker
                        queue.Enqueue((region, cols, s, rep, size));
                    }
                }) { IsBackground = true, Name = $"PoolGen{w}" };
                workers[w].Start();
            }

            try
            {
                AssetDatabase.StartAssetEditing();   // files are still written per level; Unity imports them once at the end
                while (open.Count > 0)
                {
                    long attempts = System.Threading.Interlocked.Read(ref st.attempts);
                    if (EditorUtility.DisplayCancelableProgressBar(opt.title,
                        $"filled {made} · open {open.Count} · boards {attempts} · threads {opt.threadCount} · {opt.tolLabel}",
                        made / (float)(made + open.Count))) break;

                    bool filled = false;
                    while (open.Count > 0 && queue.TryDequeue(out var c))
                    {
                        int slot = FindSlot(gates, open, c.size, c.rep);
                        if (slot >= 0)
                        {
                            SaveLevel(outputFolder, open[slot], c.region, c.cols, c.rep.weight, c.seed);
                            open.RemoveAt(slot);
                            made++;
                            filled = true;
                        }
                        // no fit at the fixed window → the board is discarded; the window never widens
                    }

                    if (filled) System.Threading.Volatile.Write(ref st.sizes, SizesOf(open));   // workers switch to the sizes still needed

                    System.Threading.Thread.Sleep(10);   // let workers produce; keep the editor responsive
                }
            }
            finally
            {
                System.Threading.Volatile.Write(ref st.stop, true);
                foreach (var t in workers) t.Join(2000);
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            return (made, System.Threading.Interlocked.Read(ref st.attempts));
        }

        static int[] SizesOf(System.Collections.Generic.List<CampaignCurveConfig.LevelTarget> open)
        {
            var sizes = new int[open.Count];
            for (int i = 0; i < open.Count; i++) sizes[i] = open[i].boardSize;
            return sizes;
        }

        // Rarest fit wins (milestone > peak > build > normal > breather) so easy slots
        // don't eat the rare heavy boards.
        static int FindSlot(in GenerationGates gates,
            System.Collections.Generic.List<CampaignCurveConfig.LevelTarget> open, int size,
            in WeightRater.Report rep)
        {
            int best = -1, bestPrio = -1;
            for (int i = 0; i < open.Count; i++)
            {
                var t = open[i];
                if (t.boardSize != size) continue;
                if (!CampaignCurve.PassesGates(gates, t, rep)) continue;
                int prio = (int)t.role;
                if (prio > bestPrio) { bestPrio = prio; best = i; }
            }
            return best;
        }

        static void SaveLevel(string outputFolder, in CampaignCurveConfig.LevelTarget t,
            int[] region, int[] cols, int weight, int seed)
        {
            var data = ScriptableObject.CreateInstance<LevelData>();
            data.size = t.boardSize;
            data.regions = region;
            data.solutionColumns = cols;
            data.weight = weight;
            data.seed = seed;
            AssetDatabase.CreateAsset(data, $"{outputFolder}/{t.level}.asset");
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
    }
}
