using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Name rules, shipped as a single asset in Resources ("ProfileConfig"). Latin letters and
    /// digits only: our font atlas has nothing else, and a name the game can't draw is worse than
    /// one the player didn't pick. The banned list is the first line of moderation — the same check
    /// belongs on the server too, since a client can always lie.
    ///
    /// Singleton: Resources.Load may only run on the main thread, so the asset is loaded once at
    /// boot and every later read is the cached reference.
    /// </summary>
    [CreateAssetMenu(menuName = "QP/Profile Config", fileName = "ProfileConfig")]
    public class ProfileConfig : ScriptableObject {

        const string Resource = "ProfileConfig";

        [Tooltip("Shortest name a player may pick.")]
        public int minNameLength = 4;

        [Tooltip("Longest name a player may pick — the leaderboard row has to fit it.")]
        public int maxNameLength = 14;

        // A generated name is one of each, with a space: "Lucky Paw". Warm and readable rather than
        // a gamer handle, and no number unless two players in the same group collide.
        [Tooltip("First word of a generated name.")]
        public string[] nameAdjectives;

        [Tooltip("Second word of a generated name.")]
        public string[] nameNouns;

        // Two lists, because one rule can't do both jobs. Checked on the NORMALISED name (lowercase,
        // letters only, leet digits folded back, repeats squeezed), and the same check has to run on
        // the server — a client can always lie.
        [Tooltip("Refused anywhere inside the name: strings with no innocent host (fuck, nazi…).")]
        public string[] bannedAnywhere;

        [Tooltip("Refused only as a whole word — as a substring they would kill innocent names " +
                 "(ass in Cassandra, cum in document, rape in grape, tit in title). Also refused " +
                 "when the whole name is just that word repeated, which is how SexSex sneaks past.")]
        public string[] bannedWholeWord;

        // ---- singleton ------------------------------------------------------------------

        static ProfileConfig _instance;

        /// <summary>The loaded asset, or null if it isn't shipped. Readable from any thread.</summary>
        public static ProfileConfig Instance {
            get {
#if UNITY_EDITOR
                if (_instance == null && !Application.isPlaying) _load();   // editor tools, no boot ran
#endif
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void _load() {
            _instance = Resources.Load<ProfileConfig>(Resource);
            if (_instance == null)
                CDebug.LogError($"[ProfileConfig] Resources/{Resource}.asset not found - names are off.");
        }
    }
}
