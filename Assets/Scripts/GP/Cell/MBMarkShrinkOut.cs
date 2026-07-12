using System.Collections;
using UnityEngine;
namespace qp {
    public class MBMarkShrinkOut : MonoBehaviour {

        const float ShrinkDur = 0.1f;   // mark shrink out (erase)

        public IEnumerator ShrinkOut() {
            var t = transform;
            float start = Mathf.Max(t.localScale.x, 0.0001f);
            for (float e = 0f; e < ShrinkDur; e += Time.unscaledDeltaTime) {
                t.localScale = Vector3.one * Mathf.Lerp(start, 0f, e / ShrinkDur);
                yield return null;
            }

            t.localScale = Vector3.zero;
        }
    }
}