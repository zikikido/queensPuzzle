using QueensPuzzle;
using UnityEngine;

namespace qp {
    /// <summary>
    /// Resolves which LevelData to play. In the editor a level the Level Builder handed over for
    /// playtesting wins; otherwise the player's current level (AppData.LevelIdx), clamped to
    /// MaxLevels so reaching the end keeps showing the last level.
    /// </summary>
    public static class LevelLoader {

        // SessionState key the Level Builder writes to hand a specific level to play mode (editor only).
        // Lives here (runtime) because the runtime loader must read it — editor scripts can't be
        // referenced by runtime code. The Level Builder references LevelLoader.PlayLevelGuidKey.
        public const string PlayLevelGuidKey = "qp.playLevelGuid";

        public static LevelData LoadLevel() {
#if UNITY_EDITOR
            // Level Builder "Play" hands a specific level over via SessionState — it wins in-editor.
            string guid = UnityEditor.SessionState.GetString(PlayLevelGuidKey, "");
            if (!string.IsNullOrEmpty(guid)) {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path)) {
                    var playtest = UnityEditor.AssetDatabase.LoadAssetAtPath<LevelData>(path);
                    if (playtest != null) return playtest;
                }
            }
#endif
            // Runtime: the current level, clamped so past-the-end keeps showing the last one.
            int idx = Mathf.Clamp(AppData.LevelIdx, 0, SOLevelsData.Count - 1);
            return Resources.Load<LevelData>($"Levels/{idx + 1}");   // Resources/Levels/{1-based}
        }
    }
}
