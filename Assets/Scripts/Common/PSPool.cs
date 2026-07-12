using System.Collections.Generic;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Pool over one scene ParticleSystem template: Play() grabs a free instance — cloning the
    /// template next to it when all are busy — moves it to a world position and fires. Scale is
    /// inherited from the template's parent (keep it under the same scaled ancestor as its
    /// targets). Instances return to the pool by deactivating themselves when the system
    /// finishes (stopAction = Disable), so "free" simply means "inactive".
    /// </summary>
    public class PSPool {

        readonly ParticleSystem _template;
        readonly Transform _parent;
        readonly List<ParticleSystem> _all = new List<ParticleSystem>();

        public PSPool(ParticleSystem template) {
            _template = template;
            _parent = template.transform.parent;
            Adopt(template);   // the template itself is instance #0
        }

        void Adopt(ParticleSystem ps) {
            var main = ps.main;
            main.stopAction = ParticleSystemStopAction.Disable;   // self-deactivate = back to pool
            ps.gameObject.SetActive(false);
            _all.Add(ps);
        }

        ParticleSystem Take() {
            foreach (var ps in _all)
                if (ps != null && !ps.gameObject.activeSelf) return ps;
            var clone = Object.Instantiate(_template, _parent);
            Adopt(clone);
            return clone;
        }

        /// <summary>Fire one instance at a world position.</summary>
        public ParticleSystem Play(Vector3 worldPos) {
           
            var ps = Take();
            ps.transform.position = worldPos;
            ps.gameObject.SetActive(true);
            ps.Play();

            return ps;
        }
    }
}
