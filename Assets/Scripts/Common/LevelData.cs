using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// A single generated puzzle, saved as an editor asset (.asset).
    /// The board is always square: <see cref="size"/> = number of regions = number of queens.
    /// Arrays are stored flat because Unity cannot serialize multi-dimensional arrays.
    /// </summary>
    [CreateAssetMenu(fileName = "Level", menuName = "QueensPuzzle/Level", order = 0)]
    public class LevelData : ScriptableObject
    {
        [Tooltip("N — the board is N x N, with N regions and N queens.")]
        public int size;

        [Tooltip("Region id [0..N-1] for each cell, row-major: index = row * size + col.")]
        public int[] regions;

        [Tooltip("Solution: solutionColumns[row] = column of the queen in that row.")]
        public int[] solutionColumns;

        [Tooltip("Measured solve weight — total find+think effort; higher = harder. 0 until rated.")]
        public int weight;

        [Tooltip("RNG seed that produced this level — lets us reproduce it exactly.")]
        public int seed;

        [Tooltip("Rows whose solution queen starts revealed on the board (early-level help). Empty = none. The column derives from solutionColumns, so a revealed queen can never contradict the solution.")]
        public int[] revealedRows;

        /// <summary>Region id at the given cell.</summary>
        public int RegionAt(int row, int col) => regions[row * size + col];

        /// <summary>True if the queen in the solution sits at this cell.</summary>
        public bool IsSolutionQueen(int row, int col) => solutionColumns[row] == col;

        /// <summary>True if this row's solution queen starts revealed on the board.</summary>
        public bool IsRevealedRow(int row) => revealedRows != null && System.Array.IndexOf(revealedRows, row) >= 0;

        /// <summary>Stable hash of the PLAYABLE content (size + regions + solution). A saved board
        /// may only be restored onto the exact puzzle it was played on — if a level is redesigned
        /// (even at the same size), the hash changes and the stale save is rejected.</summary>
        public int ContentHash()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + size;
                if (regions != null) foreach (int r in regions) h = h * 31 + r;
                if (solutionColumns != null) foreach (int c in solutionColumns) h = h * 31 + c;
                return h;
            }
        }
    }
}
