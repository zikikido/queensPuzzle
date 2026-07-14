using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace qp {

    /// <summary>Drop next to a Button to give it the standard press/release sounds.</summary>
    [RequireComponent(typeof(Button))]
    public class MBSfxButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler {

        Button _button;
        bool _pressed;

        void Awake() {
            _button = GetComponent<Button>();
        }

        public void OnPointerDown(PointerEventData eventData) {
            if (!_button.IsInteractable())
                return;

            _pressed = true;
            CommonSFX.Play(CommonSFX.Instance.ButtonDown);
        }

        public void OnPointerUp(PointerEventData eventData) {
            if (!_pressed)
                return;

            _pressed = false;
            CommonSFX.Play(CommonSFX.Instance.ButtonUp);
        }
    }
}
