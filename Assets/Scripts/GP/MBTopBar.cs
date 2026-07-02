using Common;
using UnityEngine;

namespace qp {
    public class MBTopBar : MonoBehaviour {

        TMPro.TMP_Text _queensProgressText;

        [SerializeField]
        Color _queenFoundCLR ;
        private int _queensInBoard;
        private string _foundHex;

        private void Awake() {
            transform.RecursiveFindChild("$QueensProgressText", out _queensProgressText);
            _foundHex = ColorUtility.ToHtmlStringRGB(_queenFoundCLR);
        }

        public void Init(int queensInBoard) {
            _queensInBoard = queensInBoard;
        }

        private void _setQueensProgress(int done) {
            _queensProgressText.text = $"<color=#{_foundHex}>{done}</color>/{_queensInBoard}";
        }
    }
}

