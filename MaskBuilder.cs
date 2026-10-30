using System;
using System.Collections.Generic;

namespace WallBlur
{
    /// <summary>One rounded rectangle that contributes to the blurred region (mask-space pixels).</summary>
    struct Shape
    {
        public float Cx, Cy, Hx, Hy, Radius;
    }

    /// <summary>
    /// Builds a signed distance field of the blurred region. Every shape is a rounded-rectangle SDF;
    /// fields are merged with a polynomial smooth-min so neighbouring icons melt into one region with
    /// rounded concave joints. The compositor turns the distance into per-channel coverage, which is
    /// what makes a soft feather and a chromatic fringe possible.
    /// </summary>
    class MaskBuilder
    {
        public const float Far = 1e6f;

        public readonly int Width, Height;
        /// <summary>Signed distance (mask pixels) to the region edge; negative inside. Far where unknown.</summary>
        public readonly float[] Field;

        public MaskBuilder(int w, int h)
        {
            Width = w; Height = h;
            Field = new float[w * h];
        }

        /// <param name="smooth">Smooth-min radius, mask px.</param>
        /// <param name="reach">How far from each shape the field must stay accurate, mask px.</param>
        public void Build(List<Shape> shapes, float smooth, float reach)
        {
            for (int i = 0; i < Field.Length; i++) Field[i] = Far;
            if (shapes.Count == 0) return;

            float extent = Math.Max(reach, smooth) + 2;
            foreach (Shape s in shapes)
            {
                int x0 = Math.Max(0, (int)Math.Floor(s.Cx - s.Hx - extent));
                int x1 = Math.Min(Width - 1, (int)Math.Ceiling(s.Cx + s.Hx + extent));
                int y0 = Math.Max(0, (int)Math.Floor(s.Cy - s.Hy - extent));
                int y1 = Math.Min(Height - 1, (int)Math.Ceiling(s.Cy + s.Hy + extent));
                for (int y = y0; y <= y1; y++)
                {
                    int row = y * Width;
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = Sdf(s, x + 0.5f, y + 0.5f);
                        float cur = Field[row + x];
                        Field[row + x] = cur >= Far ? d : SmoothMin(cur, d, smooth);
                    }
                }
            }
        }

        /// <summary>
        /// Low-passes the distance field so the zero contour becomes one smooth curve: bumps left by
        /// differently sized neighbours are rounded off. Values are clamped first so unknown (Far) areas
        /// don't poison the average.
        /// </summary>
        public void Round(double sigma, float clamp)
        {
            int[] radii = Blur.Radii(sigma, 3);
            if (radii[0] + radii[1] + radii[2] < 1) return;
            for (int i = 0; i < Field.Length; i++)
            {
                float f = Field[i];
                Field[i] = f > clamp ? clamp : (f < -clamp ? -clamp : f);
            }
            float[] tmp = new float[Field.Length];
            for (int pass = 0; pass < 3; pass++)
            {
                if (radii[pass] < 1) continue;
                BoxH(Field, tmp, radii[pass]);
                BoxV(tmp, Field, radii[pass]);
            }
        }

        static int Clamp(int v, int max) { return v < 0 ? 0 : (v > max ? max : v); }

        void BoxH(float[] src, float[] dst, int r)
        {
            double inv = 1.0 / (2 * r + 1);
            for (int y = 0; y < Height; y++)
            {
                int row = y * Width;
                double s = 0;
                for (int i = -r; i <= r; i++) s += src[row + Clamp(i, Width - 1)];
                for (int x = 0; x < Width; x++)
                {
                    dst[row + x] = (float)(s * inv);
                    s += src[row + Clamp(x + r + 1, Width - 1)] - src[row + Clamp(x - r, Width - 1)];
                }
            }
        }

        void BoxV(float[] src, float[] dst, int r)
        {
            double inv = 1.0 / (2 * r + 1);
            double[] sum = new double[Width];
            for (int i = -r; i <= r; i++)
            {
                int o = Clamp(i, Height - 1) * Width;
                for (int x = 0; x < Width; x++) sum[x] += src[o + x];
            }
            for (int y = 0; y < Height; y++)
            {
                int d = y * Width;
                for (int x = 0; x < Width; x++) dst[d + x] = (float)(sum[x] * inv);
                int add = Clamp(y + r + 1, Height - 1) * Width;
                int sub = Clamp(y - r, Height - 1) * Width;
                for (int x = 0; x < Width; x++) sum[x] += src[add + x] - src[sub + x];
            }
        }

        static float Sdf(Shape s, float px, float py)
        {
            float qx = Math.Abs(px - s.Cx) - (s.Hx - s.Radius);
            float qy = Math.Abs(py - s.Cy) - (s.Hy - s.Radius);
            float ox = qx > 0 ? qx : 0, oy = qy > 0 ? qy : 0;
            float inside = Math.Min(Math.Max(qx, qy), 0f);
            return (float)Math.Sqrt(ox * ox + oy * oy) + inside - s.Radius;
        }

        static float SmoothMin(float a, float b, float k)
        {
            float h = Math.Max(k - Math.Abs(a - b), 0f) / k;
            return Math.Min(a, b) - h * h * k * 0.25f;
        }
    }
}
