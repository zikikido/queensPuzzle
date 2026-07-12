using Common;
using System;
using UnityEngine;

namespace qp {
    public class MBZGPOrder : MBZOrder {

        public enum GP {
            BOARD_BG,
            BOARD_CELLS,
            BOARD_ANIMS,
            TOUTURIAL
        }

        public override Type GetEnumType() {
            return typeof(GP);
        }
    }
}

