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

            int Add(int parent, NodeKind kind, SolveTechnique tech, Outcome outcome, TraceMark[] marks, string note)
            {
                Nodes.Add(new TraceNode { parent = parent, technique = tech, kind = kind, outcome = outcome, marks = marks, note = note });
                return Nodes.Count - 1;
            }

            void SetOutcome(int idx, Outcome o) { var nd = Nodes[idx]; nd.outcome = o; Nodes[idx] = nd; }

            static readonly TraceMark[] NoMarks = new TraceMark[0];
            static TraceMark[] QueenAt(int idx) => new[] { new TraceMark { cell = idx, mark = CellMark.Queen } };
            // X-marks for whatever the last rule eliminated (it leaves the cells in _elim).
            TraceMark[] ElimMarks()
            {
                var a = new TraceMark[_elim.Count];
                for (int i = 0; i < _elim.Count; i++) a[i] = new TraceMark { cell = _elim[i], mark = CellMark.X };
                return a;
            }

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
                        cur = Add(cur, NodeKind.Placement, SolveTechnique.RegionSingle, Outcome.Continues,
                            QueenAt(idx), $"region {Lr(region[idx])} is down to one cell → place its queen");
                        Place(r, c); continue;
                    }
                    idx = LineSingle(out bool isRow, out int line);
                    if (idx >= 0)
                    {
                        int r = idx / n, c = idx % n;
                        cur = Add(cur, NodeKind.Placement, SolveTechnique.LineSingle, Outcome.Continues,
                            QueenAt(idx), $"{(isRow ? "row" : "column")} {line} is down to one cell → place its queen");
                        Place(r, c); continue;
                    }
                    if (TryLineToRegion(out string note))
                    { cur = Add(cur, NodeKind.Elimination, SolveTechnique.LineToRegion, Outcome.Continues, ElimMarks(), note); continue; }
                    if (TryRegionToLine(out note))
                    { cur = Add(cur, NodeKind.Elimination, SolveTechnique.RegionToLine, Outcome.Continues, ElimMarks(), note); continue; }
                    if (TrySqueeze(out note))
                    { cur = Add(cur, NodeKind.Elimination, SolveTechnique.Squeeze, Outcome.Continues, ElimMarks(), note); continue; }
                    if (TrySubsetLineToRegion(out note))
                    { cur = Add(cur, NodeKind.Elimination, SolveTechnique.SubsetLineToRegion, Outcome.Continues, ElimMarks(), note); continue; }
                    if (TrySubsetRegionToLine(out note))
                    { cur = Add(cur, NodeKind.Elimination, SolveTechnique.SubsetRegionToLine, Outcome.Continues, ElimMarks(), note); continue; }
                    if (TryFish(out note))
                    { cur = Add(cur, NodeKind.Elimination, SolveTechnique.Fish, Outcome.Continues, ElimMarks(), note); continue; }

                    // stuck → trial
                    if (depth >= MaxDepth)
                    {
                        Add(cur, NodeKind.TrialRoot, SolveTechnique.Trial, Outcome.Unresolved,
                            NoMarks, $"would need a deeper guess (depth limit {MaxDepth})");
                        return Outcome.Unresolved;
                    }
                    int row = MostConstrainedRow();
                    var cols = RowCands(row);
                    int trial = Add(cur, NodeKind.TrialRoot, SolveTechnique.Trial, Outcome.Continues,
                        NoMarks, $"stuck — guess in row {row} ({cols.Count} options)");

                    int rightBranch = -1;
                    foreach (int c in cols)
                    {
                        int branch = Add(trial, NodeKind.TrialBranch, SolveTechnique.Trial, Outcome.Continues,
                            QueenAt(row * n + c), $"try a queen in row {row}");
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

            // region's cells all sit in one row/column → no other region can use that line
            bool TryRegionToLine(out string note)
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
                        if (_elim.Count > 0) { note = $"region {Lr(g)} is confined to row {rr} → that row is {Lr(g)}'s queen, clear other colours from row {rr}"; return true; }
                    }
                    if (AllSameCol(cells, out int cc))
                    {
                        _elim.Clear();
                        for (int r = 0; r < n; r++) { int i = r * n + cc; if (region[i] != g) Elim(i); }
                        if (_elim.Count > 0) { note = $"region {Lr(g)} is confined to column {cc} → that column is {Lr(g)}'s queen, clear other colours from column {cc}"; return true; }
                    }
                }
                note = null; return false;
            }

            // a row/column is a single colour → that colour's queen is there → clear it elsewhere
            bool TryLineToRegion(out string note)
            {
                for (int r = 0; r < n; r++)
                {
                    if (rowDone[r]) continue;
                    var cells = RowCells(r);
                    if (cells.Count > 0 && AllSameRegion(cells, out int g))
                    {
                        _elim.Clear();
                        for (int i = 0; i < cand.Length; i++) if (region[i] == g && i / n != r) Elim(i);
                        if (_elim.Count > 0) { note = $"row {r} is all one colour ({Lr(g)}) → {Lr(g)}'s queen is in row {r}, clear {Lr(g)} everywhere else"; return true; }
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
                        if (_elim.Count > 0) { note = $"column {c} is all one colour ({Lr(g)}) → {Lr(g)}'s queen is in column {c}, clear {Lr(g)} everywhere else"; return true; }
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
                    if (_elim.Count > 0) { note = $"region {Lr(g)}'s queen ({cells.Count} options left) attacks every marked cell → none of them can hold a queen"; return true; }
                }
                note = null; return false;
            }

            bool TrySubsetRegionToLine(out string note)
            {
                if (SubsetDir(true, out note)) return true;
                if (SubsetDir(false, out note)) return true;
                note = null; return false;
            }

            bool TrySubsetLineToRegion(out string note)
            {
                if (LineSubsetDir(true, out note)) return true;
                if (LineSubsetDir(false, out note)) return true;
                note = null; return false;
            }

            // region → line subset, general k: k regions filling exactly k lines → clear other regions there.
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
                int gc = g.Count;
                var sel = new int[gc];
                for (int k = 2; k < gc; k++)
                {
                    for (int i = 0; i < k; i++) sel[i] = i;
                    do
                    {
                        int u = 0; for (int i = 0; i < k; i++) u |= mask[g[sel[i]]];
                        if (Pop(u) != k) continue;
                        _elim.Clear();
                        for (int idx = 0; idx < cand.Length; idx++)
                        {
                            if (!C(idx)) continue;
                            if ((u & (1 << (rows ? idx / n : idx % n))) == 0) continue;
                            bool inSet = false; for (int i = 0; i < k; i++) if (region[idx] == g[sel[i]]) { inSet = true; break; }
                            if (!inSet) Elim(idx);
                        }
                        if (_elim.Count > 0)
                        {
                            var regs = new List<int>(); for (int i = 0; i < k; i++) regs.Add(g[sel[i]]);
                            note = $"colours {Letters(regs)} fill {k} {(rows ? "rows" : "columns")} {Lines(u)} → no other colour fits there, clear them out";
                            return true;
                        }
                    } while (NextCombo(sel, k, gc));
                }
                note = null; return false;
            }

            // line → region subset, general k: k rows/columns holding only k colours → clear those colours elsewhere.
            bool LineSubsetDir(bool rows, out string note)
            {
                int[] rmask = new int[n];
                var L = new List<int>();
                for (int line = 0; line < n; line++)
                {
                    if (rows ? rowDone[line] : colDone[line]) continue;
                    int mk = 0;
                    for (int idx = 0; idx < cand.Length; idx++) { if (!C(idx)) continue; if ((rows ? idx / n : idx % n) != line) continue; mk |= 1 << region[idx]; }
                    if (mk != 0) { rmask[line] = mk; L.Add(line); }
                }
                int lc = L.Count;
                string what = rows ? "rows" : "columns";
                var sel = new int[lc];
                for (int k = 2; k < lc; k++)
                {
                    for (int i = 0; i < k; i++) sel[i] = i;
                    do
                    {
                        int u = 0; for (int i = 0; i < k; i++) u |= rmask[L[sel[i]]];
                        if (Pop(u) != k) continue;
                        _elim.Clear();
                        for (int idx = 0; idx < cand.Length; idx++)
                        {
                            if (!C(idx)) continue;
                            if ((u & (1 << region[idx])) == 0) continue;
                            int line = rows ? idx / n : idx % n;
                            bool inLines = false; for (int i = 0; i < k; i++) if (L[sel[i]] == line) { inLines = true; break; }
                            if (!inLines) Elim(idx);
                        }
                        if (_elim.Count > 0)
                        {
                            var lines = new List<int>(); for (int i = 0; i < k; i++) lines.Add(L[sel[i]]);
                            note = $"{what} {Lines(lines)} hold only colours {Letters(u)} → those colours are confined here, clear them everywhere else";
                            return true;
                        }
                    } while (NextCombo(sel, k, lc));
                }
                note = null; return false;
            }

            bool TryFish(out string note)
            {
                if (FishDir(true, out note)) return true;
                if (FishDir(false, out note)) return true;
                note = null; return false;
            }

            // positional fish (X-Wing/Swordfish), general k: k rows whose candidate columns span
            // exactly k columns → those rows own those columns → clear those columns in other rows.
            // Colour-agnostic. rows=false is the column↔row mirror.
            bool FishDir(bool rows, out string note)
            {
                int[] mask = new int[n];
                var L = new List<int>();
                for (int line = 0; line < n; line++)
                {
                    if (rows ? rowDone[line] : colDone[line]) continue;
                    int mk = 0;
                    for (int p = 0; p < n; p++) { int idx = rows ? line * n + p : p * n + line; if (C(idx)) mk |= 1 << p; }
                    if (mk != 0) { mask[line] = mk; L.Add(line); }
                }
                int lc = L.Count;
                var sel = new int[lc];
                for (int k = 2; k < lc; k++)
                {
                    for (int i = 0; i < k; i++) sel[i] = i;
                    do
                    {
                        int u = 0; for (int i = 0; i < k; i++) u |= mask[L[sel[i]]];
                        if (Pop(u) != k) continue;
                        _elim.Clear();
                        for (int line = 0; line < n; line++)
                        {
                            bool inSet = false; for (int i = 0; i < k; i++) if (L[sel[i]] == line) { inSet = true; break; }
                            if (inSet) continue;
                            for (int p = 0; p < n; p++)
                            {
                                if ((u & (1 << p)) == 0) continue;
                                int idx = rows ? line * n + p : p * n + line;
                                if (C(idx)) Elim(idx);
                            }
                        }
                        if (_elim.Count > 0)
                        {
                            var set = new List<int>(); for (int i = 0; i < k; i++) set.Add(L[sel[i]]);
                            string lw = rows ? "rows" : "columns", pw = rows ? "columns" : "rows";
                            note = $"{lw} {Lines(set)} are confined to {pw} {Lines(u)} → those {pw} are theirs, clear them everywhere else";
                            return true;
                        }
                    } while (NextCombo(sel, k, lc));
                }
                note = null; return false;
            }

            static bool NextCombo(int[] sel, int k, int total)
            {
                int p = k - 1;
                while (p >= 0 && sel[p] == total - k + p) p--;
                if (p < 0) return false;
                sel[p]++;
                for (int i = p + 1; i < k; i++) sel[i] = sel[i - 1] + 1;
                return true;
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

            // "5, 6, 7" from a bitmask or a list of line indices
            static string Lines(int mask) { var l = new List<int>(); for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) l.Add(i); return Lines(l); }
            static string Lines(List<int> xs)
            {
                xs.Sort();
                var sb = new StringBuilder();
                for (int i = 0; i < xs.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(xs[i]); }
                return sb.ToString();
            }

            // "C, D, F" from a region bitmask or a list of region ids
            static string Letters(int mask) { var l = new List<int>(); for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) l.Add(i); return Letters(l); }
            static string Letters(List<int> gs)
            {
                gs.Sort();
                var sb = new StringBuilder();
                for (int i = 0; i < gs.Count; i++) { if (i > 0) sb.Append(", "); sb.Append((char)('A' + gs[i])); }
                return sb.ToString();
            }
        }
    }
}
