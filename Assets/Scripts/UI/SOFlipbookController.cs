using System;
using UnityEngine;

namespace qp {
    /// <summary>
    /// A simple flipbook "animator controller" (like Unity's, minus the graph UI): a list of
    /// named states. Each state plays a SOFlipbookAnim and either LOOPS, or chains into the
    /// NEXT state (by name, after an optional delay on the last frame) — or just stops there.
    /// MBSpriteFlipbook takes one of these and plays states by name.
    /// </summary>
    [CreateAssetMenu(fileName = "FlipbookController", menuName = "QueensPuzzle/Flipbook Controller")]
    public class SOFlipbookController : ScriptableObject {

        [Serializable]
        public class State {
            public string name;           // Play("name") plays this state
            public SOFlipbookAnim anim;   // the frames + timings
            public bool loop;             // loops forever (no next)
            public float loopDelay;       // looping: extra rest on the first frame between cycles
            public string next = "";      // not looping: state to enter when done; empty = hold the last frame
            public float nextDelay;       // extra wait on the last frame before entering next
        }

        public string defaultState;       // entered on enable; empty = the first state
        public State[] states;

        public State Find(string stateName) {
            if (states == null) return null;
            foreach (var s in states)
                if (s != null && string.Equals(s.name, stateName, StringComparison.OrdinalIgnoreCase))
                    return s;
            return null;
        }

        public State Default =>
            !string.IsNullOrEmpty(defaultState) && Find(defaultState) != null
                ? Find(defaultState)
                : (states != null && states.Length > 0 ? states[0] : null);
    }
}
