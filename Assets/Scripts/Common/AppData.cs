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
