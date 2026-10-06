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

        /// <summary>The server hasn't got this version yet. Turned on when the name is generated on
        /// first launch or changed later, turned off once a push succeeds — so a push that failed
        /// simply goes out again.</summary>
        public bool unsent;

        // Equipped skins arrive with the avatars.

        public static ProfileState Load() => _holder.Value ?? new ProfileState();

        public void Save() => _holder.Save(this);
    }
}
