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
            if (_flipbook == null) _flipbook = GetComponent<MBSpriteFlipbook>();
            _flipbook.StartFrame("Idle");   // begin on the Idle first frame
            GetComponent<MBMarkPopIn>().PopIn();
            MBGameplay.instance.QueenMarkPS.Play(transform.position);
        }

        public override void ActOut() {
            throw new NotImplementedException();
        }
    }
}


