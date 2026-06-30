using Common;
using UnityEngine;

namespace qp {
    public class MBCell : MonoBehaviour {

        const string ResourcePath = "GP/Cell";

        public static MBCell LoadFromResource() {
            return Resources.Load<MBCell>(ResourcePath);
        }

        public Vector2 GetSize() {
            return (transform as RectTransform).sizeDelta;
        }

        public void Init(int clrindx, int xindex, int yindex, bool hasQueen) {

            transform.RecursiveFindChild<SpriteRenderer>("$CellSprite").color = SORegionsColors.ColorAt(clrindx);
        }
    }
}

