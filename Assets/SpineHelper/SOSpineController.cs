using System;
using Spine.Unity;
using UnityEngine;

namespace qp {
    /// <summary>
    /// The Spine twin of SOFlipbookController: a list of named states. Each state plays one
    /// animation from the Spine export (by its name inside the .json) and either LOOPS, or
    /// chains into the NEXT state (by name, after an optional delay) — or just stops there.
    /// MBSpinePlayer takes one of these and plays states by name.
    /// </summary>
    [CreateAssetMenu(fileName = "SpineController", menuName = "QueensPuzzle/Spine Controller")]
    public class SOSpineController : ScriptableObject {

        [Serializable]
        public class State {
            public string name;           // Play("name") plays this state
            [SpineAnimation(dataField: "skeletonDataAsset")]
            public string spineAnim;      // animation name inside the Spine export (e.g. "happy")
            [Tooltip("Playback speed: 1 = normal, 2 = twice as fast, 0.5 = half speed. Scales the clip and the loop/next delays.")]
            public float speed = 1f;      // time scale for this state
            public bool loop;             // loops forever (no next)
            public float loopDelay;       // looping: extra rest on the end pose between cycles
            public string next = "";      // not looping: state to enter when done; empty = hold the last pose
            public float nextDelay;       // extra wait on the last pose before entering next
            [Tooltip("Crossfade seconds when entering this state. 0 = snap, like the flipbook.")]
            public float mix;             // blend-in from whatever was playing before
            [Tooltip("Crossfade override used when THIS state chains into 'next'. -1 = use the next state's own mix.")]
            public float nextMix = -1f;   // per-chain override, so Happy→Idle can blend differently than Cry→Idle
        }

        [Tooltip("Editor only: the export this character plays — fills the spineAnim dropdowns below. Not read at runtime.")]
        public SkeletonDataAsset skeletonDataAsset;

        public string defaultState;       // entered on enable; empty = NONE (nothing auto-plays)
        public State[] states;

        public State Find(string stateName) {
            if (states == null) return null;
            foreach (var s in states)
                if (s != null && string.Equals(s.name, stateName, StringComparison.OrdinalIgnoreCase))
                    return s;
            return null;
        }

        public State Default => string.IsNullOrEmpty(defaultState) ? null : Find(defaultState);
    }
}
