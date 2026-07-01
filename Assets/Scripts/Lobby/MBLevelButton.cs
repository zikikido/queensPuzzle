using Common;
using TMPro;
using UnityEngine;

namespace qp {
    public class MBLevelButton : MonoBehaviour {

        public void Awake() {
            transform.RecursiveFindChild<TMP_Text>("$Text").text = $"Level {AppData.LevelIdx.Value + 1}";
        }
    }
}
