using System.Collections;
using UnityEngine;
namespace qp {
    public class MBMarkPopIn : MonoBehaviour {

        // mark "stamp" in — 0.28s matches the playable ad's pop. Serialized because the
        // same component drives both the X and the puppy: a slower stamp reads well on an
        // X but can feel sluggish when seating the eighth puppy of a big level.
        [SerializeField] float popDur = 0.28f;

        Coroutine _anim;

        public void PopIn() {
            if (_anim != null) StopCoroutine(_anim);   // restart clean — never two pops fighting over the scale
            _anim = StartCoroutine(_popIn());
        }

        IEnumerator _popIn() {
            var t = transform;
            var dur = popDur > 0f ? popDur : 0.28f;
            t.localScale = Vector3.zero;
            for (float e = 0f; e < dur; e += Time.unscaledDeltaTime) {
                t.localScale = Vector3.one * EaseOutBack(e / dur);
                yield return null;
            }
            t.localScale = Vector3.one;
            _anim = null;
        }

        // overshoot ease (matches the board bloom): 0 -> past 1 -> settles to 1
        // c1 2.6 peaks around 1.15, the same overshoot the ad's pop keyframe uses
        static float EaseOutBack(float x) {
            const float c1 = 2.6f;
            const float c3 = c1 + 1f;
            float p = x - 1f;
            return 1f + c3 * p * p * p + c1 * p * p;
        }
    }
}
