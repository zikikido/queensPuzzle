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

        // Accumulated active play time on the CURRENT campaign level (whole seconds) — the campaign
        // twin of the daily's timeSec. Survives restarts/fails/app-resume; reset when a new level
        // begins (same lifecycle as LevelAttempts). Reported as lvl_time_sec; never shown to the player.
        public static readonly PlayerPrefsHelper.IntHolder LevelTimeSec = new PlayerPrefsHelper.IntHolder("qp_level_time_sec", 0);

        // The CURRENT attempt as one saved blob: the in-progress board plus the attempt's meta
        // (boost uses, added lives, bones lost) — see LastPlayData. Mutate fields then Save().
        public static LastPlayData LastPlayData = qp.LastPlayData.Load();

        // Haptics (vibration) on/off.
        public static readonly PlayerPrefsHelper.BoolHolder Haptics = new PlayerPrefsHelper.BoolHolder("qp_haptics", true);

        // Debug mode, unlocked by the settings-popup switch code (MBSettingsPopup). Persists so
        // the short open-code works on every later run until explicitly exited in MBDebugWin.
        public static readonly PlayerPrefsHelper.BoolHolder DebugMode = new PlayerPrefsHelper.BoolHolder("qp_debug_mode", false);

        // FPS overlay on/off (MBFpsOverlay) — persists so it's already up on the next run.
        public static readonly PlayerPrefsHelper.BoolHolder ShowFps = new PlayerPrefsHelper.BoolHolder("qp_show_fps", false);

        // One-shot: the "this is your last bone" callout ($LastBonesToturial) is shown the first
        // time the player is ever down to a single bone, then never again.
        public static readonly PlayerPrefsHelper.BoolHolder LastBoneToturialSeen = new PlayerPrefsHelper.BoolHolder("qp_last_bone_tut_seen", false);

        // Install attribution from Singular's device attribution callback (network / campaign ...).
        // Written once by SingularBoot when the callback fires (usually first launch, may take a few
        // seconds — or never, if Singular hasn't enabled the callback for the app). Read on every
        // later launch and attached to each events-server event. Value == null until it arrives.
        public static readonly PlayerPrefsHelper.ObjectHolder<SingularSource> SingularSource
            = new PlayerPrefsHelper.ObjectHolder<SingularSource>("qp_singular_source");

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
