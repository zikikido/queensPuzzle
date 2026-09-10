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
        public int[] cells;      // the cells the rule covers (the new queen's own cell excluded):
                                 // Touch → the ring around each touched puppy, RowCol → the row
                                 // and/or column a culprit shares, Color → its whole region

        public static readonly RuleBreak None = new RuleBreak { rule = Rule.None, culprits = new int[0], cells = new int[0] };

        /// <param name="n">board size</param>
        /// <param name="regions">size^2 region ids</param>
        /// <param name="queens">board indices of the queens already placed (right or wrong)</param>
        /// <param name="cell">board index of the queen just placed</param>
        public static RuleBreak Find(int n, int[] regions, IReadOnlyList<int> queens, int cell)
        {
            int row = cell / n, col = cell % n;

            var hits = new List<int>();
            var cells = new List<int>();

            // touch — any of the 8 neighbours
            foreach (int q in queens)
            {
                if (q == cell) continue;
                int dr = q / n - row, dc = q % n - col;
                if (dr >= -1 && dr <= 1 && dc >= -1 && dc <= 1) hits.Add(q);
            }
            if (hits.Count > 0)
            {
                // the ring around each touched puppy — its "no touch" zone the new queen stepped into
                foreach (int q in hits)
                {
                    int qr = q / n, qc = q % n;
                    for (int r = qr - 1; r <= qr + 1; r++)
                        for (int c = qc - 1; c <= qc + 1; c++)
                        {
                            if (r < 0 || r >= n || c < 0 || c >= n) continue;
                            int i = r * n + c;
                            if (i != q && i != cell && !cells.Contains(i)) cells.Add(i);
                        }
                }
                return new RuleBreak { rule = Rule.Touch, culprits = hits.ToArray(), cells = cells.ToArray() };
            }

            // row / column — only the line(s) a culprit actually sits on
            bool sameRow = false, sameCol = false;
            foreach (int q in queens)
            {
                if (q == cell) continue;
                bool onRow = q / n == row, onCol = q % n == col;
                if (onRow || onCol) hits.Add(q);
                sameRow |= onRow;
                sameCol |= onCol;
            }
            if (hits.Count > 0)
            {
                if (sameRow) for (int c = 0; c < n; c++) if (c != col) cells.Add(row * n + c);
                if (sameCol) for (int r = 0; r < n; r++) if (r != row) cells.Add(r * n + col);
                return new RuleBreak { rule = Rule.RowCol, culprits = hits.ToArray(), cells = cells.ToArray() };
            }

            // same region
            int region = regions[cell];
            foreach (int q in queens)
            {
                if (q == cell) continue;
                if (regions[q] == region) hits.Add(q);
            }
            if (hits.Count > 0)
            {
                for (int i = 0; i < regions.Length; i++)
                    if (regions[i] == region && i != cell) cells.Add(i);
                return new RuleBreak { rule = Rule.Color, culprits = hits.ToArray(), cells = cells.ToArray() };
            }

            return None;
        }
    }
}
