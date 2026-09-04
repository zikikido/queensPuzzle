using Common;
using System.Collections;
using System.Globalization;
using UnityEngine;

namespace qp {
    public class MBWinPopup : MonoBehaviour {

        const float DailyYAlone = -28f;   // $DailyChallange y when it's the only row

        CanvasGroup _group;
        bool _showing;      // a real win is on screen (guards the layout pass from hiding it)
        float _dailyY = float.NaN;   // $DailyChallange's authored y, cached on first daily show

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

        // Social-proof block ($Achievement): $Fast/$Bones carry the ONE strongest comparison
        // from WinAchievement.Pick in their $Text; $DailyChallange shows the run's clock and
        // exists only on daily-challenge wins. Runs in OnEnable so it's fresh on every show;
        // the layout pass (_showing == false) hides the whole block.
        void OnEnable() {
            var root = transform.RecursiveFindChild("$Achievement");
            if (root == null) return;

            var a = _showing ? WinAchievement.Pick() : WinAchievement.None;
            bool daily = _showing && DailyChallengeManager.InDailyRun;

            root.gameObject.SetActive(daily || a.Type != EWinAchievement.None);

            _SetRow(root, "$Fast", a.Type == EWinAchievement.Time, a);
            _SetRow(root, "$Bones", a.Type == EWinAchievement.NoBones, a);

            var dailyRow = root.RecursiveFindChild("$DailyChallange");
            if (dailyRow != null) {
                dailyRow.gameObject.SetActive(daily);
                if (daily) {
                    var time = dailyRow.RecursiveFindChild<TMPro.TMP_Text>("$Time");
                    if (time != null) time.text = _Clock(DailyChallengeManager.State.timeSec);

                    // Alone (no achievement row underneath) the daily row drops toward center.
                    var rt = (RectTransform)dailyRow;
                    if (float.IsNaN(_dailyY)) _dailyY = rt.anchoredPosition.y;   // authored y
                    rt.anchoredPosition = new Vector2(rt.anchoredPosition.x,
                        a.Type == EWinAchievement.None ? DailyYAlone : _dailyY);
                }
            }
        }

        void _SetRow(Transform root, string row, bool on, WinAchievement a) {
            var t = root.RecursiveFindChild(row);
            if (t == null) return;
            t.gameObject.SetActive(on);
            if (!on) return;
            var text = t.RecursiveFindChild<TMPro.TMP_Text>("$Text");
            if (text != null) text.text = _AchievementText(a);
        }

        // The percent is the hero of the line: gold, oversized and stroked like the win title
        // (the material tag resolves via Resources/Fonts & Materials/), trailing zeros trimmed
        // (90.00 -> "90", 90.10 -> "90.1").
        static string _AchievementText(WinAchievement a) {
            string pct = $"<size=91><color=#FFB300><material=\"Mitr-SemiBold Win Text\">{a.Pct.ToString("0.##", CultureInfo.InvariantCulture)}%</material></color></size>";
            return a.Type switch {
                EWinAchievement.Time     => $"Faster than {pct} of players!",
                EWinAchievement.NoBones  => $"Better than {pct} of players!",
                _ => "",
            };
        }

        static string _Clock(float sec) {
            int s = Mathf.Max(0, Mathf.RoundToInt(sec));
            return $"{s / 60:00}:{s % 60:00}";
        }

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
