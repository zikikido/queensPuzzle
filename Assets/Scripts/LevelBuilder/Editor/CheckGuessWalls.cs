using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace QueensPuzzle {

    // One-shot batch: rate every LevelData under Assets/Levels and flag guess walls —
    // levels the human-style solver cannot finish by deduction alone (trials > 0).
    // Read-only: nothing is stamped, offenders are listed in the console.
    public static class CheckGuessWalls {

        [MenuItem("QueensPuzzle/Check guess walls")]
        public static void Run() {
            string[] guids = AssetDatabase.FindAssets("t:LevelData", new[] { "Assets/Levels" });
            var walls = new List<(string path, WeightRater.Report rep)>();
            var broken = new List<string>();
            int done = 0;
            try {
                for (int i = 0; i < guids.Length; i++) {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var lvl = AssetDatabase.LoadAssetAtPath<LevelData>(path);
                    if (lvl == null) continue;
                    if (EditorUtility.DisplayCancelableProgressBar("Checking guess walls", path, (float)i / guids.Length)) break;
                    try {
                        var rep = WeightRater.Rate(lvl.size, lvl.regions, lvl.solutionColumns);
                        if (!rep.solved) broken.Add(path);
                        else if (rep.trials > 0) walls.Add((path, rep));
                        done++;
                    } catch (System.Exception e) {
                        Debug.LogError($"[CheckGuessWalls] {path}: {e.Message}");
                        broken.Add(path);
                    }
                }
            } finally {
                EditorUtility.ClearProgressBar();
            }

            foreach (var (path, rep) in walls.OrderByDescending(w => w.rep.guessCost)) {
                int worstRun = rep.guessCosts.Length > 0 ? rep.guessCosts.Max() : 0;
                Debug.LogWarning($"[CheckGuessWalls] GUESS WALL {path} — weight {rep.weight}, " +
                    $"{rep.trials} guess(es) at {rep.guessCosts.Length} stuck point(s), depth {rep.maxTrialDepth}, " +
                    $"guess cost {rep.guessCost} (worst run {worstRun})", AssetDatabase.LoadAssetAtPath<LevelData>(path));
            }
            foreach (string path in broken)
                Debug.LogError($"[CheckGuessWalls] UNSOLVABLE {path} — solver could not finish even with guessing",
                    AssetDatabase.LoadAssetAtPath<LevelData>(path));

            Debug.Log($"[CheckGuessWalls] checked {done} of {guids.Length}: " +
                $"{done - walls.Count - broken.Count} clean, {walls.Count} guess wall(s), {broken.Count} broken.");
        }
    }
}
