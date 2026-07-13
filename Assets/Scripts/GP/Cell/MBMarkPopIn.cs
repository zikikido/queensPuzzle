using System.Collections;
using UnityEngine;
namespace qp {
    public class MBMarkPopIn : MonoBehaviour {

        const float PopDur = 0.15f;     // mark "stamp" in

        Coroutine _anim;

        public void PopIn() {
            if (_anim != null) StopCoroutine(_anim);   // restart clean — never two pops fighting over the scale
            _anim = StartCoroutine(_popIn());
        }

        IEnumerator _popIn() {
            var t = transform;
            t.localScale = Vector3.zero;
            for (float e = 0f; e < PopDur; e += Time.unscaledDeltaTime) {
                t.localScale = Vector3.one * EaseOutBack(e / PopDur);
                yield return null;
            }
            t.localScale = Vector3.one;
            _anim = null;
        }

        // overshoot ease (matches the board bloom): 0 -> past 1 -> settles to 1
        static float EaseOutBack(float x) {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float p = x - 1f;
            return 1f + c3 * p * p * p + c1 * p * p;
        }
    }
}
