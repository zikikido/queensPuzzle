using System;

namespace qp {

    /// <summary>What the server needs to know about a player. Equipped skins join it with the
    /// avatars.</summary>
    [Serializable]
    public class ProfilePush {
        public string name;
    }
}
