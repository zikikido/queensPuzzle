using System;

namespace QueensPuzzle
{
    /// <summary>Which deduction technique forced a step, ordered easiest → hardest.</summary>
    public enum SolveTechnique
    {
        None = 0,
        RegionSingle = 1,
        LineSingle = 2,
        LineToRegion = 3,  // a row/column is a single colour → clear that colour elsewhere
        RegionToLine = 4,  // a region is confined to one row/column → clear that line of others
        Squeeze = 5,
        SubsetLineToRegion = 6,  // k rows/columns hold only k colours → clear those colours elsewhere
        SubsetRegionToLine = 7,  // k regions fill k rows/columns → clear those lines of other regions
        Fish = 8,                // k rows confined to k columns (or vice-versa) → clear those lines elsewhere
        Trial = 9,
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

    /// <summary>How a step marks a cell on the board.</summary>
    public enum CellMark
    {
        X = 0,      // eliminated — this cell can't hold a queen
        Queen = 1,  // a queen is placed here
    }

    /// <summary>One cell a step touches, and what it does to it.</summary>
    [Serializable]
    public struct TraceMark
    {
        public int cell;        // flat index = row * size + col
        public CellMark mark;
    }

    /// <summary>
    /// One node of the full solve trace, stored flattened (Unity can't serialize a recursive tree).
    /// Children point to their parent via <see cref="parent"/>; rebuild the tree by grouping on it.
    /// A linear solve is a chain; each trial fans out into branches under its TrialRoot.
    ///
    /// The cells a step affects live in <see cref="marks"/> (each tagged X or Queen); <see cref="note"/>
    /// carries the *reason* — which regions/lines were involved — not the coordinates.
    /// </summary>
    [Serializable]
    public struct TraceNode
    {
        public int parent;            // index of the parent node, -1 for a top-level step
        public SolveTechnique technique;
        public NodeKind kind;
        public Outcome outcome;
        public TraceMark[] marks;     // cells this step marks (X or queen)
        public string note;           // human-readable reason (regions/lines), no coordinates
    }
}
