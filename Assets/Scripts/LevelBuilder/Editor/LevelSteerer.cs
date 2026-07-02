using System;
using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// Editor wrapper around <see cref="WeightAnnealer"/>: generates a unique starting board,
    /// steers its region map toward the requested weight, then assembles a rated, traced asset.
    /// </summary>
    public static class LevelSteerer
    {
        public const int Iterations = 300;
        public const float Gamma = 0.5f;

        /// <summary>
        /// Generates a level steered to <paramref name="target"/>. Pass <paramref name="warmRegion"/> +
        /// <paramref name="warmSol"/> (a same-size reference board) to anneal FROM it — starts on the
        /// fingerprint and mutates into a different board — instead of climbing from a random start.
        /// </summary>
        public static LevelData Generate(LevelFingerprint target, int n, int seed,
            int[] warmRegion = null, int[] warmSol = null, Action<float> onProgress = null)
        {
            int[] startRegion, startSol;
            int minDrift = 0;
            if (warmRegion != null && warmSol != null && warmRegion.Length == n * n)
            {
                startRegion = warmRegion;
                startSol = warmSol;
                minDrift = n * n / 4; // must end up a genuinely different board, not the reference
                onProgress?.Invoke(0.1f);
            }
            else
            {
                var start = LevelGenerator.Generate(n, seed, 250, p => onProgress?.Invoke(p * 0.1f));
                if (start == null) return null;
                startRegion = start.regions;
                startSol = start.solutionColumns;
            }

            int[] region = WeightAnnealer.Steer(n, startRegion, startSol,
                target, Gamma, Iterations, seed,
                out int[] sol, p => onProgress?.Invoke(0.1f + p * 0.9f), minDrift);

            var data = ScriptableObject.CreateInstance<LevelData>();
            data.size = n;
            data.regions = region;
            data.solutionColumns = sol;
            data.seed = seed;

            var rating = WeightRater.Rate(n, region, sol);
            data.weight = rating.weight;
            return data;
        }
    }
}
