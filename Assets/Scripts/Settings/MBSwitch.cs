using Common;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace qp {
    /// <summary>
    /// A tappable on/off switch: the $ON / $OFF children show the state, a tap flips it and
    /// reports through the callback. Wire with Init(initial, onChanged) — Init is safe to call
    /// again (e.g. every popup open) to resync state without firing the callback.
    /// </summary>
    public class MBSwitch : MonoBehaviour {

        GameObject _onGo, _offGo;
        Action<bool> _onChanged;
        bool _on;

        public bool IsOn => _on;

        void Awake() {
            var on = transform.RecursiveFindChild("$ON");
            var off = transform.RecursiveFindChild("$OFF");
            _onGo = on != null ? on.gameObject : null;
            _offGo = off != null ? off.gameObject : null;

            var btn = GetComponentInChildren<Button>(true);
            if (btn == null) btn = gameObject.AddComponent<Button>();   // whole switch tappable
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(Toggle);
        }

        /// <summary>Set the state (no callback) and the change handler.</summary>
        public void Init(bool on, Action<bool> onChanged) {
            _on = on;
            _onChanged = onChanged;
            Apply();
        }

        public void Toggle() {
            _on = !_on;
            Apply();
            _onChanged?.Invoke(_on);
        }

        void Apply() {
            if (_onGo != null) _onGo.SetActive(_on);
            if (_offGo != null) _offGo.SetActive(!_on);
        }
    }
}
