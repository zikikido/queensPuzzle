using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// Shared board visuals used by both the builder window (editor) and the runtime game:
    /// the per-region color and the queen (crown) silhouette. The texture is a white
    /// silhouette — callers tint it (GUI.color / Image.color) to the shade they want.
    /// </summary>
    public static class BoardVisuals
    {
        /// <summary>Distinct pastel color for region <paramref name="i"/> of <paramref name="n"/>.</summary>
        public static Color RegionColor(int i, int n)
        {
            float h = (i * 0.6180339887f + 0.12f) % 1f; // golden-ratio spacing => distinct hues
            return Color.HSVToRGB(h, 0.42f, 0.97f);
        }

        /// <summary>White crown silhouette: a band across the lower middle plus three top spikes.</summary>
        public static Texture2D CreateQueenTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color[size * size];
            Vector2 s1a = new Vector2(0.30f, 0.16f), s1b = new Vector2(0.20f, 0.52f), s1c = new Vector2(0.40f, 0.52f);
            Vector2 s2a = new Vector2(0.50f, 0.10f), s2b = new Vector2(0.40f, 0.52f), s2c = new Vector2(0.60f, 0.52f);
            Vector2 s3a = new Vector2(0.70f, 0.16f), s3b = new Vector2(0.60f, 0.52f), s3c = new Vector2(0.80f, 0.52f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size;
                    float ny = (size - 0.5f - y) / size; // flip so y=0 is top
                    var p = new Vector2(nx, ny);
                    bool band = nx >= 0.22f && nx <= 0.78f && ny >= 0.50f && ny <= 0.74f;
                    bool on = band
                              || InTriangle(p, s1a, s1b, s1c)
                              || InTriangle(p, s2a, s2b, s2c)
                              || InTriangle(p, s3a, s3b, s3c);
                    px[y * size + x] = on ? Color.white : new Color(1, 1, 1, 0);
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>The crown texture wrapped as a Sprite for uGUI Images.</summary>
        public static Sprite CreateQueenSprite(int size = 64)
        {
            var tex = CreateQueenTexture(size);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sign(p, a, b), d2 = Sign(p, b, c), d3 = Sign(p, c, a);
            bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
            bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(hasNeg && hasPos);
        }

        static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
            => (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }
}
