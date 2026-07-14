using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Project-side owner of the one MBBGMusicV2: lives on the $Music GameObject in the
    /// Loading scene, survives scene switches, and destroys duplicates if Loading reloads.
    /// </summary>
    [RequireComponent(typeof(MBBGMusicV2))]
    public class MBMusic : MonoBehaviour {

        public static MBBGMusicV2 BG { get; private set; }

        void Awake() {
            if (BG != null) {
                Destroy(gameObject);
                return;
            }

            BG = GetComponent<MBBGMusicV2>();
            DontDestroyOnLoad(gameObject);
        }
    }
}
