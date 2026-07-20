using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Gameplay-only sounds: CommonSFX.Play(GPSFX.Instance.PlaceQueen). Loaded lazily from
    /// Resources/GPSFX on first use in the scene; Navigator calls Release() when leaving
    /// Gameplay, and the Single-mode scene load that follows frees the clips from memory.
    /// </summary>
    [CreateAssetMenu(fileName = "GPSFX", menuName = "QueensPuzzle/GP SFX")]
    public class GPSFX : ScriptableObject {

        public AudioClip PlaceQueen;
        public AudioClip XMark;
        public AudioClip Erase;
        public AudioClip Happy;
        public AudioClip Error;
        public AudioClip Win;
        public AudioClip Fail;
        public AudioClip Hint;
        public AudioClip BoardStart;
        public AudioClip PuppyWin;
        public AudioClip PuppyLose;

        const string ResourcePath = "GPSFX";
        static GPSFX _instance;

        public static GPSFX Instance {
            get {
                if (_instance == null) {
                    _instance = Resources.Load<GPSFX>(ResourcePath);
                    if (_instance == null)
                        CDebug.LogError($"[GPSFX] Resources/{ResourcePath}.asset not found.");
                }
                return _instance;
            }
        }

        /// <summary>Warm the cache up front (MBGameplay.Awake) instead of on the first Play.</summary>
        public static void Load() {
            _ = Instance;
        }

        /// <summary>Drop the cached asset so the next scene load can free its clips.</summary>
        public static void Release() {
            _instance = null;
        }
    }
}
