namespace QueensPuzzle
{
    /// <summary>
    /// Brute-force constraint solver for a Queens board. Used here to verify uniqueness:
    /// a good puzzle must have exactly one valid solution. Counts solutions up to a limit
    /// (we only ever need to know whether the count is 1 or more than 1).
    ///
    /// A placement is valid when every row, column and region holds exactly one queen and no
    /// two queens touch. Searching row by row with column- and region-usage tracking enforces
    /// all of that; the only remaining adjacency case is consecutive rows (column diff &gt;= 2).
    /// </summary>
    public static class QueensSolver
    {
        /// <summary>Counts valid solutions for the given region map, stopping at <paramref name="limit"/>.</summary>
        public static int CountSolutions(int n, int[] region, int limit = 2)
        {
            bool[] usedCol = new bool[n];
            bool[] usedRegion = new bool[n];
            int[] colOfRow = new int[n];
            int count = 0;

            void Search(int row)
            {
                if (count >= limit) return;
                if (row == n) { count++; return; }

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
                    if (count >= limit) return;
                }
            }

            Search(0);
            return count;
        }

        /// <summary>Convenience: true when the board has exactly one solution.</summary>
        public static bool HasUniqueSolution(int n, int[] region) => CountSolutions(n, region, 2) == 1;

        /// <summary>
        /// Searches for any valid solution that differs from <paramref name="target"/>.
        /// Returns true (with that solution in <paramref name="alt"/>) when one exists, i.e.
        /// the board is NOT unique; returns false when the only solution is the target.
        /// </summary>
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
