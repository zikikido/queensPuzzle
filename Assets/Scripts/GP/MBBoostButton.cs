using Common;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {
    public class MBBoostButton : MonoBehaviour {

        public EBoostType BoostType;
        private TMP_Text _amountText;
        private int _lastAmount = int.MinValue;

        Button _btn = null;

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
            _amountText = transform.RecursiveFindChild<TMPro.TMP_Text>("$AmountText");
        }

        private void Update() {

            var dataAmount = AppData.Boosts[BoostType].Value;

            if (_lastAmount != dataAmount) {
                _amountText.text = dataAmount.ToString();
                _lastAmount = dataAmount;
            }
        }
    }
}

