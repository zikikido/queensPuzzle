using UnityEngine;

namespace Common {

    /// <summary>
    /// Ordered BG tracks stored as Resources-relative paths (no direct AudioClip refs, so the
    /// asset itself pulls nothing into memory). Edit through its custom inspector: drop an
    /// AudioClip and it is validated + converted to a path string.
    /// </summary>
    [CreateAssetMenu(fileName = "BGMusicPlaylist", menuName = "Common/BG Music Playlist")]
    public class BGMusicPlaylist : ScriptableObject {

        public string[] ResourcePaths = new string[0];
    }
}
