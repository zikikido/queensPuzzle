using Common;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace qp {
    public class MBFailPopup : MonoBehaviour {

        CanvasGroup _group;
        bool _showing;   // a real fail is on screen (guards the layout pass from hiding it)
        GameObject _btnContinue;   // rewarded revive — only offered when a rewarded ad is ready

        /// <summary>The one way to open the popup (MBGameplay.Fail).</summary>
        public void Show() {
            _showing = true;
            _group.alpha = 1f;   // whatever the layout pass left behind, a real show is opaque
            gameObject.SetActive(true);
            UpdateContinueButton();
        }

        // Same trick as the win popup: stay ACTIVE but invisible for the first frames so the UI
        // lays out at real size; only after the layout cycles hide for real. Keep the popup
        // active in the scene — this script turns it off by itself.
        private void Awake() {
            var back = transform.RecursiveFindChild<Button>("$BtnBack");
            back.onClick.AddListener(ResetToLobby);

            var btnContinue = transform.RecursiveFindChild<Button>("$BtnContinue");
            btnContinue.onClick.AddListener(Continue);
            _btnContinue = btnContinue.gameObject;

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
            // Interstitial on restart, from GameConfig.StartShowInterAtLevel (+ 1-min cooldown).
            // Show it first, then go to the lobby when it closes.
            if (AppData.LevelIdx.Value + 1 >= GameConfig.StartShowInterAtLevel && Ads.CanShowInterstitial)
                Ads.ShowInterstitial(() => MBGameplay.instance?.Replay());
            else
                MBGameplay.instance?.Replay();
        }

        // Continue is a rewarded revive — only shown when a rewarded ad is ready.
        void Update() {
            if (_showing) UpdateContinueButton();
        }

        void UpdateContinueButton() {
            if (_btnContinue == null) return;
            bool ready = Ads.IsRewardedReady;
            if (_btnContinue.activeSelf != ready) _btnContinue.SetActive(ready);
        }

        // Continue — watch a rewarded ad; only revive (bones refill) if the reward is granted.
        void Continue() {
            Ads.WatchToEarn(() => {
                _showing = false;
                gameObject.SetActive(false);
                MBGameplay.instance?.ContinueAfterFail();
            });
        }
    }
}
