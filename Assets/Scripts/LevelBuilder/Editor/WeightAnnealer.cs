using System;
using System.Collections.Generic;

namespace QueensPuzzle
{
    /// <summary>
    /// What Generate aims for. weight is required; peak / evenness / steps are optional
    /// (&lt;= 0 = don't care). Each tolerance is a % of its target value — a level counts
    /// as on-target when every active term is within its tolerance.
    /// </summary>
    public struct LevelFingerprint
    {
        public int weight, peak, steps;   // steps = paid steps (queen shadows excluded)
        public float evenness;            // 0..1
        public int tolWeightPct, tolPeakPct, tolEvennessPct, tolStepsPct;
    }

    /// <summary>
    /// Steers a board toward a target fingerprint by simulated annealing on the region map.
    ///
    /// Each step flips one boundary cell into a touching region — but only "legal" flips that keep
    /// every region connected and non-empty, so the partition stays valid the whole time. The board
    /// is re-rated, and a move is accepted when it lowers a cost, or (rarely, while "hot") even when
    /// it raises it, so the search can climb out of local minima:
    ///
    ///   cost = Σ %-beyond-tolerance per fingerprint term  +  gamma * neighbourStdDev%
    ///
    /// The first term aims at the requested fingerprint (weight / peak / evenness / steps, each
    /// within its % tolerance). The second term — the spread of the board's OWN one-flip
    /// neighbours' weights — steers away from fragile "knife-edge" boards (where one different
    /// cell swings the weight wildly) toward stable ones that play like they're rated.
    /// Uniqueness is enforced per move: a flip that creates a second solution is rejected.
    ///
    /// Pure C# (no Unity types); the editor wrapper <c>LevelSteerer</c> turns the result into an asset.
    /// </summary>
    public static class WeightAnnealer
    {
        /// <summary>
        /// Anneals from a valid unique starting board toward the <paramref name="target"/> fingerprint.
        /// Returns the best region map found, with its solution in <paramref name="solution"/>.
        /// With <paramref name="minDriftFromStart"/> &gt; 0 (warm start from a reference board), only
        /// boards that differ from the start in at least that many region cells AND move at least
        /// <paramref name="minQueenDrift"/> queens can win — so a warm start returns a genuinely
        /// different board with a genuinely different solution, not the reference in disguise.
        /// </summary>
        public static int[] Steer(int n, int[] startRegion, int[] startSol, LevelFingerprint target,
            float gamma, int iterations, int seed, out int[] solution, Action<float> onProgress = null,
            int minDriftFromStart = 0, int minQueenDrift = 0)
        {
            var rng = new Random(seed);
            int[] origin = (int[])startRegion.Clone();
            int[] originSol = (int[])startSol.Clone();
            int[] cur = (int[])startRegion.Clone();
            int[] curSol = (int[])startSol.Clone();
            double curCost = Cost(n, cur, curSol, target, gamma);

            int[] best = (int[])cur.Clone();
            int[] bestSol = (int[])curSol.Clone();
            double bestCost = minDriftFromStart > 0 || minQueenDrift > 0 ? double.MaxValue : curCost;

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
                    if (curCost < bestCost && Diff(cur, origin) >= minDriftFromStart && Diff(curSol, originSol) >= minQueenDrift)
                    { bestCost = curCost; best = (int[])cur.Clone(); bestSol = (int[])curSol.Clone(); }
                }
                t *= 0.99;
                if (t < 0.05) t = 0.05;
            }
            onProgress?.Invoke(1f);
            // drift never reached (warm start walked in circles) — return where the walk ended
            if (bestCost == double.MaxValue) { best = cur; bestSol = curSol; }
            solution = bestSol;
            return best;
        }

        static int Diff(int[] a, int[] b)
        {
            int d = 0;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) d++;
            return d;
        }

        static double Cost(int n, int[] region, int[] sol, LevelFingerprint target, float gamma)
        {
            var rep = WeightRater.Rate(n, region, sol);
            double cost = FingerprintGap(rep, target);
            // stability, in the same units (% of the target weight)
            double stdDevPct = Math.Sqrt(NeighbourVariance(n, region)) * 100.0 / Math.Max(1, target.weight);
            return cost + gamma * stdDevPct;
        }

        /// <summary>Sum of the %-beyond-tolerance gaps of all active fingerprint terms. 0 = on target.</summary>
        public static double FingerprintGap(WeightRater.Report rep, LevelFingerprint t)
            => Gap(rep.weight, t.weight, t.tolWeightPct)
             + Gap(rep.peak, t.peak, t.tolPeakPct)
             + Gap(rep.evenness, t.evenness, t.tolEvennessPct)
             + Gap(rep.paidSteps, t.steps, t.tolStepsPct);

        public static bool OnTarget(WeightRater.Report rep, LevelFingerprint t) => FingerprintGap(rep, t) <= 0;

        // % deviation beyond the tolerance; 0 inside the tolerance or when the term is "don't care".
        static double Gap(double value, double target, int tolPct)
        {
            if (target <= 0) return 0;
            double pct = Math.Abs(value - target) * 100.0 / target;
            return Math.Max(0, pct - tolPct);
        }

        // Population variance of the weights of every legal one-cell-flip neighbour that stays unique.
        static double NeighbourVariance(int n, int[] region)
        {
            var weights = new List<int>();
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
                    weights.Add(WeightRater.Rate(n, cand, sol).weight);
                }
            }
            if (weights.Count == 0) return 0;
            double m = 0; foreach (int s in weights) m += s; m /= weights.Count;
            double v = 0; foreach (int s in weights) v += (s - m) * (s - m);
            return v / weights.Count;
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
