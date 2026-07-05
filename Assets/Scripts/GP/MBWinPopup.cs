using Common;
using System.Collections;
using UnityEngine;

namespace qp {
    public class MBWinPopup : MonoBehaviour {

        CanvasGroup _group;

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

        // $FirstTry celebrates winning the level on the first attempt (counter kept in AppData;
        // it only resets when a NEW level starts, so it's still valid while the popup shows).
        void OnEnable() {
            var firstTry = transform.RecursiveFindChild("$FirstTry");
            if (firstTry != null) firstTry.gameObject.SetActive(AppData.LevelAttempts.Value <= 1);
        }

        IEnumerator Start() {
            // wait for UI to refresh our layout
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            _group.alpha = 1f;
            gameObject.SetActive(false);   // hidden until Win() shows it
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
