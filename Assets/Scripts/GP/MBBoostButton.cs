using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {
    public class MBBoostButton : MonoBehaviour {

        public EBoostType BoostType;
        private TMP_Text _amountText;
        private int _lastAmount = int.MinValue;

        public Button GetButton() {
            return GetComponent<Button>();
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

