using Common;
using UnityEngine;

namespace qp {
    public class MBCell : MonoBehaviour {

        const string ResourcePath = "Reskin/GP/Cell";

        public static MBCell LoadFromResource() {
            return Resources.Load<MBCell>(ResourcePath);
        }

        public void Init(int clrindx, int xindex, int yindex, bool hasQueen) {

            transform.RecursiveFindChild<SpriteRenderer>("$CellSprite").color = SORegionsColors.ColorAt(clrindx);
        }
    }
}

