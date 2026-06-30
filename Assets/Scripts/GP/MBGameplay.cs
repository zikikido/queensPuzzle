using Common;
using Core;
using System.Collections;
using UnityEngine;

namespace qp {
    public class MBGameplay : MonoBehaviour {
        private void Awake() {

        }

        IEnumerator Start() {
            // load the level 

            // load the board into $Board
            var board = transform.RecursiveFindChild("$Board");

            // update the borad size accroding to board

            // after update the size of board we will make the scaler to scale it and fit the screen
            transform.RecursiveFindChild<RTResizeInvoker.ABSMBListener>("$BoardScaler").ForceParentToReport();

            // wait for UI to refresh our Layou+
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
        }

    }
}

