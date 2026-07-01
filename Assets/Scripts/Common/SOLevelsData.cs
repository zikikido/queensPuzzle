using UnityEngine;

namespace qp
{
    /// <summary>
    /// Per level-set data — for now just the number of levels. Loaded once from Resources at app
    /// start and cached. (Future: several sets, one asset each.)
    /// </summary>
    [CreateAssetMenu(fileName = "SOLevelsData", menuName = "QueensPuzzle/Levels Data")]
    public class SOLevelsData : ScriptableObject
    {
        public int LevelsCount;   // number of levels in this set

        const string ResourcePath = "SOLevelsData";
        static SOLevelsData _instance;

        public static SOLevelsData Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<SOLevelsData>(ResourcePath);
                    if (_instance == null)
                        Debug.LogError($"[SOLevelsData] Resources/{ResourcePath}.asset not found.");
                }
                return _instance;
            }
        }

        /// <summary>Number of levels (0 if the asset is missing).</summary>
        public static int Count => Instance != null ? Instance.LevelsCount : 0;

        // Warm the cache at startup.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Preload() { _ = Instance; }
    }
}
