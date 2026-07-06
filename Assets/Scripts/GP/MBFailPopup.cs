using Common;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace qp {
    public class MBFailPopup : MonoBehaviour {

        CanvasGroup _group;
        bool _showing;   // a real fail is on screen (guards the layout pass from hiding it)

        /// <summary>The one way to open the popup (MBGameplay.Fail).</summary>
        public void Show() {
            _showing = true;
            _group.alpha = 1f;   // whatever the layout pass left behind, a real show is opaque
            gameObject.SetActive(true);
        }

        // Same trick as the win popup: stay ACTIVE but invisible for the first frames so the UI
        // lays out at real size; only after the layout cycles hide for real. Keep the popup
        // active in the scene — this script turns it off by itself.
        private void Awake() {
            var back = transform.RecursiveFindChild<Button>("$BtnBack");
            back.onClick.AddListener(ResetToLobby);

            var btnContinue = transform.RecursiveFindChild<Button>("$BtnContinue");
            btnContinue.onClick.AddListener(Continue);

            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;   // invisible, but alive for the layout pass
        }

        IEnumerator Start() {
            // wait for UI to refresh our layout
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            _group.alpha = 1f;
            if (!_showing) gameObject.SetActive(false);   // layout pass only — hidden until Show()
        }

        // Reset — abandon the attempt, back to the lobby.
        void ResetToLobby() {
            SceneManager.LoadScene("Lobby");
        }

        // Continue — every bone returns (the wrong queens stay as permanent X's), resume play.
        void Continue() {
            _showing = false;
            gameObject.SetActive(false);
            MBGameplay.instance?.ContinueAfterFail();
        }
    }
}
