using System.Collections;
using Common;
using UnityEngine;
using UnityEngine.UI;

namespace qp {
    public class MBLobby : MonoBehaviour {

        MBSettingsPopup _settings;   // inactive in the scene by default (its OUT state)
        MBTournamentLeaderboard _leaderboard;

        private void Awake() {
            var lvlBtn = transform.RecursiveFindChild<MBLevelButton>("$LvlButton");
            if (lvlBtn != null) lvlBtn.GetButton().onClick.AddListener(PlayCurrentLevel);

            transform.RecursiveFindChild<Button>("$SettingsBtn").onClick.AddListener(OpenSettings);

            var streakProgressPopup = FindAnyObjectByType<MBDailyStreakInfoPopup>(FindObjectsInactive.Include);
            if (streakProgressPopup != null) streakProgressPopup.gameObject.SetActive(true);
            else Debug.LogError("[MBGameplay] MBDailyStreakInGamePopup missing in the scene");

            // Awake so it lays out at real size and hides itself, the same deal the streak popup
            // makes. A screen first measured while hidden comes up with zero-high rows.
            _leaderboard = FindAnyObjectByType<MBTournamentLeaderboard>(FindObjectsInactive.Include);
            if (_leaderboard != null) _leaderboard.gameObject.SetActive(true);
            else Debug.LogError("[MBLobby] MBTournamentLeaderboard missing in the scene");

            var card = FindAnyObjectByType<MBTournamentCard>(FindObjectsInactive.Include);
            if (card != null) {
                card.Tapped += OnTournamentTapped;
                card.PlayTapped += PlayCurrentLevel;
            }
            else Debug.LogError("[MBLobby] MBTournamentCard missing in the scene");
        }

        // Open plays the in animation; the popup closes itself (X / BG tap → out animation).
        void OpenSettings() {
            if (_settings == null) {
                _settings = FindAnyObjectByType<MBSettingsPopup>(FindObjectsInactive.Include);
            }

            _settings.Open();
        }

        /// <summary>
        /// The card was tapped. Only a card with a table behind it opens one.
        ///
        /// A locked card is an explanation, not a door. Offline has no table worth showing — the
        /// last one may be hours old. And a player who has not joined has nothing of their own to
        /// look at, so on that card only the Play button does anything at all.
        /// </summary>
        void OnTournamentTapped(ETournamentStatus status) {
            switch (status) {
                case ETournamentStatus.Active:
                case ETournamentStatus.EndingSoon:
                case ETournamentStatus.Calculating:
                    StartCoroutine(ShowLeaderboard());
                    break;

                case ETournamentStatus.Ended:
                    // TODO: collect the prize first, then show the final table.
                    StartCoroutine(ShowLeaderboard());
                    break;
            }
        }

        IEnumerator ShowLeaderboard() {
            if (_leaderboard == null) yield break;

            var showing = _leaderboard.Show();
            yield return showing;

            // The one thing the leaderboard hands back: they want to play. Everything else it
            // dealt with itself.
            if (showing.Result == ETournamentScreenResult.Play) PlayCurrentLevel();
        }

        // Start the player's current level (AppData.LevelIdx). LevelLoader reads that index.
        void PlayCurrentLevel() {
            DailyChallengeManager.ExitDaily();   // campaign play — make sure no daily run is flagged
#if UNITY_EDITOR
            // the lobby launches by index — drop any Level Builder playtest token so it isn't reused
            UnityEditor.SessionState.EraseString(LevelLoader.PlayLevelGuidKey);
#endif
            Navigator.Go(Navigator.Gameplay);
        }
    }
}
