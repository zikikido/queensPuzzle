using System;
using System.Collections.Generic;
using System.Text;

namespace QueensPuzzle
{
    /// <summary>
    /// Builds the full, human-readable solve trace for a level (every deduction and placement,
    /// not just queens) and, at each trial, the whole tree of options the player could pick —
    /// bounded to <see cref="MaxDepth"/> levels of nested guessing. Returned flattened (each node
    /// points to its parent) so it serializes onto the asset and rebuilds into a tree.
    ///
    /// Separate from <see cref="DifficultyRater"/> (which only needs the score): this one trades
    /// speed for a readable, branch-by-branch record. Pure C#; runs at build time.
    /// </summary>
    public static class SolveTracer
    {
        public const int MaxDepth = 4;

        public static TraceNode[] Build(int n, int[] region, int[] solution)
        {
            var board = new Board(n, region, solution, new List<TraceNode>());
            board.Run(-1, 0, guided: true);
            return board.Nodes.ToArray();
        }

        class Board
        {
            readonly int n;
            readonly int[] region, sol;
            bool[] cand, queen, rowDone, colDone, regDone;
            int placed;
            public readonly List<TraceNode> Nodes;
            readonly List<int> _elim = new List<int>();

            public Board(int n, int[] region, int[] sol, List<TraceNode> nodes)
            {
                this.n = n; this.region = region; this.sol = sol; Nodes = nodes;
                cand = new bool[n * n];
                for (int i = 0; i < cand.Length; i++) cand[i] = true;
                queen = new bool[n * n]; rowDone = new bool[n]; colDone = new bool[n]; regDone = new bool[n];
            }

            Board Clone()
            {
                var b = new Board(n, region, sol, Nodes);
                b.cand = (bool[])cand.Clone(); b.queen = (bool[])queen.Clone();
                b.rowDone = (bool[])rowDone.Clone(); b.colDone = (bool[])colDone.Clone(); b.regDone = (bool[])regDone.Clone();
                b.placed = placed;
                return b;
            }

            int Add(int parent, NodeKind kind, int row, int col, SolveTechnique tech, Outcome outcome, string note)
            {
                Nodes.Add(new TraceNode { parent = parent, row = row, col = col, technique = tech, kind = kind, outcome = outcome, note = note });
                return Nodes.Count - 1;
            }

            void SetOutcome(int idx, Outcome o) { var nd = Nodes[idx]; nd.outcome = o; Nodes[idx] = nd; }

            // ---- the recursive trace ----

            public Outcome Run(int parent, int depth, bool guided)
            {
                int cur = parent;
                while (true)
                {
                    if (HasEmptyUnit()) return Outcome.DeadEnd;
                    if (placed == n) return Outcome.Solved;

                    int idx = RegionSingle();
                    if (idx >= 0)
                    {
                        int r = idx / n, c = idx % n;
                        cur = Add(cur, NodeKind.Placement, r, c, SolveTechnique.RegionSingle, Outcome.Continues,
                            $"region {Lr(region[idx])} has only ({r},{c}) left → place queen");
                        Place(r, c); continue;
                    }
                    idx = LineSingle(out bool isRow, out int line);
                    if (idx >= 0)
                    {
                        int r = idx / n, c = idx % n;
                        cur = Add(cur, NodeKind.Placement, r, c, SolveTechnique.LineSingle, Outcome.Continues,
                            $"{(isRow ? "row" : "column")} {line} has only ({r},{c}) left → place queen");
                        Place(r, c); continue;
                    }
                    if (TryRegionLine(out string note))
                    { cur = Add(cur, NodeKind.Elimination, -1, -1, SolveTechnique.RegionLine, Outcome.Continues, note); continue; }
                    if (TrySqueeze(out note))
                    { cur = Add(cur, NodeKind.Elimination, -1, -1, SolveTechnique.Squeeze, Outcome.Continues, note); continue; }
                    if (TrySubset(out note))
                    { cur = Add(cur, NodeKind.Elimination, -1, -1, SolveTechnique.Subset, Outcome.Continues, note); continue; }

                    // stuck → trial
                    if (depth >= MaxDepth)
                    {
                        Add(cur, NodeKind.TrialRoot, -1, -1, SolveTechnique.Trial, Outcome.Unresolved,
                            $"would need a deeper guess (depth limit {MaxDepth})");
                        return Outcome.Unresolved;
                    }
                    int row = MostConstrainedRow();
                    var cols = RowCands(row);
                    int trial = Add(cur, NodeKind.TrialRoot, row, -1, SolveTechnique.Trial, Outcome.Continues,
                        $"stuck — guess in row {row}: {cols.Count} options");

                    int rightBranch = -1;
                    foreach (int c in cols)
                    {
                        int branch = Add(trial, NodeKind.TrialBranch, row, c, SolveTechnique.Trial, Outcome.Continues,
                            $"what if a queen at ({row},{c})?");
                        if (guided && c == sol[row]) { rightBranch = branch; continue; } // the real path continues here
                        var clone = Clone();
                        clone.Place(row, c);
                        Outcome outc = clone.Run(branch, depth + 1, guided: false);
                        SetOutcome(branch, outc == Outcome.Solved ? Outcome.Continues : outc);
                    }

                    if (!guided) return Outcome.DeadEnd; // an impossible line — every option exhausted
                    Place(row, sol[row]);
                    cur = rightBranch;
                }
            }

            // ---- placement & rules ----

            void Place(int r, int c)
            {
                int i = r * n + c;
                if (queen[i]) return;
                queen[i] = true; cand[i] = true; placed++;
                int g = region[i];
                rowDone[r] = true; colDone[c] = true; regDone[g] = true;
                for (int cc = 0; cc < n; cc++) Kill(r * n + cc, i);
                for (int rr = 0; rr < n; rr++) Kill(rr * n + c, i);
                for (int k = 0; k < cand.Length; k++) if (region[k] == g) Kill(k, i);
                for (int dr = -1; dr <= 1; dr++)
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        int nr = r + dr, nc = c + dc;
                        if (nr >= 0 && nr < n && nc >= 0 && nc < n) Kill(nr * n + nc, i);
                    }
            }

            void Kill(int idx, int keep) { if (idx != keep && cand[idx] && !queen[idx]) cand[idx] = false; }
            void Elim(int idx) { if (cand[idx] && !queen[idx]) { cand[idx] = false; _elim.Add(idx); } }

            bool HasEmptyUnit()
            {
                for (int r = 0; r < n; r++) if (!rowDone[r] && RowCount(r) == 0) return true;
                for (int c = 0; c < n; c++) if (!colDone[c] && ColCount(c) == 0) return true;
                for (int g = 0; g < n; g++) if (!regDone[g] && RegCount(g) == 0) return true;
                return false;
            }

            int RegionSingle()
            {
                for (int g = 0; g < n; g++) if (!regDone[g] && RegCount(g) == 1) for (int i = 0; i < cand.Length; i++) if (region[i] == g && C(i)) return i;
                return -1;
            }

            int LineSingle(out bool isRow, out int line)
            {
                for (int r = 0; r < n; r++) if (!rowDone[r] && RowCount(r) == 1) { isRow = true; line = r; for (int c = 0; c < n; c++) if (C(r * n + c)) return r * n + c; }
                for (int c = 0; c < n; c++) if (!colDone[c] && ColCount(c) == 1) { isRow = false; line = c; for (int r = 0; r < n; r++) if (C(r * n + c)) return r * n + c; }
                isRow = false; line = -1; return -1;
            }

            bool TryRegionLine(out string note)
            {
                for (int g = 0; g < n; g++)
                {
                    if (regDone[g]) continue;
                    var cells = RegCells(g);
                    if (cells.Count == 0) continue;
                    if (AllSameRow(cells, out int rr))
                    {
                        _elim.Clear();
                        for (int c = 0; c < n; c++) { int i = rr * n + c; if (region[i] != g) Elim(i); }
                        if (_elim.Count > 0) { note = $"region {Lr(g)} is confined to row {rr}, so row {rr} is its queen → remove {Cells(_elim)}"; return true; }
                    }
                    if (AllSameCol(cells, out int cc))
                    {
                        _elim.Clear();
                        for (int r = 0; r < n; r++) { int i = r * n + cc; if (region[i] != g) Elim(i); }
                        if (_elim.Count > 0) { note = $"region {Lr(g)} is confined to column {cc}, so column {cc} is its queen → remove {Cells(_elim)}"; return true; }
                    }
                }
                for (int r = 0; r < n; r++)
                {
                    if (rowDone[r]) continue;
                    var cells = RowCells(r);
                    if (cells.Count > 0 && AllSameRegion(cells, out int g))
                    {
                        _elim.Clear();
                        for (int i = 0; i < cand.Length; i++) if (region[i] == g && i / n != r) Elim(i);
                        if (_elim.Count > 0) { note = $"row {r} can only be filled by region {Lr(g)} → remove {Lr(g)} elsewhere: {Cells(_elim)}"; return true; }
                    }
                }
                for (int c = 0; c < n; c++)
                {
                    if (colDone[c]) continue;
                    var cells = ColCells(c);
                    if (cells.Count > 0 && AllSameRegion(cells, out int g))
                    {
                        _elim.Clear();
                        for (int i = 0; i < cand.Length; i++) if (region[i] == g && i % n != c) Elim(i);
                        if (_elim.Count > 0) { note = $"column {c} can only be filled by region {Lr(g)} → remove {Lr(g)} elsewhere: {Cells(_elim)}"; return true; }
                    }
                }
                note = null; return false;
            }

            bool TrySqueeze(out string note)
            {
                for (int g = 0; g < n; g++)
                {
                    if (regDone[g]) continue;
                    var cells = RegCells(g);
                    if (cells.Count < 2) continue;
                    _elim.Clear();
                    for (int idx = 0; idx < cand.Length; idx++)
                    {
                        if (!C(idx) || region[idx] == g) continue;
                        bool all = true;
                        foreach (int x in cells) if (!Touch(idx, x)) { all = false; break; }
                        if (all) Elim(idx);
                    }
                    if (_elim.Count > 0) { note = $"squeeze: region {Lr(g)}'s queen is among {Cells(cells)}; these touch all of them → remove {Cells(_elim)}"; return true; }
                }
                note = null; return false;
            }

            bool TrySubset(out string note)
            {
                if (SubsetDir(true, out note)) return true;
                if (SubsetDir(false, out note)) return true;
                note = null; return false;
            }

            bool SubsetDir(bool rows, out string note)
            {
                int[] mask = new int[n];
                var g = new List<int>();
                for (int reg = 0; reg < n; reg++)
                {
                    if (regDone[reg]) continue;
                    int mk = 0;
                    for (int idx = 0; idx < cand.Length; idx++) if (region[idx] == reg && C(idx)) mk |= 1 << (rows ? idx / n : idx % n);
                    if (mk != 0) { mask[reg] = mk; g.Add(reg); }
                }
                for (int a = 0; a < g.Count; a++)
                    for (int b = a + 1; b < g.Count; b++)
                        if (Pop(mask[g[a]] | mask[g[b]]) == 2 && Outside(rows, mask[g[a]] | mask[g[b]], g[a], g[b], -1))
                        { note = $"subset: regions {Lr(g[a])},{Lr(g[b])} fill 2 {(rows ? "rows" : "columns")} → remove others there: {Cells(_elim)}"; return true; }
                for (int a = 0; a < g.Count; a++)
                    for (int b = a + 1; b < g.Count; b++)
                        for (int c = b + 1; c < g.Count; c++)
                            if (Pop(mask[g[a]] | mask[g[b]] | mask[g[c]]) == 3 && Outside(rows, mask[g[a]] | mask[g[b]] | mask[g[c]], g[a], g[b], g[c]))
                            { note = $"subset: regions {Lr(g[a])},{Lr(g[b])},{Lr(g[c])} fill 3 {(rows ? "rows" : "columns")} → remove others there: {Cells(_elim)}"; return true; }
                note = null; return false;
            }

            bool Outside(bool rows, int lineMask, int g1, int g2, int g3)
            {
                _elim.Clear();
                for (int idx = 0; idx < cand.Length; idx++)
                {
                    if (!C(idx)) continue;
                    int rg = region[idx];
                    if (rg == g1 || rg == g2 || (g3 >= 0 && rg == g3)) continue;
                    int line = rows ? idx / n : idx % n;
                    if ((lineMask & (1 << line)) != 0) Elim(idx);
                }
                return _elim.Count > 0;
            }

            // ---- helpers ----

            bool C(int i) => cand[i] && !queen[i];
            int RowCount(int r) { int k = 0; for (int c = 0; c < n; c++) if (C(r * n + c)) k++; return k; }
            int ColCount(int c) { int k = 0; for (int r = 0; r < n; r++) if (C(r * n + c)) k++; return k; }
            int RegCount(int g) { int k = 0; for (int i = 0; i < cand.Length; i++) if (region[i] == g && C(i)) k++; return k; }
            List<int> RegCells(int g) { var l = new List<int>(); for (int i = 0; i < cand.Length; i++) if (region[i] == g && C(i)) l.Add(i); return l; }
            List<int> RowCells(int r) { var l = new List<int>(); for (int c = 0; c < n; c++) if (C(r * n + c)) l.Add(r * n + c); return l; }
            List<int> ColCells(int c) { var l = new List<int>(); for (int r = 0; r < n; r++) if (C(r * n + c)) l.Add(r * n + c); return l; }
            public List<int> RowCands(int r) { var l = new List<int>(); for (int c = 0; c < n; c++) if (C(r * n + c)) l.Add(c); return l; } // columns, not indices
            int MostConstrainedRow() { int best = -1, bk = int.MaxValue; for (int r = 0; r < n; r++) { if (rowDone[r]) continue; int k = RowCount(r); if (k > 1 && k < bk) { bk = k; best = r; } } return best; }
            bool AllSameRow(List<int> cells, out int row) { row = cells[0] / n; foreach (int i in cells) if (i / n != row) return false; return true; }
            bool AllSameCol(List<int> cells, out int col) { col = cells[0] % n; foreach (int i in cells) if (i % n != col) return false; return true; }
            bool AllSameRegion(List<int> cells, out int g) { g = region[cells[0]]; foreach (int i in cells) if (region[i] != g) return false; return true; }
            bool Touch(int a, int b) { int dr = Math.Abs(a / n - b / n), dc = Math.Abs(a % n - b % n); return Math.Max(dr, dc) == 1; }
            static int Pop(int x) { int c = 0; while (x != 0) { x &= x - 1; c++; } return c; }
            static char Lr(int g) => (char)('A' + g);
            string Cells(List<int> idxs)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < idxs.Count && i < 6; i++) sb.Append($"({idxs[i] / n},{idxs[i] % n})");
                if (idxs.Count > 6) sb.Append("…");
                return sb.ToString();
            }
        }
    }
}
