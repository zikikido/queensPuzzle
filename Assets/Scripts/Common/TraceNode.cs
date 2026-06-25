using System;

namespace QueensPuzzle
{
    /// <summary>Which deduction technique forced a step, ordered easiest → hardest.</summary>
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

    /// <summary>What a trace node represents.</summary>
    public enum NodeKind
    {
        Placement = 0,    // a queen was placed
        Elimination = 1,  // a deduction removed candidate cells
        TrialRoot = 2,    // a guess point — its children are the options
        TrialBranch = 3,  // one option of a guess ("what if a queen goes here")
    }

    /// <summary>How a line of reasoning ended.</summary>
    public enum Outcome
    {
        Continues = 0,    // ongoing / part of the solution path
        DeadEnd = 1,      // led to a contradiction (the guess was impossible)
        Solved = 2,       // completed the board
        Unresolved = 3,   // hit the trace depth limit before resolving
    }

    /// <summary>
    /// One node of the full solve trace, stored flattened (Unity can't serialize a recursive tree).
    /// Children point to their parent via <see cref="parent"/>; rebuild the tree by grouping on it.
    /// A linear solve is a chain; each trial fans out into branches under its TrialRoot.
    /// </summary>
    [Serializable]
    public struct TraceNode
    {
        public int parent;            // index of the parent node, -1 for a top-level step
        public int row, col;          // the cell involved (-1 when not applicable)
        public SolveTechnique technique;
        public NodeKind kind;
        public Outcome outcome;
        public string note;           // human-readable reason
    }
}
