using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace QueensPuzzle.EditorTools
{
    /// <summary>
    /// Range generator: clone a run of numbered levels from the Load set into the Save set, in
    /// parallel. Each new level keeps its source's board size AND is steered to its source's own
    /// fingerprint (weight / peak / even / steps) — so the Save set copies the Load set's difficulty,
    /// just on different boards. Tolerances + the Warm toggle apply to all. Every level must end on a
    /// unique board ("V"); slots that can't reach one in the attempt budget are offered for retry.
    /// </summary>
    public partial class LevelBuilderWindow
    {
        const int RangeAttempts = 300;   // per-level tries to reach a unique board before giving up

        int _rangeFrom = 1, _rangeTo = 10;

        struct BatchJob { public int number, size, seed; public int[] warmRegion, warmSol; public LevelFingerprint target; }
        struct BatchResult { public bool ok, onTarget; public int[] region, sol; public int weight; }

        // live run state (null when idle)
        Task<BatchResult>[] _batchTasks;
        List<BatchJob> _batchJobs;
        int _batchRound, _batchSavedTotal, _batchOnTarget;

        void DrawRangeGenerate()
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Generate range", GUILayout.Width(146));
                GUILayout.Label("from", EditorStyles.miniLabel, GUILayout.Width(32));
                _rangeFrom = Mathf.Max(1, EditorGUILayout.IntField(_rangeFrom, GUILayout.Width(44)));
                GUILayout.Label("to", EditorStyles.miniLabel, GUILayout.Width(18));
                _rangeTo = Mathf.Max(_rangeFrom, EditorGUILayout.IntField(_rangeTo, GUILayout.Width(44)));
            }
            using (new EditorGUI.DisabledScope(_batchTasks != null))
                if (GUILayout.Button($"Clone {_rangeFrom}–{_rangeTo}: {LoadSet} → {SaveSet} (parallel, save)", GUILayout.Height(24)))
                    GenerateRange();
            EditorGUILayout.LabelField(" ", $"copies '{LoadSet}' size + fingerprint into '{SaveSet}' · tolerances + Warm apply to all · retries until unique", EditorStyles.miniLabel);
        }

        void GenerateRange()
        {
            // One job per source slot. Size AND the target fingerprint come from the LOAD-set level
            // (so we reproduce its difficulty); the new board is saved into the SAVE set.
            var jobs = new List<BatchJob>();
            var missing = new List<int>();
            int baseSeed = System.Environment.TickCount;
            for (int num = _rangeFrom; num <= _rangeTo; num++)
            {
                var src = AssetDatabase.LoadAssetAtPath<LevelData>(LevelPath(LoadSet, num));
                if (src == null) { missing.Add(num); continue; }

                var rep = WeightRater.Rate(src.size, src.regions, src.solutionColumns);  // the source's own fingerprint
                jobs.Add(new BatchJob
                {
                    number = num,
                    size = src.size,
                    seed = baseSeed + num * 7919,
                    warmRegion = (int[])src.regions.Clone(),
                    warmSol = (int[])src.solutionColumns.Clone(),
                    target = new LevelFingerprint
                    {
                        weight = rep.weight,
                        peak = rep.peak,
                        evenness = Mathf.Round(rep.evenness * 100f) / 100f,
                        steps = rep.paidSteps,
                        tolWeightPct = _tolWeight,
                        tolPeakPct = _tolPeak,
                        tolEvennessPct = _tolEvenness,
                        tolStepsPct = _tolSteps,
                    },
                });
            }

            if (jobs.Count == 0)
            {
                EditorUtility.DisplayDialog("Generate range",
                    $"No levels in the load set '{LoadSet}' between {_rangeFrom} and {_rangeTo}.\n\n" +
                    "The range copies each slot's size + fingerprint from the load set, so those slots must exist.", "OK");
                return;
            }
            if (missing.Count > 0)
                Debug.Log($"[LevelBuilder] Range: skipping {missing.Count} slot(s) missing in {LoadSet}: {string.Join(",", missing)}");

            _batchRound = 1;
            _batchSavedTotal = 0;
            _batchOnTarget = 0;
            StartBatchRound(jobs);
        }

        void StartBatchRound(List<BatchJob> jobs)
        {
            bool warm = _warmStart;   // warm-start each new board from its source board (global toggle)

            _batchJobs = jobs;
            _batchTasks = new Task<BatchResult>[jobs.Count];
            for (int i = 0; i < jobs.Count; i++)
            {
                var job = jobs[i];   // local copy so the closure captures this iteration's job
                _batchTasks[i] = Task.Run(() => ComputeLevel(job, warm));
            }
            EditorApplication.update += PollBatch;
        }

        // Pure — runs on a worker thread. Steers to the job's (source) fingerprint, verifying a unique
        // board each try; keeps the best (closest to target) unique result and stops early once it's
        // on target. ok = false only if no unique board turned up within the attempt budget.
        static BatchResult ComputeLevel(BatchJob job, bool warm)
        {
            var best = new BatchResult { ok = false };
            double bestGap = double.MaxValue;
            try
            {
                for (int attempt = 0; attempt < RangeAttempts; attempt++)
                {
                    int seed = job.seed + attempt * 104729;
                    if (!LevelSteerer.TrySteerRaw(job.target, job.size, seed,
                            warm ? job.warmRegion : null, warm ? job.warmSol : null,
                            out int[] region, out int[] sol, out _))
                        continue;
                    if (QueensSolver.TryFindAlternative(job.size, region, sol, out _)) continue; // not unique → not V

                    var rep = WeightRater.Rate(job.size, region, sol);
                    double gap = WeightAnnealer.FingerprintGap(rep, job.target);
                    if (gap < bestGap)
                    {
                        bestGap = gap;
                        best = new BatchResult { ok = true, onTarget = gap <= 0, region = region, sol = sol, weight = rep.weight };
                    }
                    if (bestGap <= 0) break; // matched the source fingerprint
                }
            }
            catch { /* fall through to failure — the retry dialog offers another round */ }
            return best;
        }

        // Polled on the editor loop so the UI stays responsive while the workers run.
        void PollBatch()
        {
            if (_batchTasks == null) { EditorApplication.update -= PollBatch; return; } // domain-reload safety

            int done = 0;
            for (int i = 0; i < _batchTasks.Length; i++) if (_batchTasks[i].IsCompleted) done++;
            EditorUtility.DisplayProgressBar($"Clone range · round {_batchRound}",
                $"{done}/{_batchTasks.Length} levels (parallel)…",
                _batchTasks.Length == 0 ? 1f : done / (float)_batchTasks.Length);
            if (done < _batchTasks.Length) return;   // still working

            EditorApplication.update -= PollBatch;
            EditorUtility.ClearProgressBar();

            // Back on the main thread: save the valid ones, collect the ones with no unique board.
            var failed = new List<BatchJob>();
            int roundClosest = 0;
            for (int i = 0; i < _batchTasks.Length; i++)
            {
                var res = _batchTasks[i].Result;
                if (res.ok)
                {
                    SaveBatchLevel(_batchJobs[i], res);
                    _batchSavedTotal++;
                    if (res.onTarget) _batchOnTarget++; else roundClosest++;
                }
                else failed.Add(_batchJobs[i]);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            RefreshMaxLevel();

            _batchTasks = null;
            _batchJobs = null;

            int closest = _batchSavedTotal - _batchOnTarget;
            if (failed.Count == 0)
            {
                _status = $"Cloned {_batchSavedTotal} → {SaveSet} · {_batchOnTarget} on fingerprint, {closest} closest · all unique ✓";
                Repaint();
                return;
            }

            string list = string.Join(", ", failed.ConvertAll(j => j.number.ToString()));
            bool retry = EditorUtility.DisplayDialog("Some levels aren't unique yet",
                $"{failed.Count} level(s) didn't reach a unique board in {RangeAttempts} attempts:\n{list}\n\nRetry these?",
                "Retry", "Stop");
            if (retry)
            {
                for (int i = 0; i < failed.Count; i++)
                {
                    var j = failed[i];
                    j.seed = System.Environment.TickCount + i * 7919;   // fresh seeds for the next round
                    failed[i] = j;
                }
                _batchRound++;
                StartBatchRound(failed);
            }
            else
            {
                _status = $"Saved {_batchSavedTotal} to {SaveSet}; stopped with {failed.Count} without a unique board: {list}";
                Repaint();
            }
        }

        // Write the generated board into the Save-set slot: overwrite in place (keeps GUID) or create.
        void SaveBatchLevel(BatchJob job, BatchResult res)
        {
            EnsureSetFolder(SaveSet);
            string path = LevelPath(SaveSet, job.number);
            var lvl = AssetDatabase.LoadAssetAtPath<LevelData>(path);
            bool isNew = lvl == null;
            if (isNew) lvl = ScriptableObject.CreateInstance<LevelData>();

            lvl.size = job.size;
            lvl.regions = res.region;
            lvl.solutionColumns = res.sol;
            lvl.seed = job.seed;
            lvl.weight = res.weight;
            lvl.regionColors = null;   // generated board — default region→colour mapping
            lvl.revealedRows = null;   // new board, new solution — stale reveals would point at wrong rows
            lvl.name = job.number.ToString();

            if (isNew) AssetDatabase.CreateAsset(lvl, path);
            else EditorUtility.SetDirty(lvl);
        }
    }
}
