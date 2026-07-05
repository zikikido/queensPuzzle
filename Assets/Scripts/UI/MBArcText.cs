using TMPro;
using UnityEngine;

namespace qp {
    /// <summary>
    /// Bends a TMP text into an arc (rainbow shape) by warping its mesh: each character is
    /// lifted along a parabola and tilted to follow the curve. Attach next to the TMP_Text,
    /// tweak ArcHeight in the Inspector — works live, any font, any text length.
    /// Negative height arcs downward.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(TMP_Text))]
    public class MBArcText : MonoBehaviour {

        [SerializeField] float _arcHeight = 20f;   // rise of the middle letter, in text units
        [SerializeField] float _tiltScale = 1f;    // how strongly letters rotate with the slope (0 = upright)

        TMP_Text _tmp;
        float _appliedHeight, _appliedTilt;
        string _appliedText;

        void OnEnable() { _tmp = GetComponent<TMP_Text>(); Apply(); }
        void OnValidate() { if (_tmp != null) Apply(); }

        void LateUpdate() {
            if (_tmp.havePropertiesChanged || _tmp.text != _appliedText ||
                _arcHeight != _appliedHeight || _tiltScale != _appliedTilt)
                Apply();
        }

        void Apply() {
            _appliedText = _tmp.text;
            _appliedHeight = _arcHeight;
            _appliedTilt = _tiltScale;

            _tmp.ForceMeshUpdate();
            var info = _tmp.textInfo;
            if (info.characterCount == 0) return;

            // text extents on the x axis — the arc parameter runs 0..1 across them
            float minX = float.MaxValue, maxX = float.MinValue;
            for (int i = 0; i < info.characterCount; i++) {
                var ch = info.characterInfo[i];
                if (!ch.isVisible) continue;
                minX = Mathf.Min(minX, ch.bottomLeft.x);
                maxX = Mathf.Max(maxX, ch.topRight.x);
            }
            float width = Mathf.Max(maxX - minX, 0.0001f);

            for (int i = 0; i < info.characterCount; i++) {
                var ch = info.characterInfo[i];
                if (!ch.isVisible) continue;

                var verts = info.meshInfo[ch.materialReferenceIndex].vertices;
                int vi = ch.vertexIndex;

                // character centre and its 0..1 position along the text
                Vector3 mid = (verts[vi] + verts[vi + 2]) * 0.5f;
                float t = (mid.x - minX) / width;

                // parabola: 0 at the ends, ArcHeight in the middle; slope gives the tilt
                float lift = _arcHeight * 4f * t * (1f - t);
                float slope = _arcHeight * 4f * (1f - 2f * t) / width;   // dY/dX
                var rot = Quaternion.Euler(0f, 0f, Mathf.Atan(slope * _tiltScale) * Mathf.Rad2Deg);

                for (int v = 0; v < 4; v++)
                    verts[vi + v] = mid + rot * (verts[vi + v] - mid) + new Vector3(0f, lift, 0f);
            }

            _tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
        }
    }
}
