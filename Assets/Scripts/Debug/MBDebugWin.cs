using Common;
using System;
using UnityEngine;
using UnityEngine.Diagnostics;
using UnityEngine.EventSystems;

namespace qp {
    /// <summary>
    /// Runtime debug panel — IMGUI, so it needs no prefab or scene presence and works in any
    /// scene, on device too. Opened from the settings popup by the switch code once
    /// AppData.DebugMode is on (see MBSettingsPopup). Add a button = add one line in OnGUI.
    /// </summary>
    public class MBDebugWin : MonoBehaviour {

        static MBDebugWin _instance;

        Vector2 _scroll;
        float _scale;
        string _levelInput = "";
        EventSystem _blockedEventSystem;   // the EventSystem we disabled while we're up

        public static void Open() {
            if (_instance == null) _instance = new GameObject("$DebugWin").AddComponent<MBDebugWin>();
        }

        void Awake() {
            _scale = Mathf.Max(1f, Screen.dpi / 96f);   // finger-sized widgets on phone screens
        }

        void Close() => Destroy(gameObject);
        void OnDestroy() { if (_instance == this) _instance = null; }

        // Everything under the panel is blocked while it's up: uGUI via the EventSystem
        // (IMGUI doesn't go through it, so we stay clickable), the GP board via InputLocks.
        void OnEnable() {
            if (MBGameplay.instance != null) MBGameplay.instance.InputLocks++;
            _blockedEventSystem = EventSystem.current;
            if (_blockedEventSystem != null) _blockedEventSystem.enabled = false;
        }

        void OnDisable() {
            if (MBGameplay.instance != null) MBGameplay.instance.InputLocks--;
            if (_blockedEventSystem != null) { _blockedEventSystem.enabled = true; _blockedEventSystem = null; }
        }

        void OnGUI() {
            GUI.matrix = Matrix4x4.Scale(new Vector3(_scale, _scale, 1f));
            float w = Screen.width / _scale, h = Screen.height / _scale;

            GUILayout.BeginArea(new Rect(10, 10, w - 20, h - 20), GUI.skin.box);
            GUILayout.Label($"DEBUG | level {AppData.LevelIdx + 1}/{SOLevelsData.Count} | attempts {AppData.LevelAttempts.Value} | bones lost {AppData.LastPlayData.bonesLost} | firebase {FirebaseState()}");
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("Crashlytics");
            if (Button("Send non-fatal exception")) CDebug.LogError(new Exception("MBDebugWin: non-fatal test"));
            if (Button("CRASH NOW (native)")) Utils.ForceCrash(ForcedCrashCategory.AccessViolation);

            GUILayout.Space(16);
            GUILayout.Label("Boosters");
            foreach (var boost in AppData.Boosts)
                if (Button($"+5 {boost.Key} (now {boost.Value.Value})")) boost.Value.Value += 5;

            GUILayout.Space(16);
            GUILayout.Label("Level");
            if (Button("Level +1")) JumpLevel(+1);
            if (Button("Level -1")) JumpLevel(-1);
            GUILayout.BeginHorizontal();
            _levelInput = GUILayout.TextField(_levelInput, GUILayout.Height(44), GUILayout.Width(120));
            if (Button($"Set level (1-{SOLevelsData.Count})") && int.TryParse(_levelInput, out int lvl))
                AppData.LevelIdx.Value = Mathf.Clamp(lvl - 1, 0, Mathf.Max(0, SOLevelsData.Count - 1));
            GUILayout.EndHorizontal();
            if (Button("Clear saved board")) AppData.LastPlayData.Invalidate();
            if (Button("Restore bones")) { AppData.LastPlayData.bonesLost = 0; AppData.LastPlayData.Save(); }

            GUILayout.Space(16);
            GUILayout.Label("Ads (AppLovin MAX)");
            if (Button("Mediation Debugger")) MaxSdk.ShowMediationDebugger();
            if (Button($"Show Rewarded  (ready: {Ads.IsRewardedReady})"))
                Ads.ShowRewarded(earned => Debug.Log($"[Debug] rewarded closed — earned: {earned}"));
            if (Button($"Show Interstitial  (ready: {Ads.IsInterstitialReady})"))
                Ads.ShowInterstitial(() => Debug.Log("[Debug] interstitial closed"));
            if (Button($"Banner: {(Ads.BannerVisible ? "HIDE" : "SHOW")}")) {
                if (Ads.BannerVisible) Ads.HideBanner(); else Ads.ShowBanner();
            }

            GUILayout.Space(16);
            GUILayout.Label("Overlays");
            if (Button($"FPS overlay: {(AppData.ShowFps.Value ? "ON" : "OFF")}")) MBFpsOverlay.Toggle();

            GUILayout.Space(16);
            if (Button("EXIT DEBUG MODE")) { AppData.DebugMode.Value = false; Close(); }
            if (Button("Close")) Close();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // A jump only moves the pointer — the stale saved attempt is ignored anyway because
        // LastPlayData.forLevelIdx no longer matches.
        static void JumpLevel(int delta) =>
            AppData.LevelIdx.Value = Mathf.Clamp(AppData.LevelIdx.Value + delta, 0, Mathf.Max(0, SOLevelsData.Count - 1));

        static bool Button(string label) => GUILayout.Button(label, GUILayout.Height(44));

        static string FirebaseState() {
#if !IGNORE_FIREBASE
            return FirebaseBootstrap.FBAvailable ? "ready" : "NOT READY";
#else
            return "ignored (define)";
#endif
        }
    }
}
