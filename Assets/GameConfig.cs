namespace qp {
    /// <summary>
    /// Game tuning config — ads pacing and rewarded-ad payouts. Plain values, edit here.
    /// </summary>
    public static class GameConfig {

        // Ads start from these levels (1-based); earlier levels stay ad-free.
        public static int StartShowBannerAtLevel = 11;
        public static int StartShowInterAtLevel = 11;

        // Granted when the player finishes a rewarded ad.
        public static int BoosterAddedAfterRewarded = 1;
        public static int BonesAddedAfterRewarded = 3;
    }
}
