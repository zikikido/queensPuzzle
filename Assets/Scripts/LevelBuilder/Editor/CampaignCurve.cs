namespace QueensPuzzle
{
    /// <summary>
    /// Build-time accept/reject for generated levels against <see cref="GenerationGates"/>.
    /// All tunables live on the curve config asset (exposed as its Gates property); this only
    /// wires them to <see cref="WeightRater"/> reports (editor-only, hence not on the config class).
    /// </summary>
    public static class CampaignCurve
    {
        /// <summary>Accept/reject one rated candidate for a slot. Board size must match separately.</summary>
        public static bool PassesGates(in GenerationGates c, in CampaignCurveConfig.LevelTarget t, in WeightRater.Report rep)
        {
            if (!rep.solved || rep.trials > c.maxTrials) return false;
            if (rep.weight < t.minWeight || rep.weight > t.maxWeight) return false;
            bool milestone = t.role == CampaignCurveConfig.Role.Milestone;
            if (rep.paidSteps > (milestone ? c.milestoneMaxPaidSteps : c.maxPaidSteps)) return false;
            if (rep.weight >= c.gateMinWeight && rep.paidSteps > 0)
            {
                if (rep.peak / (float)rep.weight > c.maxPeakShare) return false;            // wall
                if (rep.weight / (float)rep.paidSteps < c.minWeightPerStep) return false;   // grind
                // light levels skip the start gate too — with few steps the first third
                // always holds most of the weight, and that's fine on an easy board
                bool hardSlot = milestone || t.role == CampaignCurveConfig.Role.Peak;
                if (rep.startShare > (hardSlot ? c.peakMaxStartShare : c.maxStartShare)) return false;
            }
            return true;
        }

        /// <summary>A clean over-cap level worth hoarding for the milestone pool.</summary>
        public static bool IsMilestonePoolCandidate(CampaignCurveConfig c, in WeightRater.Report rep) =>
            rep.solved && rep.trials == 0 && rep.weight >= c.milestonePoolMin && rep.weight <= c.milestonePoolMax;
    }
}
