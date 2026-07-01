using UnityEngine;
using UnityEngine.UI;

namespace qp {
    public class MBBoostButton : MonoBehaviour {

        public EBoostType BoostType;

        public Button GetButton() {
            return GetComponent<Button>();
        }

    }
}

