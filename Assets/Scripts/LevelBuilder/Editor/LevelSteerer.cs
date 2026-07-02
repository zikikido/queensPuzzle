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

        public static LevelData Generate(int targetWeight, int n, int seed, Action<float> onProgress = null)
        {
            var start = LevelGenerator.Generate(n, seed, 250, p => onProgress?.Invoke(p * 0.1f));
            if (start == null) return null;

            int[] region = WeightAnnealer.Steer(n, start.regions, start.solutionColumns,
                targetWeight, Gamma, Iterations, seed,
                out int[] sol, p => onProgress?.Invoke(0.1f + p * 0.9f));

            var data = ScriptableObject.CreateInstance<LevelData>();
            data.size = n;
            data.regions = region;
            data.solutionColumns = sol;
            data.seed = seed;

            var rating = WeightRater.Rate(n, region, sol);
            data.weight = rating.weight;
            data.estimatedSolveSeconds = rating.estimatedSeconds;
            return data;
        }
    }
}
