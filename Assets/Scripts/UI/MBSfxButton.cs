using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>Drop next to a Button to give it the standard click sound.</summary>
    [RequireComponent(typeof(Button))]
    public class MBSfxButton : MonoBehaviour {

        void Awake() {
            GetComponent<Button>().onClick.AddListener(() => CommonSFX.Play(CommonSFX.Instance.Click));
        }
    }
}
