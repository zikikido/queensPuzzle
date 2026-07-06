using System;
using System.Collections.Generic;
using Common;

namespace qp {
    /// <summary>
    /// App-wide persisted data. Exposes the PlayerPrefs helpers directly — no wrapper API.
    /// </summary>
    public static class AppData {

        // Persisted booster count per type — each with its own default.
        // Use directly, e.g. AppData.Boosts[EBoostType.QUEEN]++ / .Value
        public static readonly Dictionary<EBoostType, PlayerPrefsHelper.IntHolder> Boosts
            = new Dictionary<EBoostType, PlayerPrefsHelper.IntHolder> {
                { EBoostType.UNDO,  new PlayerPrefsHelper.IntHolder("qp_boost_UNDO",  5) },
                { EBoostType.HINT,     new PlayerPrefsHelper.IntHolder("qp_boost_HINT",     5) },
                { EBoostType.QUEEN, new PlayerPrefsHelper.IntHolder("qp_boost_QUEEN", 3) },
            };

        // Current level index (0-based) for the lobby / progression.
        public static readonly PlayerPrefsHelper.IntHolder LevelIdx = new PlayerPrefsHelper.IntHolder("qp_level_idx", 0);

        // How many times the CURRENT level was started, and which level the counter belongs to.
        // Reset only when a different level begins — LevelAttempts == 1 on the win = first try.
        public static readonly PlayerPrefsHelper.IntHolder LevelAttempts = new PlayerPrefsHelper.IntHolder("qp_level_attempts", 0);
        public static readonly PlayerPrefsHelper.IntHolder AttemptsLevelIdx = new PlayerPrefsHelper.IntHolder("qp_attempts_level_idx", -1);

        // The in-progress board of the CURRENT level, one char per cell (0/X/Q/W — including
        // wrong queens), so reopening the app lands exactly where the player stopped.
        // Valid only while BoardStateLevelIdx matches LevelIdx; invalidated on win.
        public static readonly PlayerPrefsHelper.StringHolder BoardState = new PlayerPrefsHelper.StringHolder("qp_board_state", "");
        public static readonly PlayerPrefsHelper.IntHolder BoardStateLevelIdx = new PlayerPrefsHelper.IntHolder("qp_board_state_level", -1);

        // Bones lost on the CURRENT level's board (valid together with BoardState). Not derivable
        // from the cells: after a fail-continue the wrong queens stay on the board as permanent
        // X's but the bones come back, so the count must be stored separately.
        public static readonly PlayerPrefsHelper.IntHolder BonesLost = new PlayerPrefsHelper.IntHolder("qp_bones_lost", 0);

        // Haptics (vibration) on/off.
        public static readonly PlayerPrefsHelper.BoolHolder Haptics = new PlayerPrefsHelper.BoolHolder("qp_haptics", true);

#if UNITY_EDITOR
        // Editor-only guard: catch an enum value that was added but not given a Boosts entry.
        [UnityEditor.InitializeOnLoadMethod]
        static void EditorVerifyAllBoostsPresent() {
            foreach (EBoostType type in Enum.GetValues(typeof(EBoostType)))
                if (!Boosts.ContainsKey(type))
                    UnityEngine.Debug.LogError($"[AppData] Boosts is missing an entry for EBoostType.{type}");
        }
#endif
    }
}
