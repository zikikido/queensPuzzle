using Common;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace qp {
    public class MBLobby : MonoBehaviour {

        const string GameplayScene = "Gameplay";

        private void Awake() {
            var lvlBtn = transform.RecursiveFindChild<MBLevelButton>("$LvlButton");
            if (lvlBtn != null) lvlBtn.GetButton().onClick.AddListener(PlayCurrentLevel);
        }

        // Start the player's current level (AppData.LevelIdx). LevelLoader reads that index.
        void PlayCurrentLevel() {
#if UNITY_EDITOR
            // the lobby launches by index — drop any Level Builder playtest token so it isn't reused
            UnityEditor.SessionState.EraseString(LevelLoader.PlayLevelGuidKey);
#endif
            SceneManager.LoadScene(GameplayScene);
        }
    }
}
