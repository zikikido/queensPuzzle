using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Lives on the Loading-scene MusicStarter object: Awake makes the referenced
    /// MBBGMusicV2 persistent, and OnDestroy — which fires when the Loading scene is
    /// torn down — starts playback, so the loading screen itself stays silent.
    /// </summary>
    public class MBMusic : MonoBehaviour {

        [SerializeField] MBBGMusicV2 _bg;

        public static MBBGMusicV2 BG { get; private set; }

        // set only on the instance that owns BG, so a duplicate's OnDestroy stays silent
        bool _armed;

        void Awake() {
            if (BG != null) {
                // Loading reloaded: a speaker already persists, drop the duplicates
                Destroy(_bg.gameObject);
                Destroy(gameObject);
                return;
            }

            BG = _bg;
            DontDestroyOnLoad(_bg.gameObject);
            _armed = true;
        }

        void OnDestroy() {
            // the guard covers app quit / editor stop, where playing makes no sense
            if (_armed && BG != null && BG.isActiveAndEnabled)
                BG.Play(true, 2);
        }
    }
}
