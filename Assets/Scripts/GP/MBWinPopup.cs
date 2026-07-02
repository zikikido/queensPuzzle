using Common;
using UnityEngine;

namespace qp {
    public class MBWinPopup : MonoBehaviour {

        private void Awake() {
            var levelBtn = transform.RecursiveFindChild<MBLevelButton>("$LvlButton");
            if (levelBtn != null) levelBtn.GetButton().onClick.AddListener(Next);
        }

        // Rebuild the board with the now-current level (LevelIdx was advanced on win) — no scene
        // reload, so the scene and the banner ad stay loaded.
        void Next() {
            gameObject.SetActive(false);
            var gp = FindAnyObjectByType<MBGameplay>();
            if (gp != null) gp.Replay();
        }
    }
}
