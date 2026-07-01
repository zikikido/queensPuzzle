using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {
    public class MBLevelButton : MonoBehaviour {

        public void Awake() {
            int n = Mathf.Min(AppData.LevelIdx + 1, SOLevelsData.Count);   // next level, capped at the max
            transform.RecursiveFindChild<TMP_Text>("$Text").text = $"Level {n}";
        }

        public Button GetButton() {
            return GetComponent<Button>();
        }
    }
}
