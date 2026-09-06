using UnityEngine;

namespace qp {

    /// <summary>An in-game reaction effect the gameplay can fire at a board position.</summary>
    public interface IInGameReactionFX {
        void Play(Vector3 worldPos);
    }

    /// <summary>Optional bridge: gameplay reports what color sits under a world position, so
    /// effect tints can avoid disappearing into the board. Null func / null color = unknown.</summary>
    public static class ReactionBackdrop {
        public static System.Func<Vector3, Color?> ColorAt;
    }
}
