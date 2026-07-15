using QueensPuzzle;
using UnityEngine;

namespace qp {
    /// <summary>
    /// Resolves which LevelData to play. In the editor a level the Level Builder handed over for
    /// playtesting wins; otherwise the player's current level (AppData.LevelIdx). Past the end
    /// of the pack the game is endless - see <see cref="PackIndex"/>.
    /// </summary>
    public static class LevelLoader {

        // SessionState key the Level Builder writes to hand a specific level to play mode (editor only).
        // Lives here (runtime) because the runtime loader must read it - editor scripts can't be
        // referenced by runtime code. The Level Builder references LevelLoader.PlayLevelGuidKey.
        public const string PlayLevelGuidKey = "qp.playLevelGuid";

        // The decrypted level pack - loaded and decrypted ONCE, then every level is an O(1)
        // lookup-table read. ~1 MB for 10K levels; lives for the whole session.
        static byte[] _pack;
        static int _packCount;
        const string UnknownLevelSetId = "unknown";
        static string _levelSetId = UnknownLevelSetId;

        public static string CurrentLevelSetId { get; private set; } = UnknownLevelSetId;
        public static int CurrentPackIndex { get; private set; } = -1;
        public static int CurrentLevelHash { get; private set; }

        /// <summary>How many levels shipped - read from the pack header (0 when no pack).</summary>
        public static int LevelCount => EnsurePack() ? _packCount : 0;

        public static LevelPack.Level LoadLevel() {
#if UNITY_EDITOR
            // Level Builder "Play" hands a specific level over via SessionState - it wins in-editor.
            // The asset converts to the runtime type here: the game itself never sees a LevelData.
            string guid = UnityEditor.SessionState.GetString(PlayLevelGuidKey, "");
            if (!string.IsNullOrEmpty(guid)) {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path)) {
                    var playtest = UnityEditor.AssetDatabase.LoadAssetAtPath<LevelData>(path);
                    if (playtest != null) {
                        var level = playtest.ToPacked();
                        SetCurrentIdentity("editor_playtest", -1, level);
                        return level;
                    }
                }
            }
#endif
            if (!EnsurePack()) return null;

            int packIndex = PackIndex(Mathf.Max(0, AppData.LevelIdx));
            var packedLevel = LevelPack.Decode(_pack, packIndex);
            SetCurrentIdentity(_levelSetId, packIndex, packedLevel);
            return packedLevel;
        }

        static void SetCurrentIdentity(string levelSetId, int packIndex, LevelPack.Level level) {
            CurrentLevelSetId = string.IsNullOrEmpty(levelSetId) ? UnknownLevelSetId : levelSetId;
            CurrentPackIndex = packIndex;
            CurrentLevelHash = level != null ? level.ContentHash() : 0;
        }

        // Levels per wave in the campaign curve (CampaignCurveConfig wave templates).
        const int WaveLen = 20;

        // Maps the ever-growing LevelIdx to a pack index. Within the pack it's 1:1; past the
        // end the game turns endless: the last tenth of the pack loops forever, whole waves
        // shuffled per lap (so laps differ but the difficulty rhythm inside a wave survives).
        // Seeded by lap number only - every player sees the same levels in the same order.
        static int PackIndex(int levelIdx) {
            if (levelIdx < _packCount) return levelIdx;

            int waves = _packCount / 10 / WaveLen;   // whole waves in the loop region
            if (waves < 1) return _packCount - 1;    // pack too small to loop - old clamp behavior

            int loopLen = waves * WaveLen;
            int past = levelIdx - _packCount;
            int lap = past / loopLen;
            int pos = past % loopLen;

            // Fisher-Yates over the wave blocks, seeded by lap - deterministic across players.
            var perm = new int[waves];
            for (int i = 0; i < waves; i++) perm[i] = i;
            for (int i = waves - 1; i > 0; i--) {
                int j = (int)(Hash((uint)lap * 0x9E3779B9u + (uint)i) % (uint)(i + 1));
                (perm[i], perm[j]) = (perm[j], perm[i]);
            }
            return _packCount - loopLen + perm[pos / WaveLen] * WaveLen + pos % WaveLen;
        }

        static uint Hash(uint x) {   // lowbias32 - same deterministic hash the campaign curve uses
            x ^= x >> 16; x *= 0x7feb352d;
            x ^= x >> 15; x *= 0x846ca68b;
            x ^= x >> 16;
            return x;
        }

        static bool EnsurePack() {
            if (_pack != null) return true;

            var ta = Resources.Load<TextAsset>("Levels/levels");
            if (ta == null) {
                Common.CDebug.LogError("[LevelLoader] Resources/Levels/levels.bytes not found - run Export Levels.");
                return false;
            }
            try {
                _pack = LevelPack.Decrypt(ta.bytes);
                _packCount = LevelPack.Count(_pack);
                _levelSetId = LevelPack.LevelSetId(_pack);
            } catch (System.Exception e) {
                Common.CDebug.LogError("[LevelLoader] level pack unreadable: " + e.Message);
                _pack = null;
                _levelSetId = UnknownLevelSetId;
                return false;
            }
            Resources.UnloadAsset(ta);   // the encrypted copy is no longer needed
            return _packCount > 0;
        }
    }
}