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
            if (!TrySteerRaw(target, n, seed, warmRegion, warmSol, out int[] region, out int[] sol, out int weight, onProgress))
                return null;

            var data = ScriptableObject.CreateInstance<LevelData>();
            data.size = n;
            data.regions = region;
            data.solutionColumns = sol;
            data.seed = seed;
            data.weight = weight;
            return data;
        }

        /// <summary>
        /// Thread-safe core of <see cref="Generate"/>: steers to a raw region/solution/weight without
        /// touching the Unity object model — safe to run on a worker thread (pass a null
        /// <paramref name="onProgress"/> off the main thread).
        /// </summary>
        public static bool TrySteerRaw(LevelFingerprint target, int n, int seed,
            int[] warmRegion, int[] warmSol, out int[] region, out int[] sol, out int weight,
            Action<float> onProgress = null)
        {
            int[] startRegion, startSol;
            int minDrift = 0;
            if (warmRegion != null && warmSol != null && warmRegion.Length == n * n)
            {
                // a random board symmetry moves every queen and colour while keeping the puzzle
                // structure (and so the fingerprint) exactly — the anneal then mutates it further
                startRegion = (int[])warmRegion.Clone();
                startSol = (int[])warmSol.Clone();
                RandomTransform(n, ref startRegion, ref startSol, seed);
                minDrift = n * n / 4; // and the anneal must still change ≥¼ of the cells on top
                onProgress?.Invoke(0.1f);
            }
            else
            {
                if (!LevelGenerator.TryGenerateRaw(n, seed, 250, out startRegion, out startSol, out _,
                        p => onProgress?.Invoke(p * 0.1f)))
                { region = null; sol = null; weight = 0; return false; }
            }

            region = WeightAnnealer.Steer(n, startRegion, startSol,
                target, Gamma, Iterations, seed,
                out sol, p => onProgress?.Invoke(0.1f + p * 0.9f), minDrift);
            if (region == null || sol == null) { weight = 0; return false; }

            weight = WeightRater.Rate(n, region, sol).weight;
            return true;
        }

        // One of the 7 non-identity board symmetries (flip H / flip V / transpose, combined).
        // Queens' attack rules are symmetric under all of them, so validity, uniqueness and the
        // solve structure are preserved exactly — only the positions move.
        static void RandomTransform(int n, ref int[] region, ref int[] sol, int seed)
        {
            int op = 1 + new System.Random(seed).Next(7);
            bool fh = (op & 1) != 0, fv = (op & 2) != 0, tr = (op & 4) != 0;

            var r2 = new int[n * n];
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    int rr = fv ? n - 1 - r : r, cc = fh ? n - 1 - c : c;
                    if (tr) { int t = rr; rr = cc; cc = t; }
                    r2[rr * n + cc] = region[r * n + c];
                }

            var s2 = new int[n];
            for (int r = 0; r < n; r++)
            {
                int c = sol[r];
                int rr = fv ? n - 1 - r : r, cc = fh ? n - 1 - c : c;
                if (tr) { int t = rr; rr = cc; cc = t; }
                s2[rr] = cc;
            }

            region = r2;
            sol = s2;
        }
    }
}
