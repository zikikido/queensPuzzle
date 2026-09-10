using System.Collections;
using UnityEngine;

namespace qp {
    /// <summary>
    /// The "!" bubble over a puppy that clashes with the queen just placed. Pops in over the
    /// cell, holds while the rule feedback plays, shrinks away and destroys itself.
    /// Prefab: RuleAlert/Resources/RuleAlert — root pops (MBMarkPopIn), $Visual carries the sprite.
    /// </summary>
    public class MBRuleAlert : MonoBehaviour {

        const string ResourcePath = "RuleAlert";
        const float HoldDur = 1.0f;      // matches the card / cell blink length
        const float ShrinkDur = 0.25f;   // eases away, no snap

        static readonly Vector2 Offset = new Vector2(0.28f, 0.34f);   // up-right of the puppy, in cell sizes
        const float Size = 0.5f;                                      // bubble width, in cell sizes

        /// <summary>Spawn a bubble over <paramref name="cell"/> (parented to it — gone with the board).</summary>
        public static void Spawn(Transform cell, float cellSize) {
            var prefab = Resources.Load<MBRuleAlert>(ResourcePath);
            if (prefab == null) { Debug.LogWarning("[MBRuleAlert] missing Resources/" + ResourcePath); return; }

            var alert = Instantiate(prefab, cell);
            alert.transform.localPosition = new Vector3(Offset.x * cellSize, Offset.y * cellSize, 0f);
            alert.transform.Find("$Visual").localScale = Vector3.one * (Size * cellSize);
            alert.StartCoroutine(alert._life());
        }

        IEnumerator _life() {
            GetComponent<MBMarkPopIn>().PopIn();
            yield return new WaitForSecondsRealtime(HoldDur);

            var t = transform;
            for (float e = 0f; e < ShrinkDur; e += Time.unscaledDeltaTime) {
                t.localScale = Vector3.one * (1f - Mathf.SmoothStep(0f, 1f, e / ShrinkDur));
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
