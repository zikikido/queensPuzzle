using UnityEngine;

namespace qp {

    // "Level 1" and "Level 10000" are not the same width — $LevelText is left-aligned inside a
    // fixed box, so the pair drifts off-center as digits are added. We shift the whole Level
    // container on X by a hand-tuned amount per digit count. MBTopBar calls Recenter() right
    // after it writes the text; nothing runs per-frame.
    public class MBLevelCenterizer : MonoBehaviour {

        [SerializeField] float _x1Digit;
        [SerializeField] float _x2Digits;
        [SerializeField] float _x3Digits;
        [SerializeField] float _x4Digits;
        [SerializeField] float _x5Digits;

        RectTransform _rt;
        float _authoredX;   // kept for text that isn't a plain number (the daily date)

        private void Awake() {
            _rt = (RectTransform)transform;
            _authoredX = _rt.anchoredPosition.x;
        }

        // Anything outside 1-5 digits stays at the position authored in the scene.
        public void Recenter(string text) {
            float x;
            switch (_digitCount(text)) {
                case 1: x = _x1Digit; break;
                case 2: x = _x2Digits; break;
                case 3: x = _x3Digits; break;
                case 4: x = _x4Digits; break;
                case 5: x = _x5Digits; break;
                default: x = _authoredX; break;
            }
            _rt.anchoredPosition = new Vector2(x, _rt.anchoredPosition.y);
        }

        private static int _digitCount(string text) {
            if (string.IsNullOrEmpty(text)) return 0;
            foreach (var c in text)
                if (!char.IsDigit(c)) return 0;
            return text.Length;
        }
    }
}
