using System;
using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// The 10K-campaign difficulty curve as an EDITOR authoring asset — every tunable of the
    /// progression design in one place, editable in the inspector without touching code.
    ///
    /// Scale: difficulty 0..1 where 1.0 = <see cref="weightCap"/> — the hardest puzzle solvable
    /// by pure deduction. Levels requiring guessing are always rejected at build time.
    ///
    ///   L1..learnEnd   learning ramp: median learnStart → learnEndMedian, decade rhythm on top,
    ///                  milestones capped to the ramp (learnMilestones).
    ///   after          wave templates (one peak, breather right after) + hot/cool macro cycles
    ///                  + a milestone every milestoneEvery levels.
    ///
    /// Breathers (≤ breatherCeil) are protected: no hot multiplier, no jitter — the flat floor
    /// is the fun engine. All randomness is a hash of the level number, so builds reproduce.
    ///
    /// <see cref="GetTarget"/> turns a level number into board size + weight window + role.
    /// </summary>
    [CreateAssetMenu(fileName = "CampaignCurve", menuName = "QueensPuzzle/Campaign Curve", order = 1)]
    public class CampaignCurveConfig : ScriptableObject
    {
        public enum Role { Breather, Normal, Build, Peak, Milestone }

        public struct LevelTarget
        {
            public int level;
            public int boardSize;
            public float difficulty;    // 0..1 target after macro cycle + jitter
            public int targetWeight;    // difficulty * weightCap
            public int minWeight;       // accept window (target ± matchTol, milestones wider)
            public int maxWeight;
            public Role role;
        }

        [Serializable]
        public class WaveTemplate
        {
            [Tooltip("Difficulty 0..1 per level of the wave. Exactly one peak, a breather right after it.")]
            public float[] values;
        }

        [Header("Scale")]
        [Tooltip("Weight that equals difficulty 1.0 — the hardest clean no-guess puzzle.")]
        public int weightCap = 600;

        [Header("Learning phase (levels 1..learnEnd)")]
        public int learnEnd = 200;
        [Tooltip("Difficulty at level 1.")]
        public float learnStart = 0.05f;
        [Tooltip("Median difficulty reached at learnEnd.")]
        public float learnEndMedian = 0.25f;
        [Tooltip("Hard cap for any non-milestone level during learning.")]
        public float learnMax = 0.60f;
        [Tooltip("Ramp shape: < 1 = slightly front-loaded.")]
        public float learnExponent = 0.9f;
        [Tooltip("Decade rhythm inside the ramp, slots X1..X0.")]
        public float[] learnSlotMult = { 0.7f, 1.0f, 1.25f, 0.9f, 1.45f, 0.8f, 1.6f, 1.05f, 1.9f, 2.4f };
        [Tooltip("Learning milestones (L50/100/150/200 with the defaults) — capped to the ramp.")]
        public float[] learnMilestones = { 0.30f, 0.45f, 0.55f, 0.75f };
        [Tooltip("Board sizes of the onboarding levels (mirrors MSet L1–14).")]
        public int[] teachSizes = { 4, 4, 6, 6, 8, 6, 6, 8, 8, 7, 6, 6, 8, 8 };

        [Header("Wave phase (after learnEnd)")]
        [Tooltip("Picked per wave by level hash, never the same template twice in a row.")]
        public WaveTemplate[] waveTemplates =
        {
            new WaveTemplate { values = new[] { 0.12f, 0.20f, 0.25f, 0.30f, 0.38f, 0.15f, 0.25f, 0.33f, 0.42f, 0.50f,
                                                0.13f, 0.28f, 0.38f, 0.50f, 0.62f, 0.12f, 0.22f, 0.35f, 0.45f, 0.30f } },
            new WaveTemplate { values = new[] { 0.35f, 0.45f, 0.14f, 0.25f, 0.35f, 0.28f, 0.40f, 0.13f, 0.30f, 0.42f,
                                                0.22f, 0.35f, 0.48f, 0.15f, 0.30f, 0.42f, 0.52f, 0.62f, 0.13f, 0.25f } },
            new WaveTemplate { values = new[] { 0.13f, 0.18f, 0.22f, 0.27f, 0.32f, 0.36f, 0.14f, 0.30f, 0.40f, 0.46f,
                                                0.52f, 0.62f, 0.14f, 0.28f, 0.35f, 0.25f, 0.40f, 0.30f, 0.20f, 0.35f } },
            new WaveTemplate { values = new[] { 0.14f, 0.28f, 0.42f, 0.50f, 0.15f, 0.30f, 0.45f, 0.55f, 0.62f, 0.13f,
                                                0.25f, 0.38f, 0.30f, 0.45f, 0.35f, 0.15f, 0.30f, 0.45f, 0.35f, 0.25f } },
            new WaveTemplate { values = new[] { 0.15f, 0.35f, 0.20f, 0.42f, 0.25f, 0.48f, 0.14f, 0.38f, 0.28f, 0.50f,
                                                0.20f, 0.44f, 0.30f, 0.62f, 0.13f, 0.35f, 0.25f, 0.45f, 0.30f, 0.22f } },
        };
        [Tooltip("At/below this difficulty a level is a protected breather: no heat, no jitter.")]
        public float breatherCeil = 0.16f;
        [Tooltip("Hot macro-cycle multiplier — applied to non-breathers only.")]
        public float hotMult = 1.14f;
        public int macroHotLen = 150;
        public int macroCoolLen = 100;
        [Tooltip("Peak cap after the hot multiplier.")]
        public float wavePeakCeil = 0.72f;

        [Header("Milestones")]
        public int milestoneEvery = 50;
        public int milestoneSize = 11;
        public float milestoneMin = 0.85f;
        public float milestoneMax = 1.00f;
        [Tooltip("Clean levels in this weight range are hoarded for milestones instead of discarded.")]
        public int milestonePoolMin = 600;
        public int milestonePoolMax = 850;

        [Header("Matching & variation")]
        [Tooltip("Deterministic wobble (hash of level number) so the wave is never recognizable.")]
        public float jitter = 0.025f;
        [Tooltip("RELATIVE tolerance: accept within target ± (this × target weight). 0.10 = ±10%.")]
        public float matchTol = 0.10f;
        [Tooltip("Tolerance never shrinks below this many weight points (matters for tiny targets).")]
        public int minTolWeight = 4;

        [Serializable]
        public class SizeRange
        {
            public int size;
            [Tooltip("Lightest weight a board of this size can realistically reach.")]
            public int minWeight;
            [Tooltip("Heaviest clean weight worth asking of this size.")]
            public int maxWeight;
        }

        [Header("Board size rotation (decade slots X1..X0)")]
        public int[] sizeRotation = { 8, 10, 10, 9, 10, 10, 9, 10, 10, 10 };
        [Tooltip("Feasible weight range per size (from MSet data). When a slot's target falls outside " +
                 "its rotation size's range, the smallest size whose range fits is used instead.")]
        public SizeRange[] sizeWeightRanges =
        {
            // floors reflect what RANDOM generation actually produces (measured), not the
            // hand-made MSet references — e.g. only ~5% of random 8x8 boards weigh under ~50
            new SizeRange { size = 8,  minWeight = 19, maxWeight = 700 },
            new SizeRange { size = 9,  minWeight = 55, maxWeight = 950 },
            new SizeRange { size = 10, minWeight = 80, maxWeight = 1300 },
            new SizeRange { size = 11, minWeight = 150, maxWeight = 1600 },
        };

        [Header("Quality gates")]
        [Tooltip("No guessing, ever.")]
        public int maxTrials = 0;
        [Tooltip("The single most expensive deduction step may hold at most this share of the weight.")]
        public float maxPeakShare = 0.40f;
        [Tooltip("Grinder reject: weight per paid step must be at least this (meaty levels only).")]
        public float minWeightPerStep = 8f;
        [Tooltip("Marathon reject.")]
        public int maxPaidSteps = 18;
        public int milestoneMaxPaidSteps = 24;
        [Tooltip("Levels must open gently: share of weight paid in the first third of the solve.")]
        public float maxStartShare = 0.55f;
        public float peakMaxStartShare = 0.65f;
        [Tooltip("Wall/grind gates are skipped below this weight so onboarding is not rejected.")]
        public int gateMinWeight = 100;

        public LevelTarget GetTarget(int level)
        {
            if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));
            float d;
            Role role;
            int size;
            bool milestone = milestoneEvery > 0 && level % milestoneEvery == 0;

            if (level <= learnEnd)
            {
                if (milestone && learnMilestones.Length > 0)
                {
                    int i = Math.Min(level / milestoneEvery - 1, learnMilestones.Length - 1);
                    d = learnMilestones[i];
                    role = Role.Milestone;
                    size = milestoneSize;
                }
                else
                {
                    float prog = (float)Math.Pow(level / (float)learnEnd, learnExponent);
                    float median = learnStart + (learnEndMedian - learnStart) * prog;
                    d = median * learnSlotMult[(level - 1) % learnSlotMult.Length];
                    d = Clamp(d + JitterAt(level), 0.01f, learnMax);
                    role = RoleOf(d);
                    size = level <= teachSizes.Length ? teachSizes[level - 1]
                        : FitSize((int)Math.Round(d * weightCap), sizeRotation[(level - 1) % sizeRotation.Length]);
                }
            }
            else if (milestone)
            {
                d = milestoneMin + (milestoneMax - milestoneMin) * Hash01((uint)level * 2u + 1u);
                role = Role.Milestone;
                size = milestoneSize;
            }
            else
            {
                var tpl = waveTemplates[TemplateIndex((level - learnEnd - 1) / WaveLen)].values;
                d = tpl[(level - learnEnd - 1) % WaveLen % tpl.Length];
                role = RoleOf(d);
                if (d > breatherCeil)   // breathers are protected: no heat, no jitter
                {
                    if (IsHot(level)) d *= hotMult;
                    d = Clamp(d + JitterAt(level), 0.05f, wavePeakCeil);
                }
                size = FitSize((int)Math.Round(d * weightCap), sizeRotation[(level - 1) % sizeRotation.Length]);
            }

            var t = new LevelTarget
            {
                level = level,
                boardSize = size,
                difficulty = d,
                targetWeight = (int)Math.Round(d * weightCap),
                role = role,
            };
            if (role == Role.Milestone && level > learnEnd)
            {
                t.minWeight = (int)(milestoneMin * weightCap);
                t.maxWeight = milestonePoolMax;   // hoarded clean pool qualifies
            }
            else
            {
                int tol = Math.Max(minTolWeight, (int)(matchTol * t.targetWeight));
                t.minWeight = Math.Max(1, t.targetWeight - tol);
                t.maxWeight = t.targetWeight + tol;
            }
            return t;
        }

        int WaveLen => waveTemplates[0].values.Length;

        // Keep the rotation size when the target is feasible on it; otherwise the smallest
        // size whose range holds the target (so easy learning slots drop to small boards).
        int FitSize(int targetWeight, int preferred)
        {
            foreach (var r in sizeWeightRanges)
                if (r.size == preferred && targetWeight >= r.minWeight && targetWeight <= r.maxWeight)
                    return preferred;
            int best = preferred, bestSize = int.MaxValue;
            foreach (var r in sizeWeightRanges)
                if (targetWeight >= r.minWeight && targetWeight <= r.maxWeight && r.size < bestSize)
                { best = r.size; bestSize = r.size; }
            return best;
        }

        Role RoleOf(float d) =>
            d <= breatherCeil ? Role.Breather :
            d < 0.30f ? Role.Normal :
            d < 0.55f ? Role.Build : Role.Peak;

        bool IsHot(int level) =>
            (level - learnEnd - 1) % (macroHotLen + macroCoolLen) < macroHotLen;

        // Template per wave: a step of 1..len-1 each wave guarantees no immediate repeat.
        int TemplateIndex(int wave)
        {
            int len = waveTemplates.Length;
            if (len < 2) return 0;
            int idx = (int)(Hash(0xC0FFEEu) % (uint)len);
            for (int w = 1; w <= wave; w++)
                idx = (idx + 1 + (int)(Hash((uint)w) % (uint)(len - 1))) % len;
            return idx;
        }

        float JitterAt(int level) => (Hash01((uint)level) * 2f - 1f) * jitter;

        static float Hash01(uint x) => Hash(x) / (float)uint.MaxValue;

        static uint Hash(uint x)   // lowbias32 — deterministic across runs and platforms
        {
            x ^= x >> 16; x *= 0x7feb352d;
            x ^= x >> 15; x *= 0x846ca68b;
            x ^= x >> 16;
            return x;
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
