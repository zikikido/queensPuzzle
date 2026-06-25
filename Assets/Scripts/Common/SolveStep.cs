using System;

namespace QueensPuzzle
{
    /// <summary>Which technique forced a step in the solve path (ordered easiest → hardest).</summary>
    public enum SolveTechnique
    {
        None = 0,
        RegionSingle = 1,
        LineSingle = 2,
        RegionLine = 3,
        Squeeze = 4,
        Subset = 5,
        Trial = 6,
    }

    /// <summary>
    /// One step of the recorded solve path: a queen placed at (row, col) and the hardest technique
    /// that unlocked it. Stored on the level so the game can replay the solution or give hints.
    /// </summary>
    [Serializable]
    public struct SolveStep
    {
        public int row;
        public int col;
        public SolveTechnique technique;
    }
}
