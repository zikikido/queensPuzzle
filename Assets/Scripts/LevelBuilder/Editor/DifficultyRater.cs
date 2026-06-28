using System;
using System.Collections.Generic;

namespace QueensPuzzle
{
    /// <summary>
    /// Rates a level's difficulty by solving it the way a human would, then scoring the work.
    ///
    /// Deduction tricks (cheapest → hardest), with hardness weight:
    ///   region single   w0  — a region with one candidate → place a queen
    ///   line single     w1  — a row/column with one candidate → place a queen
    ///   queen-elim      w0  — a placed queen blocks its row/col/region/neighbors (automatic)
    ///   region-line     w2  — a region trapped in one row/col → clear the rest of that line
    ///   squeeze         w4  — eliminate cells that touch ALL of a region's candidates
    ///   subset          3+k / 4+k — k lines hold k colours / k regions fill k lines (Hall)
    ///   positional fish 5+k — k rows confined to k columns (or vice-versa) → clear those lines
    ///
    /// All deduction is "on the board" (visual) and cheap; a what-if GUESS is the wall, scored by the
    /// work it forces (cheapest contradiction wins — luck-free), so a guess that opens an easy board
    /// stays Medium while a costly or nested one climbs. A FATIGUE term then adds for how many hard
    /// moves/guesses the solve needs, so a long grind outranks a single "aha":
    ///
    ///   hardness = max( hardest trick ,  GuessBase + chain + Mult × inner )  +  fatigue(hard steps)
    ///
    /// Tiers by score: 0=Kitten, 1=Easy, 2..7=Medium, 8..31=Hard, 32+=Expert. With 3 lives a single
    /// cheap guess is survivable trial-and-error (Medium); Expert is the long / nested / multi-guess grind.
    ///
    /// We pass in the level's known unique solution so the solver only has to *disprove the wrong
    /// cells* (cheap) instead of *discovering* the right one (expensive search). This is the
    /// smartest, luck-free measure. Full model documented in docs/difficulty-model.svg.
    ///
    /// Pure C# (no Unity types) so it is unit-testable and runs at build time.
    /// </summary>
    public static class DifficultyRater
    {
        const int GuessBase = 3;     // base cost of any what-if — small, so a trivial guess can stay Medium
        const int Mult = 10;         // each nesting level multiplies hard → a nested guess always hits Expert
        const int BreadthWeight = 2; // a wider guess (more cells to consider) costs more, per cell past 2
        const int MaxProbe = 2;      // stop exploring once a disproof needs depth-2 nesting (already Expert)
        const int StepCap = 1_500_000; // safety net against pathological recursion

        public struct Report
        {
            public bool solved;
            public Difficulty difficulty;
            public string technique;
            public int score;            // = recursive hardness
            public int size;
            public int cycles;
            public int placements;
            public int regionSingles;
            public int lineSingles;
            public int eliminations;
            public int lineToRegionUses;
            public int regionToLineUses;
            public int squeezeUses;
            public int subsetLineToRegionUses;
            public int subsetRegionToLineUses;
            public int fishUses;
            public int trials;
            public int maxTrialDepth;
            public float estimatedSeconds;
        }

        enum PStatus { Contradiction, Solved, Stuck }

        public static Report Rate(int n, int[] region, int[] solution)
        {
            var m = new Metrics();
            var ctx = new Ctx();
            var s = new State(n, region);

            var (solved, hardness) = Solve(s, solution, m, ctx);
            if (ctx.aborted) { solved = true; hardness = Math.Max(hardness, 48); }
            // fatigue: a solve that needs many hard moves / guesses outranks a single "aha".
            int hardSteps = m.squeezeUses + m.subsetLineToRegionUses + m.subsetRegionToLineUses + m.fishUses + m.trials;
            if (hardSteps > 1) hardness += (int)Math.Round(3.0 * Math.Log(hardSteps));

            var rep = new Report
            {
                solved = solved,
                size = n,
                score = hardness,
                cycles = m.cycles,
                placements = m.placements,
                regionSingles = m.regionSingles,
                lineSingles = m.lineSingles,
                eliminations = m.eliminations,
                lineToRegionUses = m.lineToRegionUses,
                regionToLineUses = m.regionToLineUses,
                squeezeUses = m.squeezeUses,
                subsetLineToRegionUses = m.subsetLineToRegionUses,
                subsetRegionToLineUses = m.subsetRegionToLineUses,
                fishUses = m.fishUses,
                trials = m.trials,
                maxTrialDepth = m.maxTrialDepth,
            };
            rep.difficulty = Classify(hardness, solved);
            rep.technique = TechniqueName(m, solved);
            rep.estimatedSeconds = 8f + hardness * 1.2f + n * 1.5f;
            return rep;
        }

        // Tier by the (fatigue-adjusted) score. A guess is scored by the work it forces, so a cheap
        // guess that opens an easy board stays Medium; costly / nested / many-guess solves climb.
        static Difficulty Classify(int hardness, bool solved)
        {
            if (!solved) return Difficulty.Unrated;
            if (hardness >= 32) return Difficulty.Expert; // long chains / nested / many guesses
            if (hardness >= 8) return Difficulty.Hard;    // hardest deduction, or a costly guess
            if (hardness >= 2) return Difficulty.Medium;  // mid deduction, or a cheap guess
            if (hardness >= 1) return Difficulty.Easy;    // a line single
            return Difficulty.Kitten;                     // region singles only
        }

        static string TechniqueName(Metrics m, bool solved)
        {
            if (!solved) return "unsolved";
            if (m.trials > 0) return m.maxTrialDepth >= 2 ? "trial (nested)" : "trial";
            if (m.fishUses > 0) return "positional fish";
            if (m.subsetRegionToLineUses > 0) return "subset (region→line)";
            if (m.subsetLineToRegionUses > 0) return "subset (line→region)";
            if (m.squeezeUses > 0) return "squeeze";
            if (m.regionToLineUses > 0) return "region→line";
            if (m.lineToRegionUses > 0) return "line→region";
            if (m.lineSingles > 0) return "line single";
            return "region single";
        }

        // ---- negative solver (guided by the known unique solution) -------------------

        static (bool solved, int hardness) Solve(State state, int[] sol, Metrics m, Ctx ctx)
        {
            int hardness = 0;
            while (true)
            {
                if (ctx.aborted) return (true, hardness);

                var pr = Propagate(state, m, ctx);
                hardness = Math.Max(hardness, pr.w);
                if (pr.status == PStatus.Solved) return (true, hardness);
                if (pr.status == PStatus.Contradiction) return (false, hardness); // not expected on real path

                // stuck → cross out the wrong cell that is cheapest to disprove
                int row = state.MostConstrainedRow();
                if (row < 0) return (false, hardness);
                var cols = state.RowCandidateCols(row);
                int breadth = Math.Max(0, cols.Count - 2) * BreadthWeight; // a wider guess is harder
                int correctCol = sol[row];

                int best = -1, bestH = int.MaxValue;
                foreach (int col in cols)
                {
                    if (col == correctCol) continue; // never eliminate the real cell
                    var b = state.Clone();
                    var dm = new Metrics();
                    b.PlaceQueen(row, col, dm);
                    int h = ContradictionHardness(b, m, ctx, 1);
                    if (h < bestH) { bestH = h; best = col; }
                }
                if (best < 0) return (false, hardness);

                state.EliminateCell(row * state.n + best, m);
                m.trials++;
                hardness = Math.Max(hardness, bestH + breadth);
            }
        }

        // Hardness of proving an already-inconsistent state contradictory (cheapest path wins).
        static int ContradictionHardness(State s, Metrics m, Ctx ctx, int depth)
        {
            if (ctx.aborted) return 0;
            m.maxTrialDepth = Math.Max(m.maxTrialDepth, depth);

            var t = new Metrics();
            var pr = Propagate(s, t, ctx);
            // mental cost of following THIS hypothetical layer: how long the forced chain is,
            // with harder tricks counting more (you have no board to lean on)
            int chain = t.placements + t.lineToRegionUses + 2 * t.regionToLineUses + 4 * t.squeezeUses
                        + t.subsetWeight + t.fishWeight; // subset & fish already weighted as (Base + k)
            if (pr.status != PStatus.Stuck) return GuessBase + chain;       // reached the contradiction
            if (depth >= MaxProbe) return GuessBase + chain + 24;           // needs deeper nesting — cap (Expert)

            int row = s.MostConstrainedRow();
            if (row < 0) return GuessBase + chain;
            var cols = s.RowCandidateCols(row);
            int breadth = Math.Max(0, cols.Count - 2) * BreadthWeight; // wider nested guess is harder

            int best = int.MaxValue;
            foreach (int col in cols)
            {
                var b = s.Clone();
                var dm = new Metrics();
                b.PlaceQueen(row, col, dm);
                int h = ContradictionHardness(b, m, ctx, depth + 1);
                if (h < best) best = h;
            }
            return best == int.MaxValue ? GuessBase + chain : GuessBase + chain + breadth + Mult * best;
        }

        static PResult Propagate(State s, Metrics m, Ctx ctx)
        {
            int w = 0;
            while (true)
            {
                if (++ctx.steps > StepCap) { ctx.aborted = true; return new PResult { status = PStatus.Solved, w = w }; }
                m.cycles++;
                if (s.HasEmptyUnit()) return new PResult { status = PStatus.Contradiction, w = w };
                if (s.Solved) return new PResult { status = PStatus.Solved, w = w };

                int rs = s.FindRegionSingle();
                if (rs >= 0) { s.PlaceQueen(rs / s.n, rs % s.n, m); m.placements++; m.regionSingles++; continue; }

                int ls = s.FindLineSingle();
                if (ls >= 0) { s.PlaceQueen(ls / s.n, ls % s.n, m); m.placements++; m.lineSingles++; w = Math.Max(w, 1); continue; }

                int before = m.eliminations;
                s.LineToRegionEliminations(m); // a line is one colour (easy) — weight 1
                if (m.eliminations > before) { m.lineToRegionUses++; w = Math.Max(w, 1); continue; }

                before = m.eliminations;
                s.RegionToLineEliminations(m); // a region confined to a line — weight 2
                if (m.eliminations > before) { m.regionToLineUses++; w = Math.Max(w, 2); continue; }

                before = m.eliminations;
                s.SqueezeEliminations(m);
                if (m.eliminations > before) { m.squeezeUses++; w = Math.Max(w, 4); continue; }

                int kL = s.SubsetLineToRegionEliminations(m); // k lines hold only k colours — weight 3+k
                if (kL > 0) { m.subsetLineToRegionUses++; int wt = 3 + kL; m.subsetWeight += wt; w = Math.Max(w, wt); continue; }

                int kR = s.SubsetRegionToLineEliminations(m); // k regions fill k lines — weight 4+k
                if (kR > 0) { m.subsetRegionToLineUses++; int wt = 4 + kR; m.subsetWeight += wt; w = Math.Max(w, wt); continue; }

                int kF = s.FishEliminations(m); // k rows confined to k columns (positional) — weight 5+k
                if (kF > 0) { m.fishUses++; int wt = 5 + kF; m.fishWeight += wt; w = Math.Max(w, wt); continue; }

                return new PResult { status = PStatus.Stuck, w = w };
            }
        }

        struct PResult { public PStatus status; public int w; }

        class Ctx { public int steps; public bool aborted; }

        class Metrics
        {
            public int cycles, placements, regionSingles, lineSingles, eliminations,
                lineToRegionUses, regionToLineUses, squeezeUses, subsetLineToRegionUses, subsetRegionToLineUses,
                subsetWeight, fishUses, fishWeight, trials, maxTrialDepth;
        }

        // ---- board state -------------------------------------------------------------

        class State
        {
            public readonly int n;
            readonly int[] region;
            bool[] cand;
            bool[] queen;
            bool[] rowDone, colDone, regDone;
            int placedCount;
            readonly List<int> _scratch = new List<int>();

            public State(int n, int[] region)
            {
                this.n = n; this.region = region;
                cand = new bool[n * n];
                for (int i = 0; i < cand.Length; i++) cand[i] = true;
                queen = new bool[n * n];
                rowDone = new bool[n]; colDone = new bool[n]; regDone = new bool[n];
            }

            State(State o)
            {
                n = o.n; region = o.region;
                cand = (bool[])o.cand.Clone();
                queen = (bool[])o.queen.Clone();
                rowDone = (bool[])o.rowDone.Clone();
                colDone = (bool[])o.colDone.Clone();
                regDone = (bool[])o.regDone.Clone();
                placedCount = o.placedCount;
            }

            public State Clone() => new State(this);

            public bool Solved => placedCount == n;

            public void PlaceQueen(int r, int c, Metrics m)
            {
                int idx = r * n + c;
                if (queen[idx]) return;
                queen[idx] = true; cand[idx] = true; placedCount++;
                int g = region[idx];
                rowDone[r] = true; colDone[c] = true; regDone[g] = true;

                for (int cc = 0; cc < n; cc++) Kill(r * n + cc, idx, m);
                for (int rr = 0; rr < n; rr++) Kill(rr * n + c, idx, m);
                for (int i = 0; i < cand.Length; i++) if (region[i] == g) Kill(i, idx, m);
                for (int dr = -1; dr <= 1; dr++)
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        int nr = r + dr, nc = c + dc;
                        if (nr < 0 || nr >= n || nc < 0 || nc >= n) continue;
                        Kill(nr * n + nc, idx, m);
                    }
            }

            public void EliminateCell(int idx, Metrics m) => Kill(idx, -1, m);

            void Kill(int idx, int keep, Metrics m)
            {
                if (idx == keep) return;
                if (cand[idx] && !queen[idx]) { cand[idx] = false; m.eliminations++; }
            }

            public bool HasEmptyUnit()
            {
                for (int r = 0; r < n; r++) if (!rowDone[r] && RowCount(r) == 0) return true;
                for (int c = 0; c < n; c++) if (!colDone[c] && ColCount(c) == 0) return true;
                for (int g = 0; g < n; g++) if (!regDone[g] && RegCount(g) == 0) return true;
                return false;
            }

            public int FindRegionSingle()
            {
                for (int g = 0; g < n; g++) if (!regDone[g] && RegCount(g) == 1) return FirstInReg(g);
                return -1;
            }

            public int FindLineSingle()
            {
                for (int r = 0; r < n; r++) if (!rowDone[r] && RowCount(r) == 1) return FirstInRow(r);
                for (int c = 0; c < n; c++) if (!colDone[c] && ColCount(c) == 1) return FirstInCol(c);
                return -1;
            }

            public int MostConstrainedRow()
            {
                int bestR = -1, bestK = int.MaxValue;
                for (int r = 0; r < n; r++)
                {
                    if (rowDone[r]) continue;
                    int k = RowCount(r);
                    if (k > 1 && k < bestK) { bestK = k; bestR = r; }
                }
                return bestR;
            }

            public List<int> RowCandidateCols(int row)
            {
                var cols = new List<int>();
                for (int c = 0; c < n; c++) if (cand[row * n + c] && !queen[row * n + c]) cols.Add(c);
                return cols;
            }

            // a region's cells all sit in one row/column → no other region can use that line
            public void RegionToLineEliminations(Metrics m)
            {
                for (int g = 0; g < n; g++)
                {
                    if (regDone[g]) continue;
                    CollectRegion(g);
                    if (_scratch.Count == 0) continue;
                    if (AllSameRow(out int rr))
                        for (int c = 0; c < n; c++) { int i = rr * n + c; if (region[i] != g) Kill(i, -1, m); }
                    if (AllSameCol(out int cc))
                        for (int r = 0; r < n; r++) { int i = r * n + cc; if (region[i] != g) Kill(i, -1, m); }
                }
            }

            // a row/column is a single colour → that colour's queen is there → clear it elsewhere
            public void LineToRegionEliminations(Metrics m)
            {
                for (int r = 0; r < n; r++)
                {
                    if (rowDone[r]) continue;
                    CollectRow(r);
                    if (_scratch.Count > 0 && AllSameRegion(out int g))
                        for (int i = 0; i < cand.Length; i++) if (region[i] == g && i / n != r) Kill(i, -1, m);
                }
                for (int c = 0; c < n; c++)
                {
                    if (colDone[c]) continue;
                    CollectCol(c);
                    if (_scratch.Count > 0 && AllSameRegion(out int g))
                        for (int i = 0; i < cand.Length; i++) if (region[i] == g && i % n != c) Kill(i, -1, m);
                }
            }

            public void SqueezeEliminations(Metrics m)
            {
                for (int g = 0; g < n; g++)
                {
                    if (regDone[g]) continue;
                    CollectRegion(g);
                    if (_scratch.Count < 2) continue;
                    int[] x = _scratch.ToArray();

                    for (int idx = 0; idx < cand.Length; idx++)
                    {
                        if (!cand[idx] || queen[idx] || region[idx] == g) continue;
                        bool touchesAll = true;
                        for (int k = 0; k < x.Length; k++)
                            if (!Touch(idx, x[k])) { touchesAll = false; break; }
                        if (touchesAll) Kill(idx, -1, m);
                    }
                }
            }

            // Each returns the size k of the subset that fired (0 if none) so the weight scales with k.
            public int SubsetRegionToLineEliminations(Metrics m)
            {
                int k = GeneralRegionToLine(m, true);
                return k > 0 ? k : GeneralRegionToLine(m, false);
            }

            public int SubsetLineToRegionEliminations(Metrics m)
            {
                int k = GeneralLineToRegion(m, true);
                return k > 0 ? k : GeneralLineToRegion(m, false);
            }

            // positional fish (X-Wing/Swordfish): k rows confined to k columns → clear those columns
            // elsewhere. Colour-agnostic — distinct from the region subsets. Returns the size k.
            public int FishEliminations(Metrics m)
            {
                int k = GeneralFish(m, true);
                return k > 0 ? k : GeneralFish(m, false);
            }

            // k regions whose candidates span exactly k lines → those lines are theirs → clear other
            // regions from them. Tries k = 2,3,… and returns the first (smallest) size that fires.
            int GeneralRegionToLine(Metrics m, bool rows)
            {
                int[] mask = new int[n];
                var g = new List<int>();
                for (int reg = 0; reg < n; reg++)
                {
                    if (regDone[reg]) continue;
                    int mk = 0;
                    for (int idx = 0; idx < cand.Length; idx++)
                        if (region[idx] == reg && cand[idx] && !queen[idx]) mk |= 1 << (rows ? idx / n : idx % n);
                    if (mk != 0) { mask[reg] = mk; g.Add(reg); }
                }
                int gc = g.Count;
                var sel = new int[gc];
                for (int k = 2; k < gc; k++)
                {
                    for (int i = 0; i < k; i++) sel[i] = i;
                    do
                    {
                        int u = 0;
                        for (int i = 0; i < k; i++) u |= mask[g[sel[i]]];
                        if (PopCount(u) != k) continue;
                        int before = m.eliminations;
                        for (int idx = 0; idx < cand.Length; idx++)
                        {
                            if (!cand[idx] || queen[idx]) continue;
                            if ((u & (1 << (rows ? idx / n : idx % n))) == 0) continue;
                            bool inSet = false;
                            for (int i = 0; i < k; i++) if (region[idx] == g[sel[i]]) { inSet = true; break; }
                            if (!inSet) Kill(idx, -1, m);
                        }
                        if (m.eliminations > before) return k;
                    } while (NextCombo(sel, k, gc));
                }
                return 0;
            }

            // k lines whose candidates use exactly k regions → those regions' queens are in those
            // lines → clear those regions from every other line.
            int GeneralLineToRegion(Metrics m, bool rows)
            {
                int[] rmask = new int[n];
                var L = new List<int>();
                for (int line = 0; line < n; line++)
                {
                    if (rows ? rowDone[line] : colDone[line]) continue;
                    int mk = 0;
                    for (int idx = 0; idx < cand.Length; idx++)
                    {
                        if (!cand[idx] || queen[idx]) continue;
                        if ((rows ? idx / n : idx % n) != line) continue;
                        mk |= 1 << region[idx];
                    }
                    if (mk != 0) { rmask[line] = mk; L.Add(line); }
                }
                int lc = L.Count;
                var sel = new int[lc];
                for (int k = 2; k < lc; k++)
                {
                    for (int i = 0; i < k; i++) sel[i] = i;
                    do
                    {
                        int u = 0;
                        for (int i = 0; i < k; i++) u |= rmask[L[sel[i]]];
                        if (PopCount(u) != k) continue;
                        int before = m.eliminations;
                        for (int idx = 0; idx < cand.Length; idx++)
                        {
                            if (!cand[idx] || queen[idx]) continue;
                            if ((u & (1 << region[idx])) == 0) continue;
                            int line = rows ? idx / n : idx % n;
                            bool inLines = false;
                            for (int i = 0; i < k; i++) if (L[sel[i]] == line) { inLines = true; break; }
                            if (!inLines) Kill(idx, -1, m);
                        }
                        if (m.eliminations > before) return k;
                    } while (NextCombo(sel, k, lc));
                }
                return 0;
            }

            // k rows whose candidate columns span exactly k columns → those rows own those columns →
            // clear those columns in every other row. rows=false is the column↔row mirror.
            int GeneralFish(Metrics m, bool rows)
            {
                int[] mask = new int[n];
                var L = new List<int>();
                for (int line = 0; line < n; line++)
                {
                    if (rows ? rowDone[line] : colDone[line]) continue;
                    int mk = 0;
                    for (int p = 0; p < n; p++)
                    {
                        int idx = rows ? line * n + p : p * n + line;
                        if (cand[idx] && !queen[idx]) mk |= 1 << p;
                    }
                    if (mk != 0) { mask[line] = mk; L.Add(line); }
                }
                int lc = L.Count;
                var sel = new int[lc];
                for (int k = 2; k < lc; k++)
                {
                    for (int i = 0; i < k; i++) sel[i] = i;
                    do
                    {
                        int u = 0;
                        for (int i = 0; i < k; i++) u |= mask[L[sel[i]]];
                        if (PopCount(u) != k) continue;
                        int before = m.eliminations;
                        for (int line = 0; line < n; line++)
                        {
                            bool inSet = false;
                            for (int i = 0; i < k; i++) if (L[sel[i]] == line) { inSet = true; break; }
                            if (inSet) continue;
                            for (int p = 0; p < n; p++)
                            {
                                if ((u & (1 << p)) == 0) continue;
                                int idx = rows ? line * n + p : p * n + line;
                                if (cand[idx] && !queen[idx]) Kill(idx, -1, m);
                            }
                        }
                        if (m.eliminations > before) return k;
                    } while (NextCombo(sel, k, lc));
                }
                return 0;
            }

            // advances sel[] to the next k-combination of [0..total); false when exhausted
            static bool NextCombo(int[] sel, int k, int total)
            {
                int p = k - 1;
                while (p >= 0 && sel[p] == total - k + p) p--;
                if (p < 0) return false;
                sel[p]++;
                for (int i = p + 1; i < k; i++) sel[i] = sel[i - 1] + 1;
                return true;
            }

            bool Touch(int a, int b)
            {
                int dr = a / n - b / n; if (dr < 0) dr = -dr;
                int dc = a % n - b % n; if (dc < 0) dc = -dc;
                return Math.Max(dr, dc) == 1;
            }

            static int PopCount(int x) { int c = 0; while (x != 0) { x &= x - 1; c++; } return c; }

            int RowCount(int r) { int k = 0; for (int c = 0; c < n; c++) if (cand[r * n + c] && !queen[r * n + c]) k++; return k; }
            int ColCount(int c) { int k = 0; for (int r = 0; r < n; r++) if (cand[r * n + c] && !queen[r * n + c]) k++; return k; }
            int RegCount(int g) { int k = 0; for (int i = 0; i < cand.Length; i++) if (region[i] == g && cand[i] && !queen[i]) k++; return k; }

            int FirstInRow(int r) { for (int c = 0; c < n; c++) if (cand[r * n + c] && !queen[r * n + c]) return r * n + c; return -1; }
            int FirstInCol(int c) { for (int r = 0; r < n; r++) if (cand[r * n + c] && !queen[r * n + c]) return r * n + c; return -1; }
            int FirstInReg(int g) { for (int i = 0; i < cand.Length; i++) if (region[i] == g && cand[i] && !queen[i]) return i; return -1; }

            void CollectRegion(int g) { _scratch.Clear(); for (int i = 0; i < cand.Length; i++) if (region[i] == g && cand[i] && !queen[i]) _scratch.Add(i); }
            void CollectRow(int r) { _scratch.Clear(); for (int c = 0; c < n; c++) { int i = r * n + c; if (cand[i] && !queen[i]) _scratch.Add(i); } }
            void CollectCol(int c) { _scratch.Clear(); for (int r = 0; r < n; r++) { int i = r * n + c; if (cand[i] && !queen[i]) _scratch.Add(i); } }

            bool AllSameRow(out int row) { row = _scratch[0] / n; foreach (int i in _scratch) if (i / n != row) return false; return true; }
            bool AllSameCol(out int col) { col = _scratch[0] % n; foreach (int i in _scratch) if (i % n != col) return false; return true; }
            bool AllSameRegion(out int g) { g = region[_scratch[0]]; foreach (int i in _scratch) if (region[i] != g) return false; return true; }
        }
    }
}
