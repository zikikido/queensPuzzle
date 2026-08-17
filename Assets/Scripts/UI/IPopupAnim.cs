namespace qp {
    /// <summary>
    /// Optional show animation for a popup, implemented by the reskin next to the popup script.
    /// Popup logic only knows this contract, so a skin that ships without an animation simply
    /// has no component and the call no-ops.
    /// </summary>
    public interface IPopupAnim {
        /// <summary>Play the open animation from the start. Called by the popup's Show().</summary>
        void PlayIn();

        /// <summary>True once the open animation has finished — callers can wait on it.</summary>
        bool IsFinished();
    }
}
