using Common;
using UnityEngine;
using UnityEngine.UI;

namespace qp {
    public class MBLobby : MonoBehaviour {

        MBSettingsPopup _settings;   // inactive in the scene by default (its OUT state)

        private void Awake() {
            var lvlBtn = transform.RecursiveFindChild<MBLevelButton>("$LvlButton");
            if (lvlBtn != null) lvlBtn.GetButton().onClick.AddListener(PlayCurrentLevel);

            transform.RecursiveFindChild<Button>("$SettingsBtn").onClick.AddListener(OpenSettings);
        }

        // Open plays the in animation; the popup closes itself (X / BG tap → out animation).
        void OpenSettings() {
            if (_settings == null) {
                _settings = FindAnyObjectByType<MBSettingsPopup>(FindObjectsInactive.Include);
            }

            _settings.Open();
        }

        // Start the player's current level (AppData.LevelIdx). LevelLoader reads that index.
        void PlayCurrentLevel() {
#if UNITY_EDITOR
            // the lobby launches by index — drop any Level Builder playtest token so it isn't reused
            UnityEditor.SessionState.EraseString(LevelLoader.PlayLevelGuidKey);
#endif
            Navigator.Go(Navigator.Gameplay);
        }
    }
}
