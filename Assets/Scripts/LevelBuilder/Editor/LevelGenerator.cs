using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// Orchestrates level creation: place a valid solution, grow regions around it, then
    /// keep retrying (re-growing, and re-placing as needed) until the board has exactly one
    /// solution. Returns a ready-to-save <see cref="LevelData"/>.
    ///
    /// Every generated level is auto-rated: <see cref="LevelData.weight"/> gets the measured
    /// solve weight. Steering toward a requested weight is <c>LevelSteerer</c>'s job.
    /// </summary>
    public static class LevelGenerator
    {
        public const int MinSize = 4;
        public const int MaxSize = 11;

        /// <summary>
        /// Generates a unique puzzle of the given size. Returns null only if generation
        /// failed within <paramref name="maxAttempts"/> (very unlikely for sizes in range).
        /// </summary>
        public static LevelData Generate(int n, int seed, int maxAttempts = 250,
            System.Action<float> onProgress = null)
        {
            if (!TryGenerateRaw(n, seed, maxAttempts, out int[] region, out int[] cols, out int weight, onProgress))
                return null;

            var data = ScriptableObject.CreateInstance<LevelData>();
            data.size = Mathf.Clamp(n, MinSize, MaxSize);
            data.regions = region;
            data.solutionColumns = cols;
            data.seed = seed;
            data.weight = weight;
            return data;
        }

        /// <summary>
        /// Thread-safe core of <see cref="Generate"/>: produces the raw region/solution/weight of a
        /// unique board without touching the Unity object model — safe to run on a worker thread
        /// (pass a null <paramref name="onProgress"/> off the main thread).
        /// </summary>
        public static bool TryGenerateRaw(int n, int seed, int maxAttempts,
            out int[] region, out int[] cols, out int weight, System.Action<float> onProgress = null)
        {
            n = Mathf.Clamp(n, MinSize, MaxSize);
            var rng = new System.Random(seed);

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                onProgress?.Invoke(attempt / (float)maxAttempts);
                int[] c = SolutionPlacer.TryPlace(n, rng);
                if (c == null) continue;

                int[] r = RegionGrower.Grow(n, c, rng);
                if (!MakeUnique(n, r, c, rng)) continue; // couldn't repair → re-roll

                region = r; cols = c;
                weight = WeightRater.Rate(n, r, c).weight; // auto-rate on generate
                return true;
            }
            region = null; cols = null; weight = 0;
            return false;
        }

        /// <summary>
        /// Repairs <paramref name="region"/> in place until the board has a single solution, by
        /// repeatedly breaking whatever alternative solution the solver finds. Returns false if it
        /// hits an unbreakable alternative or the repair budget before reaching uniqueness.
        /// </summary>
        static bool MakeUnique(int n, int[] region, int[] cols, System.Random rng)
        {
            int budget = n * n * 4;
            for (int i = 0; i < budget; i++)
            {
                if (!QueensSolver.TryFindAlternative(n, region, cols, out int[] alt))
                    return true; // unique
                if (!RegionGrower.TryBreakAlternative(n, region, cols, alt, rng))
                    return false; // stuck — let the caller re-grow
            }
            return false;
        }
    }
}
