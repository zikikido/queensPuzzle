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

        [Tooltip("Rough estimate of human solve time, in seconds (derived, not measured).")]
        public float estimatedSolveSeconds;

        [Tooltip("RNG seed that produced this level — lets us reproduce it exactly.")]
        public int seed;

        /// <summary>Region id at the given cell.</summary>
        public int RegionAt(int row, int col) => regions[row * size + col];

        /// <summary>True if the queen in the solution sits at this cell.</summary>
        public bool IsSolutionQueen(int row, int col) => solutionColumns[row] == col;
    }
}
