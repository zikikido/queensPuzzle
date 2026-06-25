using System;
using System.Collections.Generic;

namespace QueensPuzzle
{
    /// <summary>
    /// Places a valid Queens solution: one queen per row, one per column, and no two
    /// queens touching — not even diagonally.
    ///
    /// Because there is one queen per row and column uniqueness is enforced, two queens can
    /// only ever touch if they are in consecutive rows. So the adjacency rule reduces to:
    /// for rows r and r-1, the column difference must be at least 2.
    /// </summary>
    public static class SolutionPlacer
    {
        /// <summary>
        /// Returns solutionColumns[row] = column of the queen in that row, or null if no
        /// placement was found (only possible for very small / degenerate N).
        /// </summary>
        public static int[] TryPlace(int n, Random rng)
        {
            int[] cols = new int[n];
            bool[] usedCol = new bool[n];
            return Place(0, n, cols, usedCol, rng) ? cols : null;
        }

        static bool Place(int row, int n, int[] cols, bool[] usedCol, Random rng)
        {
            if (row == n) return true;

            var candidates = new List<int>(n);
            for (int c = 0; c < n; c++)
            {
                if (usedCol[c]) continue;
                if (row > 0 && Math.Abs(c - cols[row - 1]) < 2) continue;
                candidates.Add(c);
            }
            Shuffle(candidates, rng);

            foreach (int c in candidates)
            {
                cols[row] = c;
                usedCol[c] = true;
                if (Place(row + 1, n, cols, usedCol, rng)) return true;
                usedCol[c] = false;
            }
            return false;
        }

        static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
