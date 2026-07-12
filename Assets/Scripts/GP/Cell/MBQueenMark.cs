using System;
using UnityEngine;

namespace qp {
    public class MBQueenMark : MBABSMarkAnim {

        private MBSpriteFlipbook _flipbook;

        private void Awake() {
            _flipbook = GetComponent<MBSpriteFlipbook>();
        }

        internal void Play(string v) {
            _flipbook.Play(v);
        }

        internal float StateLength(string v) {

            // if never awake
            if (_flipbook == null) {
                _flipbook = GetComponent<MBSpriteFlipbook>();
            }

            return _flipbook.StateLength(v);
        }

        public override void ActIn() {
            gameObject.SetActive(true);
            GetComponent<MBMarkPopIn>().PopIn();
            MBGameplay.instance.QueenMarkPS.Play(transform.position);
        }

        public override void ActOut() {
            throw new NotImplementedException();
        }
    }
}


