using Common;
using System.Collections;
using UnityEngine;
using Common;

namespace qp {
    public class MBWorngQueenMark : MBABSMarkAnim {

        SpriteRenderer _cellWorng;   // red flash over the cell while the bones fly
        GameObject _redX;            // the mark that stays

        void Awake() {
      
        }

        public override void ActIn() {

            _cellWorng = transform.RecursiveFindChild<SpriteRenderer>("$CellWorng");
            _redX = transform.RecursiveFindChild("$RedX").gameObject;

            gameObject.SetActive(true);   // first show runs Awake, so the cache is ready below

            var ps = MBGameplay.instance.WrongQueenMarkPS.Play(transform.position);
            var duration = ps.main.duration;

            StopAllCoroutines();
            StartCoroutine(_in(duration));
        }

        IEnumerator _in(float duration) {
            _redX.SetActive(false);

            // fade in $CellWorng over the first half of the PS...
            yield return _fadeCellWorng(0f, 1f, duration * 0.5f);

            // ...the $RedX lands mid-effect...
            _redX.SetActive(true);

            // ...and the red flash clears with the debris
            yield return _fadeCellWorng(1f, 0f, duration * 0.5f);
        }

        IEnumerator _fadeCellWorng(float from, float to, float duration) {
            var anim = Common.Animators.AnimateNumber(from, to, duration);

            while (anim.MoveNext()) {
                _setCellWorngAlpha(anim.Current);
                yield return null;
            }

            _setCellWorngAlpha(to);
        }

        void _setCellWorngAlpha(float a) {
            var c = _cellWorng.color;
            c.a = Mathf.Clamp01(a);
            _cellWorng.color = c;
        }

        // restore path: the X is simply there — no bone break, no red flash
        public override void InitIn() {
            _cellWorng = transform.RecursiveFindChild<SpriteRenderer>("$CellWorng");
            _redX = transform.RecursiveFindChild("$RedX").gameObject;

            base.InitIn();
            StopAllCoroutines();
            _setCellWorngAlpha(0f);
            _redX.SetActive(true);
        }

        public override void ActOut() {

            // if never shown yet, the children aren't cached
            if (_cellWorng == null) {
                _cellWorng = transform.RecursiveFindChild<SpriteRenderer>("$CellWorng");
                _redX = transform.RecursiveFindChild("$RedX").gameObject;
            }

            StopAllCoroutines();
            _setCellWorngAlpha(0f);
            gameObject.SetActive(false);
        }
    }
}
