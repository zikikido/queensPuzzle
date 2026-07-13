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

        // The decrypted level pack — loaded and decrypted ONCE, then every level is an O(1)
        // lookup-table read. ~1 MB for 10K levels; lives for the whole session.
        static byte[] _pack;
        static int _packCount;

        /// <summary>How many levels shipped — read from the pack header (0 when no pack).</summary>
        public static int LevelCount => EnsurePack() ? _packCount : 0;

        public static LevelPack.Level LoadLevel() {
#if UNITY_EDITOR
            // Level Builder "Play" hands a specific level over via SessionState — it wins in-editor.
            // The asset converts to the runtime type here: the game itself never sees a LevelData.
            string guid = UnityEditor.SessionState.GetString(PlayLevelGuidKey, "");
            if (!string.IsNullOrEmpty(guid)) {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path)) {
                    var playtest = UnityEditor.AssetDatabase.LoadAssetAtPath<LevelData>(path);
                    if (playtest != null) return playtest.ToPacked();
                }
            }
#endif
            if (!EnsurePack()) return null;

            // the current level, clamped so past-the-end keeps showing the last one
            int idx = Mathf.Clamp(AppData.LevelIdx, 0, _packCount - 1);
            return LevelPack.Decode(_pack, idx);
        }

        static bool EnsurePack() {
            if (_pack != null) return true;

            var ta = Resources.Load<TextAsset>("Levels/levels");
            if (ta == null) {
                Common.CDebug.LogError("[LevelLoader] Resources/Levels/levels.bytes not found — run Export Levels.");
                return false;
            }
            try {
                _pack = LevelPack.Decrypt(ta.bytes);
                _packCount = LevelPack.Count(_pack);
            } catch (System.Exception e) {
                Common.CDebug.LogError("[LevelLoader] level pack unreadable: " + e.Message);
                _pack = null;
                return false;
            }
            Resources.UnloadAsset(ta);   // the encrypted copy is no longer needed
            return _packCount > 0;
        }
    }
}
