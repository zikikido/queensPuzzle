using UnityEditor;
using UnityEngine;

namespace QueensPuzzle {

    // One-shot batch: rate every LevelData under Assets/Levels (MSet, Puzzby, __Play) with the
    // WeightRater and stamp lvl.weight — fills the unrated old-format levels and refreshes the rest.
    public static class RateAllLevels {

        [MenuItem("Tools/QueensPuzzle/Rate all level weights")]
        public static void Run() {
            string[] guids = AssetDatabase.FindAssets("t:LevelData", new[] { "Assets/Levels" });
            int done = 0, changed = 0, failed = 0;
            try {
                for (int i = 0; i < guids.Length; i++) {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var lvl = AssetDatabase.LoadAssetAtPath<LevelData>(path);
                    if (lvl == null) continue;
                    if (EditorUtility.DisplayCancelableProgressBar("Rating levels", path, (float)i / guids.Length)) break;
                    try {
                        var rep = WeightRater.Rate(lvl.size, lvl.regions, lvl.solutionColumns);
                        if (lvl.weight != rep.weight) {
                            lvl.weight = rep.weight;
                            EditorUtility.SetDirty(lvl);
                            changed++;
                        }
                        done++;
                    } catch (System.Exception e) {
                        Debug.LogError($"[RateAllLevels] {path}: {e.Message}");
                        failed++;
                    }
                }
            } finally {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
            }
            Debug.Log($"[RateAllLevels] rated {done}, updated {changed}, failed {failed} (of {guids.Length}).");
        }
    }
}
