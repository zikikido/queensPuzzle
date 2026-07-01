using System;
using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// Editor wrapper around <see cref="DifficultyAnnealer"/>: generates a unique starting board,
    /// steers its region map toward the requested difficulty, then assembles a rated, traced asset.
    ///
    /// Targets the difficulty tier directly: Hard = hardest pure deduction, Expert = a board that
    /// needs a forced guess (survivable trial-and-error under the 3-lives rule).
    /// </summary>
    public static class LevelSteerer
    {
        public const int Iterations = 300;
        public const float Gamma = 0.5f;

        public static LevelData Generate(Difficulty target, int n, int seed, Action<float> onProgress = null)
        {
            var start = LevelGenerator.Generate(n, seed, 250, p => onProgress?.Invoke(p * 0.1f));
            if (start == null) return null;

            int[] region = DifficultyAnnealer.Steer(n, start.regions, start.solutionColumns,
                target, Gamma, Iterations, seed,
                out int[] sol, p => onProgress?.Invoke(0.1f + p * 0.9f));

            var data = ScriptableObject.CreateInstance<LevelData>();
            data.size = n;
            data.regions = region;
            data.solutionColumns = sol;
            data.seed = seed;

            var rating = DifficultyRater.Rate(n, region, sol);
            data.difficulty = rating.difficulty;
            data.estimatedSolveSeconds = rating.estimatedSeconds;
            return data;
        }
    }
}
