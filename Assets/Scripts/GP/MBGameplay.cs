using Common;
using Core;
using QueensPuzzle;
using System.Collections;
using UnityEngine;

namespace qp {
    public class MBGameplay : MonoBehaviour {
        private void Awake() {

        }

        IEnumerator Start() {
            // load the board 
            ////////////////

            // load the level
            LevelData level;

            var board = transform.RecursiveFindChild("$Board");
            var cell = MBCell.LoadFromResource();

            // load the cells into $Board



            // update the borad size accroding to board

            // after update the size of board we will make the scaler to scale it and fit the screen
            transform.RecursiveFindChild<RTResizeInvoker.ABSMBListener>("$BoardScaler").ForceParentToReport();

            // wait for UI to refresh our Layou+
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
        }

    }
}

