using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// Orchestrates level creation: place a valid solution, grow regions around it, then
    /// keep retrying (re-growing, and re-placing as needed) until the board has exactly one
    /// solution. Returns a ready-to-save <see cref="LevelData"/>.
    ///
    /// NOTE: difficulty rating is not implemented yet — the result is tagged
    /// <see cref="Difficulty.Unrated"/>. Targeting a requested difficulty comes in a later
    /// step once the logical (technique-tracking) solver exists.
    /// </summary>
    public static class LevelGenerator
    {
        public const int MinSize = 5;
        public const int MaxSize = 11;

        /// <summary>
        /// Generates a unique puzzle of the given size. Returns null only if generation
        /// failed within <paramref name="maxAttempts"/> (very unlikely for sizes in range).
        /// </summary>
        public static LevelData Generate(int n, int seed, int maxAttempts = 250,
            System.Action<float> onProgress = null)
        {
            n = Mathf.Clamp(n, MinSize, MaxSize);
            var rng = new System.Random(seed);

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                onProgress?.Invoke(attempt / (float)maxAttempts);
                int[] cols = SolutionPlacer.TryPlace(n, rng);
                if (cols == null) continue;

                int[] region = RegionGrower.Grow(n, cols, rng);
                if (!MakeUnique(n, region, cols, rng)) continue; // couldn't repair → re-roll

                var data = ScriptableObject.CreateInstance<LevelData>();
                data.size = n;
                data.regions = region;
                data.solutionColumns = cols;
                data.seed = seed;
                data.difficulty = Difficulty.Unrated;          // TODO: rate via logical solver
                data.estimatedSolveSeconds = EstimateSolveSeconds(n);
                return data;
            }
            return null;
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

        /// <summary>
        /// Placeholder solve-time estimate based only on board size. Will be replaced by an
        /// estimate derived from the measured difficulty once rating exists.
        /// </summary>
        static float EstimateSolveSeconds(int n) => n * n * 0.6f;
    }
}
