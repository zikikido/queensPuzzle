using System.Threading.Tasks;

namespace qp {

    /// <summary>
    /// The profile server as the client sees it — one call, and one direction. The player edits
    /// their profile on the device; the server is told so other players see the right name on the
    /// leaderboard. Nothing comes back yet: when prizes land, owning an item will have to come FROM
    /// the server, and that is the moment this grows an answer.
    ///
    /// Who is asking is part of the connection, not of the call: the implementation is built with
    /// the player's identity (later a token) and sends it itself.
    /// A failure is thrown; the profile stays marked as unsent and goes out again later.
    /// </summary>
    public interface IProfileBackend {

        /// <summary>Store this player's profile. Sent on the first launch and after every edit —
        /// not on a schedule, since it almost never changes.</summary>
        Task Push(ProfilePush profile);
    }
}
