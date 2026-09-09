using System.Collections.Generic;

namespace QueensPuzzle
{
    /// <summary>The three board rules a placed queen can break, in the order we report them.</summary>
    public enum Rule
    {
        None,
        Touch,    // a queen on one of the 8 neighbouring cells
        RowCol,   // another queen in the same row or column
        Color     // another queen in the same region
    }

    /// <summary>
    /// Which rule a new queen breaks against the queens already on the board, and which of those
    /// queens it clashes with. Pure board math — the first rule found wins (Touch, RowCol, Color).
    /// Cells are board indices (row*n + col).
    /// </summary>
    public struct RuleBreak
    {
        public Rule rule;
        public int[] culprits;   // the existing queens that break the rule with the new one

        public static readonly RuleBreak None = new RuleBreak { rule = Rule.None, culprits = new int[0] };

        /// <param name="n">board size</param>
        /// <param name="regions">size^2 region ids</param>
        /// <param name="queens">board indices of the queens already placed (right or wrong)</param>
        /// <param name="cell">board index of the queen just placed</param>
        public static RuleBreak Find(int n, int[] regions, IReadOnlyList<int> queens, int cell)
        {
            int row = cell / n, col = cell % n;

            var hits = new List<int>();

            // touch — any of the 8 neighbours
            foreach (int q in queens)
            {
                if (q == cell) continue;
                int dr = q / n - row, dc = q % n - col;
                if (dr >= -1 && dr <= 1 && dc >= -1 && dc <= 1) hits.Add(q);
            }
            if (hits.Count > 0) return new RuleBreak { rule = Rule.Touch, culprits = hits.ToArray() };

            // row / column
            foreach (int q in queens)
            {
                if (q == cell) continue;
                if (q / n == row || q % n == col) hits.Add(q);
            }
            if (hits.Count > 0) return new RuleBreak { rule = Rule.RowCol, culprits = hits.ToArray() };

            // same region
            int region = regions[cell];
            foreach (int q in queens)
            {
                if (q == cell) continue;
                if (regions[q] == region) hits.Add(q);
            }
            if (hits.Count > 0) return new RuleBreak { rule = Rule.Color, culprits = hits.ToArray() };

            return None;
        }
    }
}
