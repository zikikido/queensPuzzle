namespace QueensPuzzle
{
    /// <summary>
    /// Build-time accept/reject rules as plain data — what the pool builder consumes, blind to
    /// which curve produced the targets. Assembled on demand from <see cref="CampaignCurveConfig.Gates"/>;
    /// never serialized on its own, so the curve asset stays the single source of truth.
    /// </summary>
    public struct GenerationGates
    {
        public int maxTrials;
        public float maxPeakShare;
        public float minWeightPerStep;
        public int maxPaidSteps;
        public int milestoneMaxPaidSteps;
        public float maxStartShare;
        public float peakMaxStartShare;
        public int gateMinWeight;
    }
}
