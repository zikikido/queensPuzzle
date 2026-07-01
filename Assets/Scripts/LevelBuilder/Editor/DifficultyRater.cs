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
            var s = new SolveTracer.Board(n, region);

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

        static (bool solved, int hardness) Solve(SolveTracer.Board state, int[] sol, Metrics m, Ctx ctx)
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
                var cols = state.RowCands(row);
                int breadth = Math.Max(0, cols.Count - 2) * BreadthWeight; // a wider guess is harder
                int correctCol = sol[row];

                int best = -1, bestH = int.MaxValue;
                foreach (int col in cols)
                {
                    if (col == correctCol) continue; // never eliminate the real cell
                    var b = state.Clone();
                    b.Place(row, col);
                    int h = ContradictionHardness(b, m, ctx, 1);
                    if (h < bestH) { bestH = h; best = col; }
                }
                if (best < 0) return (false, hardness);

                state.KnownX(row * state.N + best);
                m.trials++;
                hardness = Math.Max(hardness, bestH + breadth);
            }
        }

        // Hardness of proving an already-inconsistent state contradictory (cheapest path wins).
        static int ContradictionHardness(SolveTracer.Board s, Metrics m, Ctx ctx, int depth)
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
            var cols = s.RowCands(row);
            int breadth = Math.Max(0, cols.Count - 2) * BreadthWeight; // wider nested guess is harder

            int best = int.MaxValue;
            foreach (int col in cols)
            {
                var b = s.Clone();
                b.Place(row, col);
                int h = ContradictionHardness(b, m, ctx, depth + 1);
                if (h < best) best = h;
            }
            return best == int.MaxValue ? GuessBase + chain : GuessBase + chain + breadth + Mult * best;
        }

        static PResult Propagate(SolveTracer.Board s, Metrics m, Ctx ctx)
        {
            int w = 0;
            while (true)
            {
                if (++ctx.steps > StepCap) { ctx.aborted = true; return new PResult { status = PStatus.Solved, w = w }; }
                m.cycles++;
                if (s.HasEmptyUnit()) return new PResult { status = PStatus.Contradiction, w = w };
                if (s.Solved) return new PResult { status = PStatus.Solved, w = w };

                if (!s.NextStep(out var step)) return new PResult { status = PStatus.Stuck, w = w };

                switch (step.tech)
                {
                    case SolveTechnique.QueenScope:                            m.eliminations += step.cells.Length; break;
                    case SolveTechnique.RegionSingle:  m.placements++; m.regionSingles++;                             break;
                    case SolveTechnique.LineSingle:    m.placements++; m.lineSingles++;   w = Math.Max(w, 1);         break;
                    case SolveTechnique.LineToRegion:  m.lineToRegionUses++; m.eliminations += step.cells.Length; w = Math.Max(w, 1); break;
                    case SolveTechnique.RegionToLine:  m.regionToLineUses++; m.eliminations += step.cells.Length; w = Math.Max(w, 2); break;
                    case SolveTechnique.Squeeze:       m.squeezeUses++;      m.eliminations += step.cells.Length; w = Math.Max(w, 4); break;
                    case SolveTechnique.SubsetLineToRegion: { int wt = 3 + step.k; m.subsetLineToRegionUses++; m.subsetWeight += wt; m.eliminations += step.cells.Length; w = Math.Max(w, wt); break; }
                    case SolveTechnique.SubsetRegionToLine: { int wt = 4 + step.k; m.subsetRegionToLineUses++; m.subsetWeight += wt; m.eliminations += step.cells.Length; w = Math.Max(w, wt); break; }
                    case SolveTechnique.Fish:               { int wt = 5 + step.k; m.fishUses++;               m.fishWeight   += wt; m.eliminations += step.cells.Length; w = Math.Max(w, wt); break; }
                }
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

    }
}
