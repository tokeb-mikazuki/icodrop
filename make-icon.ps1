# Draws the icodrop icon - three water drops that have just touched and pulled together, on a soft
# blue tile - and writes icodrop.ico with 16..256 px images. Run once; build.ps1 embeds the result.
#
# The drops use the same rules as the app itself: smooth-min merging of circles (surface tension),
# light from the upper left, a bright rim on the lit side and a cooler, darker rim on the far side.
Add-Type -AssemblyName System.Drawing

$source = @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

public static class DropIcon
{
    // Design space is 44 x 44 units.
    static readonly float[] cx = { 16.5f, 28.5f, 30.5f };
    static readonly float[] cy = { 27.5f, 16.5f, 31.0f };
    static readonly float[] cr = { 9.8f, 7.4f, 4.6f };
    const float Lx = -0.55f, Ly = -0.835f;   // towards the light

    static float Smin(float a, float b, float k)
    {
        float h = Math.Max(k - Math.Abs(a - b), 0f) / k;
        return Math.Min(a, b) - h * h * k * 0.25f;
    }

    static float Sm01(float t)
    {
        t = t < 0 ? 0 : (t > 1 ? 1 : t);
        return t * t * (3 - 2 * t);
    }

    static float Drops(float x, float y)
    {
        float d = 0;
        for (int i = 0; i < cx.Length; i++)
        {
            float dx = x - cx[i], dy = y - cy[i];
            float di = (float)Math.Sqrt(dx * dx + dy * dy) - cr[i];
            d = i == 0 ? di : Smin(d, di, 5.0f);
        }
        return d;
    }

    static float Tile(float x, float y)   // rounded square, radius 11
    {
        float qx = Math.Abs(x - 22f) - (21.5f - 11f), qy = Math.Abs(y - 22f) - (21.5f - 11f);
        float ox = qx > 0 ? qx : 0, oy = qy > 0 ? qy : 0;
        return (float)Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0f) - 11f;
    }

    static float Lerp(float a, float b, float t) { return a + (b - a) * t; }

    public static Bitmap Render(int size)
    {
        int ss = size <= 64 ? 4 : (size <= 128 ? 3 : 2);
        int S = size * ss;
        float unit = 44f / S;            // design units per supersampled pixel
        float pxPerUnit = S / 44f;
        double[] accR = new double[size * size], accG = new double[size * size], accB = new double[size * size], accA = new double[size * size];

        Parallel.For(0, S, delegate(int py)
        {
            float v = (py + 0.5f) * unit;
            for (int px = 0; px < S; px++)
            {
                float u = (px + 0.5f) * unit;

                float cs = 0.5f - Tile(u, v) * pxPerUnit;
                cs = cs < 0 ? 0 : (cs > 1 ? 1 : cs);
                if (cs <= 0) continue;

                // tile: soft blue diagonal gradient
                float g = (u + v) / 88f;
                float R = Lerp(106, 47, g), G = Lerp(174, 107, g), B = 255f;

                // shadow of the drops, offset downwards
                float sd = Drops(u, v - 1.8f);
                float sa = 0.30f * (1 - Sm01((sd + 0.5f) / 3.2f));
                R = Lerp(R, 18, sa); G = Lerp(G, 58, sa); B = Lerp(B, 170, sa);

                // the drops
                float d = Drops(u, v);
                float cov = 0.5f - d * pxPerUnit;
                cov = cov < 0 ? 0 : (cov > 1 ? 1 : cov);
                if (cov > 0)
                {
                    float e = 0.2f;
                    float gx = Drops(u + e, v) - Drops(u - e, v), gy = Drops(u, v + e) - Drops(u, v - e);
                    float len = (float)Math.Sqrt(gx * gx + gy * gy);
                    float nx = len > 1e-5f ? gx / len : 0, ny = len > 1e-5f ? gy / len : 0;
                    float dl = nx * Lx + ny * Ly;
                    float lit = dl > 0 ? dl : 0, dark = dl < 0 ? -dl : 0;
                    float depth = -d < 0 ? 0 : -d;
                    float prof = 1 - Sm01(depth / 3.0f);

                    float fr = Lerp(248, 208, Sm01((v - 9f) / 28f));
                    float fg = Lerp(252, 232, Sm01((v - 9f) / 28f));
                    float fb = 255f;
                    float fa = 0.90f;

                    fr += (255 - fr) * lit * prof * 0.9f; fg += (255 - fg) * lit * prof * 0.9f; fb += (255 - fb) * lit * prof * 0.9f;
                    float dk = dark * prof * 0.55f;
                    fr = Lerp(fr, 110, dk); fg = Lerp(fg, 170, dk); fb = Lerp(fb, 255, dk);
                    fa += lit * prof * 0.10f;

                    // small glint on each drop
                    float spec = 0;
                    for (int i = 0; i < cx.Length; i++)
                    {
                        float hx = u - (cx[i] - 0.38f * cr[i]), hy = v - (cy[i] - 0.42f * cr[i]);
                        float hd = (float)Math.Sqrt(hx * hx + hy * hy) / (0.22f * cr[i]);
                        spec += (float)Math.Exp(-hd * hd);
                    }
                    if (spec > 1) spec = 1;
                    fr += (255 - fr) * spec * 0.9f; fg += (255 - fg) * spec * 0.9f; fb += (255 - fb) * spec * 0.9f;
                    fa += spec * 0.1f;
                    if (fa > 1) fa = 1;

                    float a = fa * cov;
                    R = Lerp(R, fr, a); G = Lerp(G, fg, a); B = Lerp(B, fb, a);
                }

                int o = (py / ss) * size + (px / ss);
                lock (accA)
                {
                    accR[o] += R * cs; accG[o] += G * cs; accB[o] += B * cs; accA[o] += cs;
                }
            }
        });

        Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        BitmapData bd = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        byte[] buf = new byte[bd.Stride * size];
        double n = ss * ss;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x, o = y * bd.Stride + x * 4;
                double a = accA[i];
                if (a <= 0) continue;
                buf[o] = (byte)Math.Min(255, accB[i] / a + 0.5);
                buf[o + 1] = (byte)Math.Min(255, accG[i] / a + 0.5);
                buf[o + 2] = (byte)Math.Min(255, accR[i] / a + 0.5);
                buf[o + 3] = (byte)Math.Min(255, a / n * 255 + 0.5);
            }
        Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
        bmp.UnlockBits(bd);
        return bmp;
    }
}
'@
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition $source

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$sizes = 16, 24, 32, 48, 64, 128, 256

function Get-Png([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

# Classic DIB entry (BITMAPINFOHEADER + bottom-up BGRA + 1-bpp AND mask) for the small sizes.
function Get-Dib([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([int]40); $bw.Write([int]$s); $bw.Write([int](2 * $s))
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]0)
    $bw.Write([int]($s * $s * 4)); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $s - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $s; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }
    $maskRow = [int]([math]::Ceiling($s / 32.0) * 4)
    $bw.Write((New-Object byte[] ($maskRow * $s)))
    $bw.Flush()
    return , $ms.ToArray()
}

$images = @()
foreach ($s in $sizes) {
    $bmp = [DropIcon]::Render($s)
    if ($s -ge 128) { $data = Get-Png $bmp } else { $data = Get-Dib $bmp }
    $images += , @($s, $data)
    if ($s -eq 256) { $bmp.Save((Join-Path $here 'icodrop-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $s = $img[0]; $data = $img[1]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$data.Length); $w.Write([int]$offset)
    $offset += $data.Length
}
foreach ($img in $images) { $w.Write([byte[]]$img[1]) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $here 'icodrop.ico'), $out.ToArray())
Write-Host "Wrote $(Join-Path $here 'icodrop.ico') ($($images.Count) sizes)"
