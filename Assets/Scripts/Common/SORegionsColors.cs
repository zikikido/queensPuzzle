using UnityEngine;

namespace qp
{
    /// <summary>
    /// Per-region colour palette, loaded once from <c>Resources/SORegionsColors.asset</c> and cached.
    /// Use the static API: <see cref="Instance"/> lazily loads on first access, and it's also warmed
    /// at app start so the first board build doesn't pay the load cost.
    /// </summary>
    public class SORegionsColors : ScriptableObject
    {
        public Color[] Colors;

        const string ResourcePath = "Reskin/SORegionsColors";

        static SORegionsColors _instance;

        /// <summary>The cached palette — loaded from Resources on first access.</summary>
        public static SORegionsColors Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<SORegionsColors>(ResourcePath);
                    if (_instance == null)
                        Debug.LogError($"[SORegionsColors] Resources/{ResourcePath}.asset not found.");
                }
                return _instance;
            }
        }

        /// <summary>Colour for a region index; wraps if there are more regions than colours.</summary>
        public static Color ColorAt(int regionIndex)
        {
            var inst = Instance;
            if (inst == null || inst.Colors == null || inst.Colors.Length == 0) return Color.magenta;
            int n = inst.Colors.Length;
            return inst.Colors[((regionIndex % n) + n) % n];
        }

        // Warm the cache at startup so the first lookup doesn't pay the Resources.Load cost.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Preload() { _ = Instance; }
    }
}
