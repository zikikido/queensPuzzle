using System;
using System.Collections.Generic;

namespace QueensPuzzle
{
    /// <summary>
    /// Steers a board toward a target weight by simulated annealing on the region map.
    ///
    /// Each step flips one boundary cell into a touching region — but only "legal" flips that keep
    /// every region connected and non-empty, so the partition stays valid the whole time. The board
    /// is re-rated, and a move is accepted when it lowers a cost, or (rarely, while "hot") even when
    /// it raises it, so the search can climb out of local minima:
    ///
    ///   cost = max(0, |weight - target| - Band) + gamma * neighbourStdDev
    ///
    /// The first term aims at the requested weight (anywhere within ±Band counts as a hit). The
    /// second term — the spread of the board's OWN one-flip neighbours' weights — steers away from
    /// fragile "knife-edge" boards (where one different cell swings the weight wildly) toward stable
    /// ones that play like they're rated. Uniqueness is enforced per move: a flip that creates a
    /// second solution is rejected.
    ///
    /// Pure C# (no Unity types); the editor wrapper <c>LevelSteerer</c> turns the result into an asset.
    /// </summary>
    public static class WeightAnnealer
    {
        public const int Band = 10;   // |weight - target| <= Band counts as on-target

        /// <summary>
        /// Anneals from a valid unique starting board toward the <paramref name="target"/> weight.
        /// Returns the best region map found, with its solution in <paramref name="solution"/>.
        /// </summary>
        public static int[] Steer(int n, int[] startRegion, int[] startSol, int target,
            float gamma, int iterations, int seed, out int[] solution, Action<float> onProgress = null)
        {
            var rng = new Random(seed);
            int[] cur = (int[])startRegion.Clone();
            int[] curSol = (int[])startSol.Clone();
            double curCost = Cost(n, cur, curSol, target, gamma);

            int[] best = (int[])cur.Clone();
            int[] bestSol = (int[])curSol.Clone();
            double bestCost = curCost;

            double t = 4.0; // temperature — cools each step
            for (int it = 0; it < iterations; it++)
            {
                onProgress?.Invoke(it / (float)iterations);
                if (bestCost <= 0.0001) break; // already on target and perfectly stable

                if (!RandomLegalMove(n, cur, rng, out int cell, out int newReg)) continue;
                int[] cand = (int[])cur.Clone();
                cand[cell] = newReg;
                if (!QueensSolver.TrySolve(n, cand, out int[] candSol, out bool unique) || !unique) continue;

                double candCost = Cost(n, cand, candSol, target, gamma);
                double delta = candCost - curCost;
                if (delta < 0 || rng.NextDouble() < Math.Exp(-delta / t))
                {
                    cur = cand; curSol = candSol; curCost = candCost;
                    if (curCost < bestCost) { bestCost = curCost; best = (int[])cur.Clone(); bestSol = (int[])curSol.Clone(); }
                }
                t *= 0.99;
                if (t < 0.05) t = 0.05;
            }
            onProgress?.Invoke(1f);
            solution = bestSol;
            return best;
        }

        static double Cost(int n, int[] region, int[] sol, int target, float gamma)
        {
            int gap = Math.Max(0, Math.Abs(WeightRater.Rate(n, region, sol).weight - target) - Band);
            double stdDev = Math.Sqrt(NeighbourVariance(n, region));
            return gap + gamma * stdDev;
        }

        // Population variance of the weights of every legal one-cell-flip neighbour that stays unique.
        static double NeighbourVariance(int n, int[] region)
        {
            var scores = new List<int>();
            var done = new HashSet<long>();
            int[] dr = { -1, 1, 0, 0 }, dc = { 0, 0, -1, 1 };
            for (int i = 0; i < n * n; i++)
            {
                int r = i / n, c = i % n, a = region[i];
                for (int k = 0; k < 4; k++)
                {
                    int nr = r + dr[k], nc = c + dc[k];
                    if (nr < 0 || nr >= n || nc < 0 || nc >= n) continue;
                    int b = region[nr * n + nc];
                    if (b == a) continue;
                    long key = (long)i * 100 + b;
                    if (!done.Add(key)) continue;
                    if (!StillConnected(n, region, a, i)) continue;
                    int[] cand = (int[])region.Clone();
                    cand[i] = b;
                    if (!QueensSolver.TrySolve(n, cand, out int[] sol, out bool unique) || !unique) continue;
                    scores.Add(WeightRater.Rate(n, cand, sol).weight);
                }
            }
            if (scores.Count == 0) return 0;
            double m = 0; foreach (int s in scores) m += s; m /= scores.Count;
            double v = 0; foreach (int s in scores) v += (s - m) * (s - m);
            return v / scores.Count;
        }

        // Picks a random boundary cell whose region stays connected after the flip.
        static bool RandomLegalMove(int n, int[] region, Random rng, out int cell, out int newReg)
        {
            int[] dr = { -1, 1, 0, 0 }, dc = { 0, 0, -1, 1 };
            for (int tries = 0; tries < 200; tries++)
            {
                int i = rng.Next(n * n);
                int r = i / n, c = i % n, a = region[i];
                var opts = new List<int>();
                for (int k = 0; k < 4; k++)
                {
                    int nr = r + dr[k], nc = c + dc[k];
                    if (nr < 0 || nr >= n || nc < 0 || nc >= n) continue;
                    int b = region[nr * n + nc];
                    if (b != a) opts.Add(b);
                }
                if (opts.Count == 0) continue;
                if (!StillConnected(n, region, a, i)) continue;
                cell = i; newReg = opts[rng.Next(opts.Count)];
                return true;
            }
            cell = -1; newReg = -1; return false;
        }

        // Would region g stay connected (and non-empty) if cell 'drop' left it?
        static bool StillConnected(int n, int[] region, int g, int drop)
        {
            int start = -1, size = 0;
            for (int i = 0; i < n * n; i++) if (region[i] == g && i != drop) { size++; if (start < 0) start = i; }
            if (size == 0) return false;

            var seen = new bool[n * n];
            var stack = new Stack<int>();
            stack.Push(start); seen[start] = true; int reached = 1;
            int[] dr = { -1, 1, 0, 0 }, dc = { 0, 0, -1, 1 };
            while (stack.Count > 0)
            {
                int cur = stack.Pop(); int r = cur / n, c = cur % n;
                for (int k = 0; k < 4; k++)
                {
                    int nr = r + dr[k], nc = c + dc[k];
                    if (nr < 0 || nr >= n || nc < 0 || nc >= n) continue;
                    int j = nr * n + nc;
                    if (j == drop || seen[j] || region[j] != g) continue;
                    seen[j] = true; reached++; stack.Push(j);
                }
            }
            return reached == size;
        }
    }
}
