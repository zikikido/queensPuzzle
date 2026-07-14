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
    ///     subset R→L   40+10k    region choke 80+10k   fish        50+10k
    ///     line choke 80+10k′
    ///   (choke's k = candidate cells of the starved region — every one must be checked;
    ///    line choke: try each of a small unit's k′ cells — if the try's SHADOW ALONE
    ///    starves another unit, the cell is X. One look deep; any what-if that needs
    ///    forced moves after the try is a GUESS, not a trick)
    ///   Queen shadow is free — the eyes are already on the queen that was just placed.
    ///   Streak: the same trick again, right after (no other paid step between) → think cost
    ///   halved — the pattern is still loaded in the player's head. A guess breaks the streak;
    ///   chokes streak weakly (3/4, not 1/2 — each one is still a fresh what-if).
    ///
    /// A stuck board costs a guess: GuessSetup + the chain of forced moves to the cheapest
    /// contradiction (pencil profile — the chain is board work, not head work). Nesting
    /// multiplies deeper cost by Nest per level and hits a flat wall at MaxProbe.
    /// The known unique solution guides which wrong cell to disprove (luck-free, cheapest first).
    ///
    /// The total is the level's WEIGHT (int) — how heavy it is to solve.
    /// Rough feel: &lt;10 trivial, ~40 easy, ~100 medium, ~300 hard, 400+ brutal.
    /// Full model documented in docs/weight-model.svg.
    ///
    /// Pure C# (no Unity types) so it is unit-testable and runs at build time.
    /// </summary>
    public static class WeightRater
    {
        const int GuessSetup = 200;   // flat cost of a guess — always above every trick (the worst move in the game)
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
            public int lineChokeUses;
            public int trials;
            public int maxTrialDepth;
            public int findCost;         // total scanning cost (the 3·open/25 parts)
            public int thinkCost;        // total trick-weight cost
            public int guessCost;        // total cost of all guesses (setup + chains)
            public int peak;             // cost of the single most expensive step (the "wall")
            public float evenness;       // 0..1 — 1 = every step costs the same (grind), →0 = one step holds it all (peak)
            public float startShare;     // 0..1 — share of the weight paid in the FIRST THIRD of the solve (high = starts hard)
            public int paidSteps;        // steps that cost anything (queen shadows and free endgame moves excluded)
            public int[] techCost;       // cost per SolveTechnique (find + think of its steps)
            public int[] techUses;       // uses per SolveTechnique
            public int[] guessCosts;     // one entry per stuck point (consecutive guesses merge into one run)
        }

        public static Report Rate(int n, int[] region, int[] solution)
        {
            var m = new Metrics();
            var ctx = new Ctx();
            var s = new SolveTracer.Board(n, region);
            int weight = 0;
            bool solved = true;
            var prevTech = SolveTechnique.None; // last paid trick — a repeat halves the think cost

            // Guess costs grouped per stuck point: a row with k options costs k-1 consecutive
            // guesses — one run, shown as one number on the trace's TrialRoot row.
            var guessRuns = new System.Collections.Generic.List<int>();
            bool prevWasGuess = false;

            while (!s.Solved)
            {
                if (ctx.aborted) break;
                if (s.HasEmptyUnit()) { solved = false; break; } // not expected on the real path
                m.cycles++;

                int open = s.OpenCells();
                if (s.NextStep(out var step))
                {
                    bool streak = step.tech == prevTech && TrickWeights.Streakable(step.tech);
                    if (TrickWeights.Streakable(step.tech)) prevTech = step.tech;
                    int cost = Tally(step, open, streak, m);
                    if (cost > 0) m.costs.Add(cost);
                    weight += cost;
                    prevWasGuess = false;   // a deduction resumed — the guess run (if any) is over
                    continue;
                }
                prevTech = SolveTechnique.None; // a guess is a context switch — streak broken

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
                if (bestCost > 0) m.costs.Add(bestCost);
                weight += bestCost;
                if (prevWasGuess && guessRuns.Count > 0) guessRuns[guessRuns.Count - 1] += bestCost;
                else guessRuns.Add(bestCost);
                prevWasGuess = true;
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
                lineChokeUses = m.lineChokeUses,
                trials = m.trials,
                maxTrialDepth = m.maxTrialDepth,
                findCost = m.findCost,
                thinkCost = m.thinkCost,
                guessCost = m.guessCost,
                techCost = m.techCost,
                techUses = m.techUses,
                guessCosts = guessRuns.ToArray(),
            };
            rep.technique = TechniqueName(m, rep.solved);
            rep.paidSteps = m.costs.Count;
            foreach (int c in m.costs) rep.peak = Math.Max(rep.peak, c);
            rep.startShare = StartShare(m.costs, weight);   // before Evenness — it sorts the list
            rep.evenness = Evenness(m.costs);
            return rep;
        }

        // Share of the weight paid in the first third of the solve's paid steps — high means the
        // level opens with its hardest thinking (bad shape: players quit on a cold wall).
        static float StartShare(System.Collections.Generic.List<int> costs, int total)
        {
            if (costs.Count == 0 || total <= 0) return 0f;
            int third = (costs.Count + 2) / 3, sum = 0;
            for (int i = 0; i < third; i++) sum += costs[i];
            return (float)sum / total;
        }

        // 1 − Gini of the step costs: 1 = all steps cost the same, →0 = one step carries everything.
        static float Evenness(System.Collections.Generic.List<int> costs)
        {
            if (costs.Count < 2) return 1f;
            costs.Sort();
            double sum = 0, weighted = 0;
            for (int i = 0; i < costs.Count; i++) { sum += costs[i]; weighted += (i + 1.0) * costs[i]; }
            if (sum <= 0) return 1f;
            double gini = 2.0 * weighted / (costs.Count * sum) - (costs.Count + 1.0) / costs.Count;
            return (float)(1.0 - gini);
        }

        static string TechniqueName(Metrics m, bool solved)
        {
            if (!solved) return "unsolved";
            if (m.trials > 0) return m.maxTrialDepth >= 2 ? "trial (nested)" : "trial";
            if (m.lineChokeUses > 0) return "line choke";
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
        // streak = same trick as the previous paid step → think cost halved (pattern still loaded).
        static int Tally(SolveTracer.Board.DeductionStep step, int open, bool streak, Metrics m)
        {
            m.techUses[(int)step.tech]++;
            switch (step.tech)
            {
                case SolveTechnique.QueenScope:         m.eliminations += step.cells.Length; return 0; // free — no scan
                case SolveTechnique.RegionSingle:       m.placements++; m.regionSingles++;   break;
                case SolveTechnique.LineSingle:         m.placements++; m.lineSingles++;     break;
                case SolveTechnique.LineToRegion:       m.lineToRegionUses++;       m.eliminations += step.cells.Length; break;
                case SolveTechnique.RegionToLine:       m.regionToLineUses++;       m.eliminations += step.cells.Length; break;
                case SolveTechnique.Squeeze:            m.squeezeUses++;            m.eliminations += step.cells.Length; break;
                case SolveTechnique.RegionChoke:        m.regionChokeUses++;        m.eliminations += step.cells.Length; break;
                case SolveTechnique.SubsetLineToRegion: m.subsetLineToRegionUses++; m.eliminations += step.cells.Length; break;
                case SolveTechnique.SubsetRegionToLine: m.subsetRegionToLineUses++; m.eliminations += step.cells.Length; break;
                case SolveTechnique.Fish:               m.fishUses++;               m.eliminations += step.cells.Length; break;
                case SolveTechnique.LineChoke:         m.lineChokeUses++;         m.eliminations += step.cells.Length; break;
            }
            int find = TrickWeights.Find * open / TrickWeights.Anchor;
            int think = streak ? TrickWeights.StreakThink(step.tech, step.k) : TrickWeights.Of(step.tech, step.k);
            m.findCost += find;
            m.thinkCost += think;
            m.techCost[(int)step.tech] += find + think;
            return find + think;
        }

        // Cost of following one wrong hypothesis to its contradiction (cheapest path wins).
        // Chain work is tallied into a throwaway Metrics — hypothetical moves aren't solve steps.
        static int GuessCost(SolveTracer.Board s, Metrics m, Ctx ctx, int depth)
        {
            if (ctx.aborted) return 0;
            m.maxTrialDepth = Math.Max(m.maxTrialDepth, depth);

            var t = new Metrics();
            int chain = 0;
            var prevTech = SolveTechnique.None;
            while (true)
            {
                if (++ctx.steps > StepCap) { ctx.aborted = true; return GuessSetup + chain; }
                if (s.HasEmptyUnit()) return GuessSetup + chain;   // contradiction reached
                if (s.Solved) return GuessSetup + chain;           // cannot happen on a wrong branch
                int open = s.OpenCells();
                if (!s.NextStep(out var step)) break;              // stuck → must nest deeper
                bool streak = step.tech == prevTech && TrickWeights.Streakable(step.tech);
                if (TrickWeights.Streakable(step.tech)) prevTech = step.tech;
                chain += Tally(step, open, streak, t);
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
                fishUses, lineChokeUses, trials, maxTrialDepth,
                findCost, thinkCost, guessCost;
            public int[] techCost = new int[16];
            public int[] techUses = new int[16];
            public System.Collections.Generic.List<int> costs = new System.Collections.Generic.List<int>();
        }
    }
}
