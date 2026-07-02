using System;

namespace QueensPuzzle
{
    /// <summary>
    /// Rates a level by solving it the way a human would and summing the effort of every step —
    /// a long solve on a big board weighs more even when each move is easy.
    ///
    /// Every deduction costs   Find · open / Anchor  +  weight :
    ///   the find part is scanning a cluttered board for the next move (scales with how many
    ///   cells are still open); the weight is the thinking cost of the trick itself (tenths):
    ///     region single      0    line single     10    one-colour line 10
    ///     trapped region    20    squeeze         40    subset L→R 30+10k
    ///     subset R→L   40+10k    region choke    70    fish        50+10k
    ///   Queen shadow is free — the eyes are already on the queen that was just placed.
    ///
    /// A stuck board costs a guess: GuessSetup + the chain of forced moves to the cheapest
    /// contradiction (pencil profile — the chain is board work, not head work). Nesting
    /// multiplies deeper cost by Nest per level and hits a flat wall at MaxProbe.
    /// The known unique solution guides which wrong cell to disprove (luck-free, cheapest first).
    ///
    /// The total is the level's WEIGHT (int) — how heavy it is to solve. ("Score" is reserved
    /// for the player's in-game result.) Rough feel: &lt;10 trivial, ~40 easy, ~100 medium,
    /// ~300 hard, 400+ brutal. Full model documented in docs/weight-model.svg.
    ///
    /// Pure C# (no Unity types) so it is unit-testable and runs at build time.
    /// </summary>
    public static class WeightRater
    {
        const int Find = 3;            // scan cost of any non-shadow move, in tenths
        const int Anchor = 25;         // open-cell count where the board feels "neutral"
        const int GuessSetup = 30;     // flat cost of deciding to try a what-if
        const int Nest = 3;            // multiplier per nesting level past the first
        const int MaxProbe = 3;        // beyond this a stuck branch pays a flat wall
        const int DeepWall = 300;
        const int StepCap = 1_500_000; // safety net against pathological recursion

        public struct Report
        {
            public bool solved;
            public string technique;
            public int weight;           // sum of step costs (integer, tenths scale)
            public int size;
            public int cycles;
            public int placements;
            public int regionSingles;
            public int lineSingles;
            public int eliminations;
            public int lineToRegionUses;
            public int regionToLineUses;
            public int squeezeUses;
            public int regionChokeUses;
            public int subsetLineToRegionUses;
            public int subsetRegionToLineUses;
            public int fishUses;
            public int trials;
            public int maxTrialDepth;
            public float estimatedSeconds;
            public int findCost;         // total scanning cost (the 3·open/25 parts)
            public int thinkCost;        // total trick-weight cost
            public int guessCost;        // total cost of all guesses (setup + chains)
            public int[] techCost;       // cost per SolveTechnique (find + think of its steps)
            public int[] techUses;       // uses per SolveTechnique
        }

        public static Report Rate(int n, int[] region, int[] solution)
        {
            var m = new Metrics();
            var ctx = new Ctx();
            var s = new SolveTracer.Board(n, region);
            int weight = 0;
            bool solved = true;

            while (!s.Solved)
            {
                if (ctx.aborted) break;
                if (s.HasEmptyUnit()) { solved = false; break; } // not expected on the real path
                m.cycles++;

                int open = s.OpenCells();
                if (s.NextStep(out var step)) { weight += Tally(step, open, m); continue; }

                // stuck → guess: X the wrong cell that is cheapest to disprove
                int row = s.MostConstrainedRow();
                if (row < 0) { solved = false; break; }
                int correctCol = solution[row];
                int best = -1, bestCost = int.MaxValue;
                foreach (int col in s.RowCands(row))
                {
                    if (col == correctCol) continue; // never eliminate the real cell
                    var b = s.Clone();
                    b.Place(row, col);
                    int cost = GuessCost(b, m, ctx, 1);
                    if (cost < bestCost) { bestCost = cost; best = col; }
                }
                if (best < 0) { solved = false; break; }
                m.trials++;
                m.guessCost += bestCost;
                weight += bestCost;
                s.KnownX(row * n + best);
            }

            if (ctx.aborted) { solved = true; weight = Math.Max(weight, 500); }

            var rep = new Report
            {
                solved = solved && s.Solved,
                size = n,
                weight = weight,
                cycles = m.cycles,
                placements = m.placements,
                regionSingles = m.regionSingles,
                lineSingles = m.lineSingles,
                eliminations = m.eliminations,
                lineToRegionUses = m.lineToRegionUses,
                regionToLineUses = m.regionToLineUses,
                squeezeUses = m.squeezeUses,
                regionChokeUses = m.regionChokeUses,
                subsetLineToRegionUses = m.subsetLineToRegionUses,
                subsetRegionToLineUses = m.subsetRegionToLineUses,
                fishUses = m.fishUses,
                trials = m.trials,
                maxTrialDepth = m.maxTrialDepth,
                findCost = m.findCost,
                thinkCost = m.thinkCost,
                guessCost = m.guessCost,
                techCost = m.techCost,
                techUses = m.techUses,
            };
            rep.technique = TechniqueName(m, rep.solved);
            rep.estimatedSeconds = 10f + rep.weight; // one weight point ≈ one second of solving
            return rep;
        }

        static string TechniqueName(Metrics m, bool solved)
        {
            if (!solved) return "unsolved";
            if (m.trials > 0) return m.maxTrialDepth >= 2 ? "trial (nested)" : "trial";
            if (m.fishUses > 0) return "positional fish";
            if (m.regionChokeUses > 0) return "region choke";
            if (m.subsetRegionToLineUses > 0) return "subset (region→line)";
            if (m.subsetLineToRegionUses > 0) return "subset (line→region)";
            if (m.squeezeUses > 0) return "squeeze";
            if (m.regionToLineUses > 0) return "region→line";
            if (m.lineToRegionUses > 0) return "line→region";
            if (m.lineSingles > 0) return "line single";
            return "region single";
        }

        // Cost of one deduction step found on a board with `open` candidate cells, plus metrics.
        static int Tally(SolveTracer.Board.DeductionStep step, int open, Metrics m)
        {
            m.techUses[(int)step.tech]++;
            int weight;
            switch (step.tech)
            {
                case SolveTechnique.QueenScope:         m.eliminations += step.cells.Length; return 0; // free — no scan
                case SolveTechnique.RegionSingle:       m.placements++; m.regionSingles++;   weight = 0;  break;
                case SolveTechnique.LineSingle:         m.placements++; m.lineSingles++;     weight = 10; break;
                case SolveTechnique.LineToRegion:       m.lineToRegionUses++;       m.eliminations += step.cells.Length; weight = 10; break;
                case SolveTechnique.RegionToLine:       m.regionToLineUses++;       m.eliminations += step.cells.Length; weight = 20; break;
                case SolveTechnique.Squeeze:            m.squeezeUses++;            m.eliminations += step.cells.Length; weight = 40; break;
                case SolveTechnique.RegionChoke:        m.regionChokeUses++;        m.eliminations += step.cells.Length; weight = 70; break;
                case SolveTechnique.SubsetLineToRegion: m.subsetLineToRegionUses++; m.eliminations += step.cells.Length; weight = 30 + 10 * step.k; break;
                case SolveTechnique.SubsetRegionToLine: m.subsetRegionToLineUses++; m.eliminations += step.cells.Length; weight = 40 + 10 * step.k; break;
                case SolveTechnique.Fish:               m.fishUses++;               m.eliminations += step.cells.Length; weight = 50 + 10 * step.k; break;
                default: weight = 0; break;
            }
            int find = Find * open / Anchor;
            m.findCost += find;
            m.thinkCost += weight;
            m.techCost[(int)step.tech] += find + weight;
            return find + weight;
        }

        // Cost of following one wrong hypothesis to its contradiction (cheapest path wins).
        // Chain work is tallied into a throwaway Metrics — hypothetical moves aren't solve steps.
        static int GuessCost(SolveTracer.Board s, Metrics m, Ctx ctx, int depth)
        {
            if (ctx.aborted) return 0;
            m.maxTrialDepth = Math.Max(m.maxTrialDepth, depth);

            var t = new Metrics();
            int chain = 0;
            while (true)
            {
                if (++ctx.steps > StepCap) { ctx.aborted = true; return GuessSetup + chain; }
                if (s.HasEmptyUnit()) return GuessSetup + chain;   // contradiction reached
                if (s.Solved) return GuessSetup + chain;           // cannot happen on a wrong branch
                int open = s.OpenCells();
                if (!s.NextStep(out var step)) break;              // stuck → must nest deeper
                chain += Tally(step, open, t);
            }

            if (depth >= MaxProbe) return GuessSetup + chain + DeepWall;

            int row = s.MostConstrainedRow();
            if (row < 0) return GuessSetup + chain;
            int best = int.MaxValue;
            foreach (int col in s.RowCands(row))
            {
                var b = s.Clone();
                b.Place(row, col);
                int cost = GuessCost(b, m, ctx, depth + 1);
                if (cost < best) best = cost;
            }
            return best == int.MaxValue ? GuessSetup + chain : GuessSetup + chain + Nest * best;
        }

        class Ctx { public int steps; public bool aborted; }

        class Metrics
        {
            public int cycles, placements, regionSingles, lineSingles, eliminations,
                lineToRegionUses, regionToLineUses, squeezeUses, regionChokeUses,
                subsetLineToRegionUses, subsetRegionToLineUses,
                fishUses, trials, maxTrialDepth,
                findCost, thinkCost, guessCost;
            public int[] techCost = new int[16];
            public int[] techUses = new int[16];
        }
    }
}
