using System;
using Common;

namespace qp {

    /// <summary>
    /// Who the player is, as everyone else sees them: for now the name on the leaderboard, later
    /// what they have equipped too. Saved as one JSON blob (PlayerPrefs). Mutated only by
    /// <see cref="ProfileManager"/>; the server is told about it on the next sync.
    /// </summary>
    [Serializable]
    public class ProfileState {

        static readonly PlayerPrefsHelper.ObjectHolder<ProfileState> _holder
            = new PlayerPrefsHelper.ObjectHolder<ProfileState>("qp_profile");

        /// <summary>Shown to other players. Empty only before the first launch finishes.</summary>
        public string name = "";

        /// <summary>Bumped on every change: the name generated on first launch, a rename, a skin
        /// later on.</summary>
        public int rev;

        /// <summary>The revision the server confirmed. Anything else means there is something to
        /// send — including an edit made while the previous request was still in flight, which is
        /// why this is a revision and not a flag.</summary>
        public int syncedRev;

        // Equipped skins arrive with the avatars.

        public static ProfileState Load() => _holder.Value ?? new ProfileState();

        public void Save() => _holder.Save(this);
    }
}
