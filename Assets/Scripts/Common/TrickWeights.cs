namespace QueensPuzzle
{
    /// <summary>
    /// The cost model shared by the weight rater and the solve trace: every step costs
    /// find (scanning a cluttered board — scales with open cells) + think (the trick's
    /// weight, in tenths). Queen shadow is free. Single source of truth for the table
    /// in docs/weight-model.svg.
    /// </summary>
    public static class TrickWeights
    {
        public const int Find = 3;    // scan cost of any non-shadow move, in tenths
        public const int Anchor = 25; // open-cell count where the board feels "neutral"

        /// <summary>Thinking weight of one deduction (tenths). k = subset/fish size.</summary>
        public static int Of(SolveTechnique t, int k)
        {
            switch (t)
            {
                case SolveTechnique.LineSingle: return 10;
                case SolveTechnique.LineToRegion: return 10;
                case SolveTechnique.RegionToLine: return 20;
                case SolveTechnique.Squeeze: return 40;
                case SolveTechnique.SubsetLineToRegion: return 30 + 10 * k;
                case SolveTechnique.SubsetRegionToLine: return 40 + 10 * k;
                case SolveTechnique.RegionChoke: return 70;
                case SolveTechnique.Fish: return 50 + 10 * k;
                default: return 0; // QueenScope / RegionSingle think for free
            }
        }

        /// <summary>
        /// Full cost of one step found on a board with <paramref name="open"/> candidate cells.
        /// A streak (same trick as the previous paid step — the pattern is still loaded in the
        /// player's head) halves the think part; the find part always applies.
        /// </summary>
        public static int StepCost(SolveTechnique t, int k, int open, bool streak = false)
        {
            if (t == SolveTechnique.QueenScope) return 0;
            int think = Of(t, k);
            if (streak) think /= 2;
            return Find * open / Anchor + think;
        }

        /// <summary>True if this technique can form / continue a streak (has a think cost at all).</summary>
        public static bool Streakable(SolveTechnique t) => Of(t, 2) > 0;
    }
}
