using Common;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {
    public class MBBoostButton : MonoBehaviour {

        public EBoostType BoostType;

        // Set by MBGameplay: runs this boost's effect, returns true if it actually consumed one.
        public System.Func<bool> UseAction;

        private TMP_Text _amountText;
        private int _lastAmount = int.MinValue;
        private bool _lastVideoReady;

        Button _btn = null;
        private Transform _amount;
        private Transform _videoLoading;
        private Transform _showVideo;

        public Button GetButton() {

            if (_btn == null) {
                _btn = GetComponent<Button>();
            }

            return _btn;
        }

        internal void SetInteractable(bool on) {
            GetButton().interactable = on;
        }

        private void Awake() {

            _amount = transform.RecursiveFindChild("$Amount");
            _videoLoading = transform.RecursiveFindChild("$VideoLoading");
            _showVideo = transform.RecursiveFindChild("$ShowVideo");

            _amountText = _amount.RecursiveFindChild<TMPro.TMP_Text>("$AmountText");
            

            // interactable=false only blocks clicks — no gray-out (disabled looks like normal)
            var colors = GetButton().colors;
            colors.disabledColor = colors.normalColor;
            GetButton().colors = colors;

            GetButton().onClick.AddListener(OnClick);
        }

        // Have a boost → spend it (decrement only if the effect actually ran). No boost → watch a
        // rewarded ad, then grant BoosterAddedAfterRewarded. Reuses Ads.WatchToEarn.
        void OnClick() {
            var boost = AppData.Boosts[BoostType];
            if (boost.Value > 0) {
                if (UseAction != null && UseAction()) boost.Value--;
            } else {
                Ads.WatchToEarn(() => boost.Value += GameConfig.BoosterAddedAfterRewarded);
            }
        }

        private void Update() {
            int amount = AppData.Boosts[BoostType].Value;
            bool videoReady = Ads.IsRewardedReady;
            if (amount == _lastAmount && videoReady == _lastVideoReady) return;
            _lastAmount = amount;
            _lastVideoReady = videoReady;

            if (amount > 0) _amountText.text = amount.ToString();

            // has boost → $Amount; no boost → $ShowVideo (ad ready) or $VideoLoading (still loading)
            bool has = amount > 0;
            _amount.gameObject.SetActive(has);
            _showVideo.gameObject.SetActive(!has && videoReady);
            _videoLoading.gameObject.SetActive(!has && !videoReady);
        }
    }
}

