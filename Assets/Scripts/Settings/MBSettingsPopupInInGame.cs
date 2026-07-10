using Common;
using UnityEngine;
using UnityEngine.UI;

namespace qp {
    /// <summary>
    /// The in-game settings popup: the base popup plus a Restart button — interstitial first
    /// (same gate as the fail popup), then a clean-board rebuild of the current level.
    /// </summary>
    public class MBSettingsPopupInInGame : MBSettingsPopup {

        protected override void Awake() {
            base.Awake();
            transform.RecursiveFindChild<Button>("$RestartButton").onClick.AddListener(OnRestart);
        }

        // Interstitial on restart, from GameConfig.StartShowInterAtLevel (+ 1-min cooldown).
        // Show it first, then restart when it closes.
        void OnRestart() {
            if (AppData.LevelIdx.Value + 1 >= GameConfig.StartShowInterAtLevel && Ads.CanShowInterstitial)
                Ads.ShowInterstitial(Restart);
            else
                Restart();
        }

        void Restart() {
            // clean board: without this Replay() restores the saved attempt (wrong queens, bones)
            AppData.LastPlayData.Invalidate();
            Close();   // out animation; Closed → the top bar releases its input lock
            MBGameplay.instance?.Replay();
        }
    }
}
