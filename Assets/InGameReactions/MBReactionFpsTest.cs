using UnityEngine;

namespace qp {

    /// <summary>
    /// TEMP — reaction touch test, DELETE after testing. Toggled from the debug win: every
    /// touch/click plays the next IInGameReactionFX (modulo over all effects in the scene)
    /// at the touch position. Ignores the ReactionDirector and all its rules.
    /// </summary>
    public class MBReactionFpsTest : MonoBehaviour {

        static MBReactionFpsTest _instance;
        public static bool Running => _instance != null;

        /// <summary>Debug-win toggle: start/stop from anywhere, no scene setup.</summary>
        public static void Toggle() {
            if (_instance != null) { Destroy(_instance.gameObject); return; }
            _instance = new GameObject("$ReactionFpsTest").AddComponent<MBReactionFpsTest>();
        }

        IInGameReactionFX[] _fx;
        int _i;

        void Update() {
            if (!Input.GetMouseButtonDown(0)) return;

            if (_fx == null) {
                var list = new System.Collections.Generic.List<IInGameReactionFX>();
                foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (mb is IInGameReactionFX fx) list.Add(fx);
                _fx = list.ToArray();
                Debug.Log("[MBReactionFpsTest] " + _fx.Length + " effects");
            }
            if (_fx.Length == 0 || Camera.main == null) return;

            var pos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            pos.z = 0f;
            _fx[_i++ % _fx.Length].Play(pos);
        }

        void OnDestroy() { if (_instance == this) _instance = null; }
    }
}
