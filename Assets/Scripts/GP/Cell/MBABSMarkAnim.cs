using System.Collections;
using UnityEngine;

namespace qp {
    public abstract class MBABSMarkAnim : MonoBehaviour {

        public virtual void InitOut() {
            gameObject.SetActive(false);
        }

        public abstract void ActIn();
        public abstract void ActOut();
    }
}
