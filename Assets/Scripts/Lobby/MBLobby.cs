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
            if (card != null) card.Tapped += OnTournamentTapped;
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
        /// The card was tapped. Which state it was in decides whether there is anything to open:
        /// a locked card is an explanation, not a door, and a hidden one cannot be tapped at all.
        /// </summary>
        void OnTournamentTapped(ETournamentStatus status) {
            if (status == ETournamentStatus.None || status == ETournamentStatus.Locked) return;

            // TODO: Ended should collect the prize first, then show the final table.
            StartCoroutine(ShowLeaderboard());
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
