using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {
    public class MBLevelButton : MonoBehaviour {

        public void OnEnable() {
            int n = AppData.LevelIdx + 1;   // endless: keeps counting past the pack size
            transform.RecursiveFindChild<TMP_Text>("$Text").text = $"Level {n}";
        }

        public Button GetButton() {
            return GetComponent<Button>();
        }
    }
}
