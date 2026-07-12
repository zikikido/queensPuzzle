using System.Collections;
using UnityEngine;

namespace qp {
    public class MBXmarkAnim : MBABSMarkAnim {

        MBMarkPopIn _popIn;
        MBMarkShrinkOut _shrinkOut;

        void Awake() {
            _popIn = GetComponent<MBMarkPopIn>();
            _shrinkOut = GetComponent<MBMarkShrinkOut>();
        }

        public override void ActIn() {

            gameObject.SetActive(true);   // first show runs Awake, so the cache is ready below

            StopAllCoroutines();

            _popIn.PopIn();
        }

        public override void ActOut() {

            // if never awake (hidden before the first show)
            if (_popIn == null) Awake();

            _popIn.StopAllCoroutines();
            StartCoroutine(_out());
        }

        IEnumerator _out() {
            yield return _shrinkOut.ShrinkOut();

            gameObject.SetActive(false);
        }
    }
}


