using Common;
using UnityEngine;

namespace qp {
    public class MBCell : MonoBehaviour {

        public enum ECellType {
            EMPTY,
            QUEEN,
            X,
            WRONG_QUEEN
        }

        const string ResourcePath = "GP/Cell";

        public static MBCell LoadFromResource() {
            return Resources.Load<MBCell>(ResourcePath);
        }

        public int X { get; private set; }            // column
        public int Y { get; private set; }            // row
        public ECellType State { get; private set; }
        public bool IsSolutionQueen { get; private set; }   // a queen belongs here in the solution

        GameObject _xGo, _queenGo, _wrongQueenGo;

        public Vector2 GetSize() {
            return (transform as RectTransform).sizeDelta;
        }

        public void Init(int clrindx, int xindex, int yindex, bool hasQueen) {
            X = xindex;
            Y = yindex;
            IsSolutionQueen = hasQueen;
            transform.RecursiveFindChild<SpriteRenderer>("$CellSprite").color = SORegionsColors.ColorAt(clrindx);
            _xGo = transform.RecursiveFindChild("$X").gameObject;
            _queenGo = transform.RecursiveFindChild("$Queen").gameObject;
            _wrongQueenGo = transform.RecursiveFindChild("$WrongQueen").gameObject;
            MarkCell(ECellType.EMPTY);
        }

        public void MarkCell(ECellType type) {
            State = type;
            _xGo.SetActive(type == ECellType.X);
            _queenGo.SetActive(type == ECellType.QUEEN);
            _wrongQueenGo.SetActive(type == ECellType.WRONG_QUEEN);
        }
    }
}

