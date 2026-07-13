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
    /// Separate from <c>WeightRater</c> (which only needs the weight): this one trades
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

        /// <summary>
        /// The single cheapest next deduction from the player's current, mistake-free board — the
        /// same tricks the trace uses, tried cheapest-first, but just one step (no trials, no nodes).
        /// Seed the player's placed queens and X'd cells (both must be solution-consistent).
        /// Returns false when the next step would need a guess or a harder technique.
        /// </summary>
        public static bool TryHint(int n, int[] region, int[] solution,
                                   IReadOnlyList<int> queenCells, IReadOnlyList<int> xCells, out Hint hint,
                                   Func<int, string> regionName = null, string piece = "queen", string pieces = "queens")
        {
            var board = new Board(n, region, solution, new List<TraceNode>(), regionName, piece, pieces);
            if (queenCells != null) foreach (int idx in queenCells) board.SeedQueen(idx);
            if (xCells != null) foreach (int idx in xCells) board.KnownX(idx);
            return board.TryHint(out hint);
        }

        /// <summary>
        /// The most trivial forced queen from the seeded board: apply the cheapest deductions in
        /// order until the first forced placement appears — that's the easiest queen to deduce
        /// right now. False = no queen is strictly forced (the board would need a guess).
        /// </summary>
        public static bool TryQueenBoost(int n, int[] region, int[] solution,
                                         IReadOnlyList<int> queenCells, IReadOnlyList<int> xCells, out int queenIdx)
        {
            queenIdx = -1;
            var board = new Board(n, region, solution, new List<TraceNode>());
            if (queenCells != null) foreach (int idx in queenCells) board.SeedQueen(idx);
            if (xCells != null) foreach (int idx in xCells) board.KnownX(idx);
            if (board.Solved) return false;

            // NextStep applies the cheapest deduction each call; the first placement it makes is it.
            for (int guard = n * n * 8; guard-- > 0 && board.NextStep(out var step); )
                if (step.kind == NodeKind.Placement) { queenIdx = step.cells[0]; return true; }

            return false;
        }

        public class Board
        {
            readonly int n;
            readonly int[] region, sol;
            readonly Func<int, string> _name;   // how notes name a region — letters by default, the game injects colour names
            readonly string _piece, _pieces;    // what notes call the piece — queen(s) by default; puppy/cat per game
            bool[] cand, queen, rowDone, colDone, regDone;
            int placed;
            bool _noChains;             // set on what-if boards — chains may not nest inside chains
            int _k;                     // size of the last subset/fish that fired (for weighting)
            public readonly List<TraceNode> Nodes;
            readonly List<int> _elim = new List<int>();

            public int N => n;
            public bool Solved => placed == n;

            public Board(int n, int[] region) : this(n, region, null, null) { }

            public Board(int n, int[] region, int[] sol, List<TraceNode> nodes, Func<int, string> regionName = null,
                         string piece = "queen", string pieces = "queens")
            {
                this.n = n; this.region = region; this.sol = sol; Nodes = nodes;
                _name = regionName ?? Lr;
                _piece = piece; _pieces = pieces;
                cand = new bool[n * n];
                for (int i = 0; i < cand.Length; i++) cand[i] = true;
                queen = new bool[n * n]; rowDone = new bool[n]; colDone = new bool[n]; regDone = new bool[n];
            }

            public Board Clone()
            {
                var b = new Board(n, region, sol, Nodes, _name, _piece, _pieces);
                b.cand = (bool[])cand.Clone(); b.queen = (bool[])queen.Clone();
                b.rowDone = (bool[])rowDone.Clone(); b.colDone = (bool[])colDone.Clone(); b.regDone = (bool[])regDone.Clone();
                b.placed = placed;
                b._noChains = _noChains;
                return b;
            }

            int Add(int parent, NodeKind kind, SolveTechnique tech, Outcome outcome, TraceMark[] marks, string note, int cost = 0, bool streak = false)
            {
                Nodes.Add(new TraceNode { parent = parent, technique = tech, kind = kind, outcome = outcome, marks = marks, note = note, cost = cost, streak = streak });
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
                var prevTech = SolveTechnique.None; // last paid trick — a repeat halves the think cost
                while (true)
                {
                    if (HasEmptyUnit()) return Outcome.DeadEnd;
                    if (placed == n) return Outcome.Solved;

                    int open = OpenCells();
                    if (NextStep(out var step))
                    {
                        bool streak = step.tech == prevTech && TrickWeights.Streakable(step.tech);
                        if (TrickWeights.Streakable(step.tech)) prevTech = step.tech;
                        var marks = step.kind == NodeKind.Placement ? QueenAt(step.cells[0]) : XMarks(step.cells);
                        cur = Add(cur, step.kind, step.tech, Outcome.Continues, marks, step.note,
                            TrickWeights.StepCost(step.tech, step.k, open, streak), streak);
                        continue;
                    }

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
                        NoMarks, $"stuck — guess in row {row + 1} ({cols.Count} options)");

                    int rightBranch = -1;
                    foreach (int c in cols)
                    {
                        int branch = Add(trial, NodeKind.TrialBranch, SolveTechnique.Trial, Outcome.Continues,
                            QueenAt(row * n + c), $"try a queen in row {row + 1}");
                        if (guided && c == sol[row]) { rightBranch = branch; continue; } // the real path continues here
                        var clone = Clone();
                        clone.Place(row, c);
                        Outcome outc = clone.Run(branch, depth + 1, guided: false);
                        SetOutcome(branch, outc == Outcome.Solved ? Outcome.Continues : outc);
                    }

                    if (!guided) return Outcome.DeadEnd; // an impossible line — every option exhausted
                    Place(row, sol[row]);
                    cur = rightBranch;
                    prevTech = SolveTechnique.None;      // a guess is a context switch — streak broken
                }
            }

            // ---- one shared deduction step: the trace, the weight and the hint all go through here ----

            public struct DeductionStep { public NodeKind kind; public SolveTechnique tech; public int k; public int[] cells; public string note; }

            public void SeedQueen(int idx) { Place(idx / n, idx % n); }
            public void KnownX(int idx) { if (!queen[idx]) cand[idx] = false; }

            // The cheapest applicable deduction, applied to the board. False = stuck (would need a guess).
            public bool NextStep(out DeductionStep step)
            {
                step = default;

                if (TryQueenElim(out string qnote))
                {
                    step = new DeductionStep { kind = NodeKind.Elimination, tech = SolveTechnique.QueenScope, cells = _elim.ToArray(), note = qnote };
                    return true;
                }

                int idx = RegionSingle();
                if (idx >= 0)
                {
                    Place(idx / n, idx % n);
                    step = new DeductionStep { kind = NodeKind.Placement, tech = SolveTechnique.RegionSingle, cells = new[] { idx },
                        note = $"1 {_piece} per color {_name(region[idx])}" };
                    return true;
                }

                idx = LineSingle(out bool isRow, out int line);
                if (idx >= 0)
                {
                    Place(idx / n, idx % n);
                    step = new DeductionStep { kind = NodeKind.Placement, tech = SolveTechnique.LineSingle, cells = new[] { idx },
                        note = $"1 {_piece} per {(isRow ? "row" : "column")}" };
                    return true;
                }

                string note;
                if (TryLineToRegion(out note)) return Elimed(SolveTechnique.LineToRegion, 0, note, out step);
                if (TryRegionToLine(out note)) return Elimed(SolveTechnique.RegionToLine, 0, note, out step);
                if (TrySqueeze(out note)) return Elimed(SolveTechnique.Squeeze, 0, note, out step);

                // the k-scaling tricks in TRUE weight order, so the cheapest representation fires
                // first — a k=7 line subset is the same fact as a k=2 region subset seen from the
                // other side, and the k=2 view is what a human finds.
                foreach (var (tech, k, _, _) in KOrder())
                {
                    switch (tech)
                    {
                        case SolveTechnique.SubsetLineToRegion:
                            if (TrySubsetLineToRegion(k, out note)) return Elimed(tech, k, note, out step);
                            break;
                        case SolveTechnique.SubsetRegionToLine:
                            if (TrySubsetRegionToLine(k, out note)) return Elimed(tech, k, note, out step);
                            break;
                        case SolveTechnique.RegionChoke:
                            if (TryRegionChoke(out note)) return Elimed(tech, _k, note, out step);
                            break;
                        case SolveTechnique.Fish:
                            if (TryFish(k, out note)) return Elimed(tech, k, note, out step);
                            break;
                    }
                }

                // Last resort before a guess: short what-if chains — test each cell of a small
                // unit (2-4 candidates left), follow only the easy moves (shadows, singles,
                // simple confinements), at most 2 forced queens deep. Cheapest anchors first.
                if (!_noChains)
                    foreach (var (k, d) in ChainSteps)
                        if (TryShortChain(k, d, out note)) return Elimed(SolveTechnique.ShortChain, _k, note, out step);

                return false;
            }

            // (technique, k) pairs sorted by weight; ties break colour-cued before positional.
            List<(SolveTechnique tech, int k, int w, int tie)> KOrder()
            {
                // choke's real cost depends on the region it starves — order it by its cheapest form (k=2)
                var order = new List<(SolveTechnique tech, int k, int w, int tie)>
                { (SolveTechnique.RegionChoke, 0, TrickWeights.Of(SolveTechnique.RegionChoke, 2), 0) };
                for (int k = 2; k < n; k++)
                {
                    order.Add((SolveTechnique.SubsetLineToRegion, k, TrickWeights.Of(SolveTechnique.SubsetLineToRegion, k), 1));
                    order.Add((SolveTechnique.SubsetRegionToLine, k, TrickWeights.Of(SolveTechnique.SubsetRegionToLine, k), 1));
                    order.Add((SolveTechnique.Fish, k, TrickWeights.Of(SolveTechnique.Fish, k), 2));
                }
                order.Sort((a, b) => a.w != b.w ? a.w - b.w : a.tie - b.tie);
                return order;
            }

            bool Elimed(SolveTechnique tech, int k, string note, out DeductionStep step)
            { step = new DeductionStep { kind = NodeKind.Elimination, tech = tech, k = k, cells = _elim.ToArray(), note = note }; return true; }

            // A placed queen's eliminations, split into three separate hints: its row/column,
            // then its region, then the 8 cells it touches.
            bool TryQueenElim(out string note)
            {
                _elim.Clear();
                for (int idx = 0; idx < cand.Length; idx++)
                {
                    if (!C(idx)) continue;
                    if (rowDone[idx / n] || colDone[idx % n]) Elim(idx);
                }
                if (_elim.Count > 0) { note = $"1 {_piece} per row and column"; return true; }

                _elim.Clear();
                for (int idx = 0; idx < cand.Length; idx++)
                {
                    if (!C(idx)) continue;
                    if (regDone[region[idx]]) Elim(idx);
                }
                if (_elim.Count > 0) { note = $"1 {_piece} per color"; return true; }

                _elim.Clear();
                for (int idx = 0; idx < cand.Length; idx++)
                {
                    if (!C(idx)) continue;
                    if (AdjacentToQueen(idx / n, idx % n)) Elim(idx);
                }
                if (_elim.Count > 0) { note = $"{_pieces} cannot touch"; return true; }

                note = null; return false;
            }

            bool AdjacentToQueen(int r, int c)
            {
                for (int dr = -1; dr <= 1; dr++)
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        if (dr == 0 && dc == 0) continue;
                        int nr = r + dr, nc = c + dc;
                        if (nr >= 0 && nr < n && nc >= 0 && nc < n && queen[nr * n + nc]) return true;
                    }
                return false;
            }

            // Hint = one shared NextStep from the seeded, mistake-free board.
            public bool TryHint(out Hint hint)
            {
                hint = default;
                if (placed == n || HasEmptyUnit()) return false;

                if (NextStep(out var step))
                {
                    hint = new Hint {
                        kind = step.kind == NodeKind.Placement ? HintKind.PlaceQueen : HintKind.Eliminate,
                        cells = step.cells,   // one deduction step (may touch several cells)
                        note = step.note
                    };
                    return true;
                }

                // No forced move — guess in the most-constrained row: rule out an option that
                // leads to a contradiction (X it). The last one left then falls out as a single.
                if (TryTrialElim(out int cell, out int options))
                {
                    // Eliminate, not Guess: the X is proven (by contradiction), and the tutorial
                    // needs a target state — a target-less spotlight has no way to close.
                    hint = new Hint { kind = HintKind.Eliminate, cells = new[] { cell },
                        note = $"a {_piece} here leads to a dead end → mark it X" };
                    return true;
                }

                // the contradiction needs deeper look-ahead than one step — fall back to the queen
                int row = MostConstrainedRow();
                if (row < 0 || sol == null) return false;
                // PlaceQueen, not Guess: the cell comes from the solution so the move is safe,
                // and the step must have a target state for Apply/completion to work.
                hint = new Hint { kind = HintKind.PlaceQueen, cells = new[] { row * n + sol[row] },
                    note = $"no certain move — best guess: a {_piece} here" };
                return true;
            }

            // No basic deduction — in the most-constrained row, find an option that can be ruled out:
            // assume a queen there; if it propagates to a contradiction it must be an X.
            bool TryTrialElim(out int cell, out int options)
            {
                cell = -1; options = 0;
                int row = MostConstrainedRow();
                if (row < 0) return false;
                var cols = RowCands(row);
                options = cols.Count;
                foreach (int col in cols)
                {
                    var clone = Clone();
                    clone.Place(row, col);
                    if (clone.RunsToContradiction()) { cell = row * n + col; return true; }
                }
                return false;
            }

            // Propagate deductions until solved / stuck / contradiction. True = hit an empty unit.
            bool RunsToContradiction()
            {
                while (true)
                {
                    if (HasEmptyUnit()) return true;
                    if (placed == n) return false;
                    if (!NextStep(out _)) return false;   // stuck at this depth — don't nest here
                }
            }

            TraceMark[] XMarks(int[] cells)
            {
                var a = new TraceMark[cells.Length];
                for (int i = 0; i < cells.Length; i++) a[i] = new TraceMark { cell = cells[i], mark = CellMark.X };
                return a;
            }

            // ---- placement & rules ----

            public void Place(int r, int c)
            {
                int i = r * n + c;
                if (queen[i]) return;
                queen[i] = true; cand[i] = true; placed++;
                rowDone[r] = true; colDone[c] = true; regDone[region[i]] = true;
                // the queen's eliminations (row/column/region/neighbours) are surfaced as an explicit
                // step by NextStep (SolveTechnique.QueenScope), not applied silently here.
            }

            void Elim(int idx) { if (cand[idx] && !queen[idx]) { cand[idx] = false; _elim.Add(idx); } }

            public bool HasEmptyUnit()
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
                        if (_elim.Count > 0) { note = $"{_name(g)} fits only in row {rr + 1} → clear other colors from row {rr + 1}"; return true; }
                    }
                    if (AllSameCol(cells, out int cc))
                    {
                        _elim.Clear();
                        for (int r = 0; r < n; r++) { int i = r * n + cc; if (region[i] != g) Elim(i); }
                        if (_elim.Count > 0) { note = $"{_name(g)} fits only in column {cc + 1} → clear other colors from column {cc + 1}"; return true; }
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
                        if (_elim.Count > 0) { note = $"row {r + 1} is all {_name(g)} → clear {_name(g)} outside row {r + 1}"; return true; }
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
                        if (_elim.Count > 0) { note = $"column {c + 1} is all {_name(g)} → clear {_name(g)} outside column {c + 1}"; return true; }
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
                    if (_elim.Count > 0) { note = $"{_name(g)}'s {_piece} attacks every marked cell"; return true; }
                }
                note = null; return false;
            }

            // A queen on the marked cell would attack ALL of region g's candidates (its row, its
            // column and the cells it touches combined) → g would have nowhere to go → mark it X.
            // Generalizes Squeeze, which only counts the touch part of the attack.
            bool TryRegionChoke(out string note)
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
                        foreach (int x in cells)
                            if (x / n != idx / n && x % n != idx % n && !Touch(idx, x)) { all = false; break; }
                        if (all) Elim(idx);
                    }
                    if (_elim.Count > 0) { _k = cells.Count; note = $"a {_piece} on a marked cell leaves {_name(g)} no room → mark it X"; return true; }
                }
                note = null; return false;
            }

            bool TrySubsetRegionToLine(int k, out string note)
            {
                if (SubsetDir(true, k, out note)) return true;
                if (SubsetDir(false, k, out note)) return true;
                note = null; return false;
            }

            bool TrySubsetLineToRegion(int k, out string note)
            {
                if (LineSubsetDir(true, k, out note)) return true;
                if (LineSubsetDir(false, k, out note)) return true;
                note = null; return false;
            }

            // region → line subset at a specific k: k regions filling exactly k lines → clear other regions there.
            bool SubsetDir(bool rows, int k, out string note)
            {
                note = null;
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
                if (k < 2 || k >= gc) return false;
                var sel = new int[k];
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
                        _k = k;
                        note = $"colors {Letters(regs)} fill {k} {(rows ? "rows" : "columns")} {Lines(u)} → no other color fits there, clear them out";
                        return true;
                    }
                } while (NextCombo(sel, k, gc));
                return false;
            }

            // line → region subset at a specific k: k rows/columns holding only k colours → clear those colours elsewhere.
            bool LineSubsetDir(bool rows, int k, out string note)
            {
                note = null;
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
                if (k < 2 || k >= lc) return false;
                string what = rows ? "rows" : "columns";
                var sel = new int[k];
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
                        _k = k;
                        note = $"{what} {Lines(lines)} hold only colors {Letters(u)} → those colors live here, clear them everywhere else";
                        return true;
                    }
                } while (NextCombo(sel, k, lc));
                return false;
            }

            bool TryFish(int k, out string note)
            {
                if (FishDir(true, k, out note)) return true;
                if (FishDir(false, k, out note)) return true;
                note = null; return false;
            }

            // positional fish (X-Wing/Swordfish) at a specific k: k rows whose candidate columns span
            // exactly k columns → those rows own those columns → clear those columns in other rows.
            // Colour-agnostic. rows=false is the column↔row mirror.
            bool FishDir(bool rows, int k, out string note)
            {
                note = null;
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
                if (k < 2 || k >= lc) return false;
                var sel = new int[k];
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
                        _k = k;
                        string lw = rows ? "rows" : "columns", pw = rows ? "columns" : "rows";
                        note = $"{lw} {Lines(set)} are confined to {pw} {Lines(u)} → those {pw} are theirs, clear them everywhere else";
                        return true;
                    }
                } while (NextCombo(sel, k, lc));
                return false;
            }

            // (anchor size, forced queens) pairs sorted by cost k·(1+d) — cheapest what-if first.
            // d=0 is the instant starvation (line-choke) case; d≥1 follows forced queens.
            // Anchors up to 6 cells — walking a whole small column/colour is still a player
            // move — and chains up to 6 forced queens, but only where k·(1+d) stays affordable:
            // a wide anchor with a deep chain prices itself out. Beyond all that = guessing.
            const int MaxChainCost = 14;   // k·(1+d) cap ≈ think 220 — the last stop before a trial
            static readonly (int k, int d)[] ChainSteps = BuildChainSteps();
            static (int k, int d)[] BuildChainSteps()
            {
                var l = new List<(int k, int d)>();
                for (int k = 2; k <= 6; k++)
                    for (int d = 0; d <= 6; d++)
                        if (k * (1 + d) <= MaxChainCost) l.Add((k, d));
                l.Sort((a, b) => a.k * (1 + a.d) - b.k * (1 + b.d));
                return l.ToArray();
            }

            // Test every cell of an anchor unit (region/row/column) holding exactly k candidates:
            // a player naturally tries the last few cells of a small unit. Any tested cell whose
            // what-if reaches a dead unit within d forced queens is proven X. One anchor per firing.
            bool TryShortChain(int k, int d, out string note)
            {
                note = null;
                for (int g = 0; g < n; g++)
                {
                    if (regDone[g]) continue;
                    var cells = RegCells(g);
                    if (cells.Count == k && ChainAnchor(cells, k, d, _name(g), ref note)) return true;
                }
                for (int r = 0; r < n; r++)
                {
                    if (rowDone[r]) continue;
                    var cells = RowCells(r);
                    if (cells.Count == k && ChainAnchor(cells, k, d, $"row {r + 1}", ref note)) return true;
                }
                for (int c = 0; c < n; c++)
                {
                    if (colDone[c]) continue;
                    var cells = ColCells(c);
                    if (cells.Count == k && ChainAnchor(cells, k, d, $"column {c + 1}", ref note)) return true;
                }
                return false;
            }

            bool ChainAnchor(List<int> cells, int k, int d, string name, ref string note)
            {
                _elim.Clear();
                foreach (int idx in cells)
                {
                    var b = Clone();
                    b.Place(idx / n, idx % n);
                    if (ChainDies(b, d)) Elim(idx);
                }
                if (_elim.Count == 0) return false;
                _k = k * (1 + d);   // the whole family prices as 80 + 10·k·(1+d)
                note = d == 0
                    ? $"a {_piece} on a marked cell of {name} leaves some unit no room → mark it X"
                    : $"what-if on {name}: a {_piece} on a marked cell dies after {d} forced {(d == 1 ? "move" : "moves")} → mark it X";
                return true;
            }

            // Follow only the easy moves on the what-if board: shadows, singles and the simple
            // confinements (think weight ≤ 20) — the moves a player makes without stopping.
            // Anything harder means it is not a short chain. True = a unit died in time.
            static bool ChainDies(Board b, int maxQueens)
            {
                b._noChains = true;
                int queens = 0;
                while (true)
                {
                    if (b.HasEmptyUnit()) return true;
                    if (b.Solved) return false;
                    if (!b.NextStep(out var st)) return false;
                    if (TrickWeights.Of(st.tech, st.k) > 20) return false;
                    if (st.kind == NodeKind.Placement && ++queens > maxQueens) return false;
                }
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
            public int OpenCells() { int k = 0; for (int i = 0; i < cand.Length; i++) if (C(i)) k++; return k; }
            public int MostConstrainedRow() { int best = -1, bk = int.MaxValue; for (int r = 0; r < n; r++) { if (rowDone[r]) continue; int k = RowCount(r); if (k > 1 && k < bk) { bk = k; best = r; } } return best; }
            bool AllSameRow(List<int> cells, out int row) { row = cells[0] / n; foreach (int i in cells) if (i / n != row) return false; return true; }
            bool AllSameCol(List<int> cells, out int col) { col = cells[0] % n; foreach (int i in cells) if (i % n != col) return false; return true; }
            bool AllSameRegion(List<int> cells, out int g) { g = region[cells[0]]; foreach (int i in cells) if (region[i] != g) return false; return true; }
            bool Touch(int a, int b) { int dr = Math.Abs(a / n - b / n), dc = Math.Abs(a % n - b % n); return Math.Max(dr, dc) == 1; }
            static int Pop(int x) { int c = 0; while (x != 0) { x &= x - 1; c++; } return c; }
            static string Lr(int g) => ((char)('A' + g)).ToString();   // default region name: a letter

            // "5, 6, 7" from a bitmask or a list of line indices — 1-BASED for the player
            // (internally lines are 0-based; every hint text shows row/column numbers from 1)
            static string Lines(int mask) { var l = new List<int>(); for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) l.Add(i); return Lines(l); }
            static string Lines(List<int> xs)
            {
                xs.Sort();
                var sb = new StringBuilder();
                for (int i = 0; i < xs.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(xs[i] + 1); }
                return sb.ToString();
            }

            // "C, D, F" (or "Pink, Teal, …" in game) from a region bitmask or a list of region ids
            string Letters(int mask) { var l = new List<int>(); for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) l.Add(i); return Letters(l); }
            string Letters(List<int> gs)
            {
                gs.Sort();
                var sb = new StringBuilder();
                for (int i = 0; i < gs.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(_name(gs[i])); }
                return sb.ToString();
            }
        }
    }
}
