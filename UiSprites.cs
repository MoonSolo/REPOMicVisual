using System;
using UnityEngine;

namespace MicVisual
{
    /// <summary>
    /// Draws the two textures the indicator needs at runtime, so the mod ships as a single DLL
    /// with no image assets to load or path-find. Both come back as white silhouettes with an
    /// alpha channel, tinted at draw time.
    /// </summary>
    public static class UiSprites
    {
        private const int Supersample = 4;

        /// <summary>
        /// A classic microphone glyph: capsule body, U-shaped cradle, stem and base.
        /// </summary>
        public static Texture2D CreateMicIcon(int size)
        {
            Vector2 bodyTop = new Vector2(0.5f, 0.72f);
            Vector2 bodyBottom = new Vector2(0.5f, 0.44f);
            const float bodyRadius = 0.115f;

            Vector2 cradleCentre = new Vector2(0.5f, 0.55f);
            const float cradleRadius = 0.215f;
            const float cradleThickness = 0.05f;

            const float stemRadius = 0.028f;
            Vector2 stemTop = new Vector2(0.5f, 0.31f);
            Vector2 stemBottom = new Vector2(0.5f, 0.21f);
            Vector2 baseLeft = new Vector2(0.34f, 0.19f);
            Vector2 baseRight = new Vector2(0.66f, 0.19f);

            return Render(size, size, p =>
            {
                float d = Capsule(p, bodyTop, bodyBottom, bodyRadius);
                d = Mathf.Min(d, Capsule(p, stemTop, stemBottom, stemRadius));
                d = Mathf.Min(d, Capsule(p, baseLeft, baseRight, stemRadius));

                // Cradle: the lower half of a ring, so it reads as a "U" cupping the capsule.
                if (p.y <= cradleCentre.y)
                {
                    float ring = Mathf.Abs((p - cradleCentre).magnitude - cradleRadius)
                                 - cradleThickness * 0.5f;
                    d = Mathf.Min(d, ring);
                }

                return d;
            });
        }

        /// <summary>
        /// A rounded rectangle for the bar track and fill. Callers should keep the drawn rect at
        /// roughly the same aspect ratio as the texture, otherwise the corner radius stretches.
        /// </summary>
        public static Texture2D CreateRoundedRect(int width, int height, int radius)
        {
            radius = Mathf.Clamp(radius, 1, Mathf.Min(width, height) / 2);
            Vector2 half = new Vector2(width * 0.5f, height * 0.5f);
            float r = radius;

            return Render(width, height, p =>
            {
                Vector2 outer = new Vector2(Mathf.Abs(p.x - half.x), Mathf.Abs(p.y - half.y)) - half;
                return new Vector2(Mathf.Max(outer.x, 0f), Mathf.Max(outer.y, 0f)).magnitude
                       + Mathf.Min(Mathf.Max(outer.x, outer.y), 0f) - r;
            });
        }

        private static float Capsule(Vector2 p, Vector2 a, Vector2 b, float radius)
        {
            Vector2 pa = p - a;
            Vector2 ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - radius;
        }

        /// <summary>
        /// Rasterises a signed distance function (negative inside) into a white texture with
        /// antialiased edges, using 4x4 supersampling.
        /// </summary>
        private static Texture2D Render(int width, int height, Func<Vector2, float> sdf)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[width * height];
            float step = 1f / Supersample;
            float offset = step * 0.5f;
            float perPixel = 1f / (Supersample * Supersample);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float coverage = 0f;
                    for (int sy = 0; sy < Supersample; sy++)
                    {
                        for (int sx = 0; sx < Supersample; sx++)
                        {
                            var p = new Vector2(
                                (x + offset + sx * step) / width,
                                (y + offset + sy * step) / height);
                            // Half-pixel band keeps the antialiased edge one pixel wide.
                            coverage += Mathf.Clamp01(0.5f - sdf(p) * width) * perPixel;
                        }
                    }

                    byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(coverage) * 255f);
                    pixels[y * width + x] = new Color32(255, 255, 255, a);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }
    }
}
