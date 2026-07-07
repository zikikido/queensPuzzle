using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace qp {
    // Loading scene driver: kicks off the boot tasks and mirrors their progress on the bar.
    public class MBLoading : MonoBehaviour {

        [SerializeField] Slider _bar;

        IEnumerator Start() {
            _bar.minValue = 0;
            _bar.maxValue = MBStartup.TasksTotal;
            _bar.value = MBStartup.TasksDone;

            StartCoroutine(MBStartup.Run());

            while (!MBStartup.Finished) {
                _bar.maxValue = MBStartup.TasksTotal;
                _bar.value = MBStartup.TasksDone;
                yield return null;
            }

            _bar.value = _bar.maxValue;
            Navigator.Go(Navigator.Lobby);
        }
    }
}
