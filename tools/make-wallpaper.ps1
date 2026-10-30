# Paints the sample wallpaper used for the preview images: a dusk over a mountain lake, 2560 x 1600.
# It is generated from scratch (noise + gradients), so there is no third-party image and no licence to
# worry about; treat the result as CC0.
#
#   powershell -ExecutionPolicy Bypass -File tools\make-wallpaper.ps1
param([string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) 'docs\wallpaper.jpg'))
Add-Type -AssemblyName System.Drawing

$source = @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

public static class Scenery
{
    const int W = 2560, H = 1600;
    const float Horizon = 960f;
    const float SunX = 1660f;

    static uint Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393) + (uint)(y * 668265263) + (uint)(seed * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
    }
    static float Rnd(int x, int y, int seed) { return (Hash(x, y, seed) & 0xFFFFFF) / 16777215f; }

    static float Noise(float x, float y, int seed)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        float tx = x - xi, ty = y - yi;
        tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
        float a = Rnd(xi, yi, seed), b = Rnd(xi + 1, yi, seed), c = Rnd(xi, yi + 1, seed), d = Rnd(xi + 1, yi + 1, seed);
        return (a + (b - a) * tx) + ((c + (d - c) * tx) - (a + (b - a) * tx)) * ty;
    }

    static float Fbm(float x, float y, int octaves, int seed)
    {
        float sum = 0, amp = 0.5f, f = 1;
        for (int i = 0; i < octaves; i++) { sum += amp * Noise(x * f, y * f, seed + i * 31); f *= 2; amp *= 0.5f; }
        return sum;
    }

    static float Clamp01(float v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }
    static float Smooth(float a, float b, float v) { float t = Clamp01((v - a) / (b - a)); return t * t * (3 - 2 * t); }
    static float Mix(float a, float b, float t) { return a + (b - a) * t; }

    struct C { public float R, G, B; public C(float r, float g, float b) { R = r; G = g; B = b; }
        public static C Lerp(C a, C b, float t) { return new C(Mix(a.R, b.R, t), Mix(a.G, b.G, t), Mix(a.B, b.B, t)); } }

    static readonly float[] SkyV = { 0f, 0.38f, 0.66f, 0.86f, 1f };
    static readonly C[] SkyC = {
        new C(18, 26, 70), new C(62, 56, 128), new C(170, 92, 150), new C(250, 150, 125), new C(255, 208, 160) };

    static C Sky(float v)
    {
        for (int i = 1; i < SkyV.Length; i++)
            if (v <= SkyV[i]) return C.Lerp(SkyC[i - 1], SkyC[i], Smooth(SkyV[i - 1], SkyV[i], v));
        return SkyC[SkyC.Length - 1];
    }

    // mountain ridges, far to near
    static float[][] ridge = new float[3][];
    static readonly float[] ridgeBase = { 205f, 130f, 62f };
    static readonly float[] ridgeAmp = { 150f, 190f, 150f };
    static readonly float[] ridgeScale = { 520f, 330f, 220f };
    static readonly C[] ridgeColor = { new C(132, 96, 156), new C(86, 66, 124), new C(38, 38, 82) };
    static readonly float[] ridgeHaze = { 0.5f, 0.28f, 0.1f };

    static void BuildRidges()
    {
        for (int k = 0; k < 3; k++)
        {
            ridge[k] = new float[W];
            for (int x = 0; x < W; x++)
            {
                float n = Fbm(x / ridgeScale[k] + k * 7.3f, k * 3.7f, 5, 100 + k * 17);
                float peaks = 1 - Math.Abs(2 * Fbm(x / (ridgeScale[k] * 0.55f) + 40 + k, 9.1f + k, 4, 200 + k) - 1);   // ridged
                ridge[k][x] = Horizon - ridgeBase[k] - ridgeAmp[k] * (0.65f * n + 0.35f * peaks * peaks);
            }
        }
    }

    // colour of the scene above the waterline (sky + clouds + stars + mountains)
    static C Scene(float x, float y)
    {
        float v = Clamp01(y / Horizon);
        C c = Sky(v);

        float dx = x - SunX, dy = y - (Horizon - 30f);
        float d2 = dx * dx + dy * dy;
        float glow = (float)Math.Exp(-d2 / (2 * 430f * 430f));
        c = new C(c.R + 255 * 0.55f * glow, c.G + 150 * 0.5f * glow, c.B + 70 * 0.4f * glow);
        float disc = 1 - Smooth(52f, 66f, (float)Math.Sqrt(d2));
        c = C.Lerp(c, new C(255, 238, 205), disc * 0.95f);

        // clouds: stretched fractal noise, warm near the sun and violet away from it
        float cn = Fbm(x / 760f, y / 230f + 3.1f, 6, 5);
        float density = Smooth(0.5f, 0.76f, cn) * Smooth(0.05f, 0.35f, v) * (1 - Smooth(0.82f, 0.97f, v));
        float lit = (float)Math.Exp(-Math.Sqrt(d2) / 1100f);
        C cloud = C.Lerp(new C(120, 96, 176), new C(255, 176, 140), lit);
        float rim = Smooth(0.55f, 0.7f, cn) * (1 - Smooth(0.7f, 0.9f, cn));
        cloud = new C(cloud.R + 40 * rim * lit, cloud.G + 30 * rim * lit, cloud.B + 10 * rim * lit);
        c = C.Lerp(c, cloud, density * 0.78f);

        // a few stars high up
        if (v < 0.5f)
        {
            float s = Rnd((int)x, (int)y, 7);
            if (s > 0.9992f) { float a = (0.5f - v) / 0.5f * (1 - density) * 0.9f; c = C.Lerp(c, new C(255, 250, 240), a); }
        }

        // mountains
        int xi = (int)Math.Max(0, Math.Min(W - 1, x));
        for (int k = 0; k < 3; k++)
        {
            if (y >= ridge[k][xi])
            {
                C haze = Sky(Math.Min(1f, 0.95f));
                C m = C.Lerp(ridgeColor[k], haze, ridgeHaze[k]);
                float depth = Clamp01((y - ridge[k][xi]) / 260f);
                float tex = 0.9f + 0.2f * Fbm(x / 34f, y / 22f, 3, 300 + k);
                float sunSide = Clamp01(1.0f - Math.Abs(x - SunX) / 1400f);
                float edge = 1 - Smooth(0f, 5f, y - ridge[k][xi]);            // thin warm rim light on the ridge
                m = new C(m.R * tex * (1 - 0.25f * depth) + 70 * edge * sunSide,
                          m.G * tex * (1 - 0.25f * depth) + 40 * edge * sunSide,
                          m.B * tex * (1 - 0.20f * depth) + 20 * edge * sunSide);
                c = m;
            }
        }
        return c;
    }

    public static Bitmap Render()
    {
        BuildRidges();
        byte[] buf = new byte[W * H * 4];

        Parallel.For(0, H, delegate(int y)
        {
            for (int x = 0; x < W; x++)
            {
                C c;
                if (y <= Horizon)
                {
                    c = Scene(x, y);
                }
                else
                {
                    // the lake: the sky and mountains flipped, rippled and darkened
                    float depth = (y - Horizon) / (H - Horizon);
                    float ripple = (float)Math.Sin(y * 0.06f + Fbm(x / 300f, y / 14f, 3, 77) * 6f) * (4f + 22f * depth);
                    float ym = Horizon - (y - Horizon) * 0.92f;
                    c = Scene(x + ripple, ym);
                    float streak = Fbm(x / 520f, y / 5f, 4, 91);
                    float k = (0.80f - 0.30f * depth) * (0.88f + 0.30f * streak);
                    c = new C(c.R * k + 8, c.G * k + 16, c.B * k + 38 * (1 - depth * 0.5f));
                    float sun = (float)Math.Exp(-((x - SunX) * (x - SunX)) / (2 * 150f * 150f)) * (1 - depth * 0.7f) * Smooth(0.45f, 0.8f, streak);
                    c = new C(c.R + 110 * sun, c.G + 70 * sun, c.B + 30 * sun);
                    // a soft haze where water meets land
                    float shore = 1 - Smooth(0f, 14f, y - Horizon);
                    C haze = Sky(0.97f);
                    c = C.Lerp(c, new C(haze.R * 0.8f, haze.G * 0.7f, haze.B * 0.8f), shore * 0.5f);
                }

                float vx = (x - W / 2f) / (W / 2f), vy = (y - H / 2f) / (H / 2f);
                float vig = 1 - 0.18f * (vx * vx * 0.6f + vy * vy);
                float grain = (Rnd(x, y, 1234) - 0.5f) * 5f;
                int o = (y * W + x) * 4;
                buf[o + 0] = (byte)Math.Max(0, Math.Min(255, c.B * vig + grain));
                buf[o + 1] = (byte)Math.Max(0, Math.Min(255, c.G * vig + grain));
                buf[o + 2] = (byte)Math.Max(0, Math.Min(255, c.R * vig + grain));
                buf[o + 3] = 255;
            }
        });

        Bitmap bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        BitmapData bd = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
        bmp.UnlockBits(bd);
        return bmp;
    }

    public static void SaveJpeg(Bitmap bmp, string path, long quality)
    {
        ImageCodecInfo codec = null;
        foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders()) if (c.MimeType == "image/jpeg") codec = c;
        EncoderParameters ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        bmp.Save(path, codec, ep);
    }
}
'@
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition $source

New-Item -ItemType Directory -Force (Split-Path -Parent $Out) | Out-Null
$sw = [Diagnostics.Stopwatch]::StartNew()
$bmp = [Scenery]::Render()
[Scenery]::SaveJpeg($bmp, $Out, 93)
Write-Host ("Wrote {0} ({1} KB, {2:N1}s)" -f $Out, [math]::Round((Get-Item $Out).Length / 1KB), $sw.Elapsed.TotalSeconds)
