namespace QueensPuzzle
{
    /// <summary>
    /// Brute-force search over Queens placements, used by the generator to test uniqueness:
    /// it looks for a second valid solution that differs from the intended one.
    ///
    /// A placement is valid when every row, column and region holds exactly one queen and no
    /// two queens touch. Searching row by row with column- and region-usage tracking enforces
    /// all of that; the only remaining adjacency case is consecutive rows (column diff &gt;= 2).
    /// </summary>
    public static class QueensSolver
    {
        /// <summary>
        /// Searches for any valid solution that differs from <paramref name="target"/>.
        /// Returns true (with that solution in <paramref name="alt"/>) when one exists, i.e.
        /// the board is NOT unique; returns false when the only solution is the target.
        /// </summary>
        /// <summary>
        /// Recovers the solution from the regions alone (used by import, where we have the
        /// colours but not the queens). Searches row by row and stops once a second solution
        /// is found. Returns true with the first solution in <paramref name="solution"/>;
        /// <paramref name="unique"/> is true only when it is the single solution. Returns
        /// false when the layout has no solution at all.
        /// </summary>
        public static bool TrySolve(int n, int[] region, out int[] solution, out bool unique)
        {
            bool[] usedCol = new bool[n];
            bool[] usedRegion = new bool[n];
            int[] colOfRow = new int[n];
            int[] first = null;
            int count = 0;

            void Search(int row)
            {
                if (count >= 2) return; // two is enough to prove non-uniqueness
                if (row == n)
                {
                    count++;
                    if (first == null) first = (int[])colOfRow.Clone();
                    return;
                }

                for (int c = 0; c < n; c++)
                {
                    if (usedCol[c]) continue;
                    if (row > 0 && System.Math.Abs(c - colOfRow[row - 1]) < 2) continue;

                    int reg = region[row * n + c];
                    if (usedRegion[reg]) continue;

                    usedCol[c] = true;
                    usedRegion[reg] = true;
                    colOfRow[row] = c;

                    Search(row + 1);

                    usedCol[c] = false;
                    usedRegion[reg] = false;
                    if (count >= 2) return;
                }
            }

            Search(0);
            solution = first;
            unique = count == 1;
            return first != null;
        }

        public static bool TryFindAlternative(int n, int[] region, int[] target, out int[] alt)
        {
            bool[] usedCol = new bool[n];
            bool[] usedRegion = new bool[n];
            int[] colOfRow = new int[n];
            int[] found = null;

            bool Search(int row)
            {
                if (found != null) return true;
                if (row == n)
                {
                    for (int r = 0; r < n; r++)
                        if (colOfRow[r] != target[r]) { found = (int[])colOfRow.Clone(); return true; }
                    return false; // identical to target — keep searching for a different one
                }

                for (int c = 0; c < n; c++)
                {
                    if (usedCol[c]) continue;
                    if (row > 0 && System.Math.Abs(c - colOfRow[row - 1]) < 2) continue;

                    int reg = region[row * n + c];
                    if (usedRegion[reg]) continue;

                    usedCol[c] = true;
                    usedRegion[reg] = true;
                    colOfRow[row] = c;

                    if (Search(row + 1)) return true;

                    usedCol[c] = false;
                    usedRegion[reg] = false;
                }
                return false;
            }

            Search(0);
            alt = found;
            return found != null;
        }
    }
}
