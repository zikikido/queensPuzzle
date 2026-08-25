using System;
using UnityEngine;

namespace qp {
    public class MBQueenMark : MBABSMarkAnim {

        // the Spine player sits on the "Visual" CHILD: MBMarkPopIn pops the root scale to 1,
        // so the skeleton's own scale must live one level down where the pop can't stomp it
        private MBSpinePlayer _player;

        private void Awake() {
            _player = GetComponentInChildren<MBSpinePlayer>(true);
        }

        internal void Play(string v) {
            _player.Play(v);
        }

        internal float StateLength(string v) {

            // if never awake
            if (_player == null) {
                _player = GetComponentInChildren<MBSpinePlayer>(true);
            }

            return _player.StateLength(v);
        }

        public override void InitIn() {
            base.InitIn();
            if (_player == null) _player = GetComponentInChildren<MBSpinePlayer>(true);
            _player.StartFrame("Idle");   // restored queens hold the Idle first frame, not the bind pose
        }

        public override void ActIn() {
            gameObject.SetActive(true);
            if (_player == null) _player = GetComponentInChildren<MBSpinePlayer>(true);
            _player.StartFrame("Idle");   // begin on the Idle first frame
            GetComponent<MBMarkPopIn>().PopIn();
            MBGameplay.instance.QueenMarkPS.Play(transform.position);
        }

        public override void ActOut() {
            throw new NotImplementedException();
        }
    }
}
