using UnityEngine;

namespace qp {

    /// <summary>
    /// Spins whatever it is on, for as long as it is on.
    ///
    /// The tournament card uses it for the Calculating state, where the only honest thing to show
    /// is that something is still happening. There is nothing to configure beyond the speed, and
    /// no state: enabling the object starts it, disabling it stops it, which is exactly how the
    /// card switches between its states anyway.
    /// </summary>
    public sealed class MBSpinner : MonoBehaviour {

        [Tooltip("Degrees per second. Negative spins clockwise, which is what a loading ring " +
                 "normally does.")]
        [SerializeField] float _speed = -180f;

        // Unscaled: a spinner that freezes behind a paused popup reads as the game having hung,
        // which is the opposite of what it is there to say.
        void Update() => transform.Rotate(0f, 0f, _speed * Time.unscaledDeltaTime);
    }
}
