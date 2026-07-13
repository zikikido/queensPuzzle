using System.Collections;
using UnityEngine;

namespace qp {
    public abstract class MBABSMarkAnim : MonoBehaviour {

        public virtual void InitOut() {
            gameObject.SetActive(false);
        }

        /// <summary>Show INSTANTLY — no pop-in, no particles, no fades. Board setup (restore,
        /// revealed queens, before the bloom) uses this; ActIn stays the played-move entrance.</summary>
        public virtual void InitIn() {
            transform.localScale = Vector3.one;   // in case a pop-in was interrupted mid-scale
            gameObject.SetActive(true);
        }

        public abstract void ActIn();
        public abstract void ActOut();
    }
}
