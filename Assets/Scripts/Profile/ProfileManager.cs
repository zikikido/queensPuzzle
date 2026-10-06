using System;
using System.Threading.Tasks;

namespace qp {

    /// <summary>Why a name was rejected — the popup turns this into a line of text.</summary>
    public enum ENameError {
        Ok,
        TooShort,
        TooLong,
        BadCharacters,   // only letters and digits, see ProfileConfig
        NotAllowed,      // hits the banned list
    }

    /// <summary>
    /// The player as other players see them: a name and what they have equipped. Local and
    /// instant — there is nothing to wait for; the tournament sync carries it to the server with
    /// the next request, and the server stores it on the player's row.
    ///
    /// A name is given on first launch (generated, never empty) and can be changed from the Profile
    /// popup. Rules live in <see cref="ProfileConfig"/> and the same check must run on the server —
    /// a client can always lie.
    /// </summary>
    public static class ProfileManager {

        /// <summary>Boot (MBStartup). Loads the blob, makes sure there is a name, and pushes it if
        /// the server hasn't got this version yet.</summary>
        public static void Init() => throw new NotImplementedException();

        /// <summary>Send the profile if the server hasn't got it. Called at boot, after an edit, and
        /// when the app comes back to the foreground — there is no schedule, because a profile almost
        /// never changes. Does nothing without a confirmed connection; the next focus tries again.</summary>
        public static Task PushIfUnsent() => throw new NotImplementedException();

        /// <summary>The blob. Written only here — the UI reads.</summary>
        public static ProfileState State => throw new NotImplementedException();

        /// <summary>The name shown to other players. Never empty after <see cref="Init"/>.</summary>
        public static string Name => throw new NotImplementedException();

        /// <summary>Change the name. Returns why it was refused; <see cref="ENameError.Ok"/> means
        /// it was saved and will reach the server on the next sync.</summary>
        public static ENameError SetName(string name) => throw new NotImplementedException();

        /// <summary>Check a name without saving it — for live feedback while typing.</summary>
        public static ENameError Validate(string name) => throw new NotImplementedException();

        // Equipping skins comes with the avatars — the state already carries them, nothing reads
        // them yet.

        // ---- internal --------------------------------------------------------------------
        //
        // _generateName — "LuckyPaw42": a word from the config + digits, so two players rarely
        //                 collide and nobody starts as "Player".
        // Config        — Resources/ProfileConfig: length, allowed characters, the banned list and
        //                 the words a generated name is built from.
    }
}
