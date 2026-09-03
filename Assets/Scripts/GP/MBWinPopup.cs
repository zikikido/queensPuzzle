using Common;
using System.Collections;
using UnityEngine;

namespace qp {
    public class MBWinPopup : MonoBehaviour {

        CanvasGroup _group;
        bool _showing;   // a real win is on screen (guards the layout pass from hiding it)

        /// <summary>The one way to open the popup (MBGameplay.Win).</summary>
        public void Show() {
            _showing = true;
            _group.alpha = 1f;   // whatever the layout pass left behind, a real show is opaque
            gameObject.SetActive(true);
            GetComponent<IPopupAnim>()?.PlayIn();   // the reskin animates the open, if it has one
        }

        // Same trick as the tutorial: stay ACTIVE but invisible for the first frames so the UI
        // lays out at real size; only after the layout cycles hide for real. Keep the popup
        // active in the scene — this script turns it off by itself.
        private void Awake() {
            var levelBtn = transform.RecursiveFindChild<MBLevelButton>("$LvlButton");
            if (levelBtn != null) levelBtn.GetButton().onClick.AddListener(Next);

            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;   // invisible, but alive for the layout pass
        }

        // Achievement line ($Achievement > $Text): the win's ONE social-proof string, plain text
        // for now (dedicated icons pending art — see the Win Achievement brief). Works in both
        // modes — Pick reads daily time/attempts from DailyChallengeManager.State. Runs in
        // OnEnable so it's fresh on every show; the layout pass (_showing == false) hides the row.
        void OnEnable() {
            var root = transform.RecursiveFindChild("$Achievement");
            if (root == null) return;

            var a = _showing ? WinAchievement.Pick() : WinAchievement.None;

            root.gameObject.SetActive(a.Type != EWinAchievement.None);
            if (a.Type == EWinAchievement.None) return;

            var text = root.RecursiveFindChild<TMPro.TMP_Text>("$Text");
            if (text != null) text.text = AchievementText(a);
        }

        static string AchievementText(WinAchievement a) => a.Type switch {
            EWinAchievement.Time     => $"FASTER THAN {a.Pct:0.#}% OF PLAYERS!",
            EWinAchievement.NoBones  => $"BETTER THAN {a.Pct:0.#}% OF PLAYERS!",
            EWinAchievement.FirstTry => $"BETTER THAN {a.Pct:0.#}% OF PLAYERS!",
            _ => "",
        };

        IEnumerator Start() {
            // wait for UI to refresh our layout
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            _group.alpha = 1f;
            if (!_showing) gameObject.SetActive(false);   // layout pass only — hidden until Win()
        }

        // Campaign: rebuild the board with the now-current level (LevelIdx was advanced on win) —
        // no scene reload, so the scene and the banner ad stay loaded.
        // Daily: there is no next level today — back to the lobby (which shows the Done card).
        void Next() {
            _showing = false;
            gameObject.SetActive(false);

            // Interstitial between levels, from GameConfig.StartShowInterAtLevel (+ 1-min cooldown).
            // Show it first, then continue when it closes.
            if (AppData.LevelIdx.Value + 1 >= GameConfig.StartShowInterAtLevel && Ads.CanShowInterstitial)
                Ads.ShowInterstitial(Continue);
            else
                Continue();
        }

        void Continue() {
            if (DailyChallengeManager.InDailyRun) {
                DailyChallengeManager.ExitDaily();
                Navigator.Go(Navigator.Lobby);
                return;
            }
            var gp = FindAnyObjectByType<MBGameplay>();
            if (gp != null) gp.Replay();
        }
    }
}
