using System;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace WallBlur
{
    /// <summary>Reads the current Windows wallpaper and renders sharp + pre-blurred copies of it.</summary>
    static class Wallpaper
    {
        /// <summary>The blurred copy (and the mask) are 1/Scale of the real resolution.</summary>
        public const int Scale = 2;

        public static string CurrentPath()
        {
            StringBuilder sb = new StringBuilder(520);
            Native.SystemParametersInfo(0x73, 520, sb, 0);
            return sb.ToString();
        }

        public static void ReadStyle(out int style, out bool tile, out Color back)
        {
            style = 10; tile = false; back = Colors.Black;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    if (k != null)
                    {
                        int.TryParse(Convert.ToString(k.GetValue("WallpaperStyle", "10")), out style);
                        tile = Convert.ToString(k.GetValue("TileWallpaper", "0")) == "1";
                    }
                }
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors"))
                {
                    if (k != null)
                    {
                        string[] p = Convert.ToString(k.GetValue("Background", "0 0 0")).Split(' ');
                        if (p.Length == 3)
                            back = Color.FromRgb(byte.Parse(p[0]), byte.Parse(p[1]), byte.Parse(p[2]));
                    }
                }
            }
            catch (Exception) { }
        }

        static BitmapSource Decode(string path)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
            try
            {
                BitmapImage bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(path);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch (Exception ex)
            {
                Settings.Log("wallpaper decode failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Result of rendering: Sharp is at full size, Blurred at 1/Scale size (the window stretches it back up).
        /// </summary>
        public class Layers
        {
            public int Width, Height;        // full-size
            public byte[] SharpPx;           // Bgra, Width x Height
            public int BlurWidth, BlurHeight;
            public byte[] BlurPx;            // Bgra, 1/Scale size
        }

        public static Layers Build(string path, int style, bool tile, Rect virt, Rect[] monitors, double sigma, double brightness, double tint)
        {
            BitmapSource img = Decode(path);
            int ignoredStyle; bool ignoredTile; Color back;
            ReadStyle(out ignoredStyle, out ignoredTile, out back);

            int w = (int)Math.Round(virt.Width), h = (int)Math.Round(virt.Height);
            Layers l = new Layers();
            BitmapSource sharp = Render(img, style, tile, back, virt, monitors, w, h, 1.0);
            l.Width = w; l.Height = h;
            l.SharpPx = new byte[w * h * 4];
            sharp.CopyPixels(l.SharpPx, w * 4, 0);

            int sw = (w + Scale - 1) / Scale, sh = (h + Scale - 1) / Scale;
            BitmapSource small = Render(img, style, tile, back, virt, monitors, sw, sh, 1.0 / Scale);
            byte[] px = new byte[sw * 4 * sh];
            small.CopyPixels(px, sw * 4, 0);

            double sg = Math.Max(0, sigma / Scale);
            Blur.Process(px, sw, sh, sg, 3, brightness, tint);
            l.BlurWidth = sw; l.BlurHeight = sh; l.BlurPx = px;
            return l;
        }

        static BitmapSource Render(BitmapSource img, int style, bool tile, Color back,
            Rect virt, Rect[] monitors, int pw, int ph, double s)
        {
            DrawingVisual dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(back), null, new Rect(0, 0, pw, ph));
                if (img != null)
                {
                    if (style == 22)
                    {
                        DrawOne(dc, img, new Rect(0, 0, virt.Width * s, virt.Height * s), 10, false, s);
                    }
                    else
                    {
                        foreach (Rect m in monitors)
                        {
                            Rect target = new Rect((m.X - virt.X) * s, (m.Y - virt.Y) * s, m.Width * s, m.Height * s);
                            DrawOne(dc, img, target, style, tile, s);
                        }
                    }
                }
            }
            RenderTargetBitmap rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        static void DrawOne(DrawingContext dc, BitmapSource img, Rect box, int style, bool tile, double s)
        {
            double iw = img.PixelWidth, ih = img.PixelHeight;
            dc.PushClip(new RectangleGeometry(box));
            if (tile)
            {
                ImageBrush b = new ImageBrush(img);
                b.TileMode = TileMode.Tile;
                b.ViewportUnits = BrushMappingMode.Absolute;
                b.Viewport = new Rect(box.X, box.Y, iw * s, ih * s);
                dc.DrawRectangle(b, null, box);
            }
            else
            {
                double dw, dh;
                switch (style)
                {
                    case 2: dw = box.Width; dh = box.Height; break;                         // stretch
                    case 6: { double k = Math.Min(box.Width / iw, box.Height / ih); dw = iw * k; dh = ih * k; break; } // fit
                    case 0: dw = iw * s; dh = ih * s; break;                                // center
                    default: { double k = Math.Max(box.Width / iw, box.Height / ih); dw = iw * k; dh = ih * k; break; } // fill
                }
                dc.DrawImage(img, new Rect(box.X + (box.Width - dw) / 2, box.Y + (box.Height - dh) / 2, dw, dh));
            }
            dc.Pop();
        }
    }


    static class Blur
    {
        /// <summary>
        /// Gaussian-ish blur (repeated box blur) in float precision, then darkening, then rounding with a
        /// touch of dither so smooth gradients don't show banding.
        /// </summary>
        public static void Process(byte[] px, int w, int h, double sigma, int passes, double brightness, double tint)
        {
            int n = px.Length;
            float[] a = new float[n];
            float[] b = new float[n];
            for (int i = 0; i < n; i++) a[i] = px[i];

            int[] radii = Radii(sigma, passes);
            for (int p = 0; p < passes; p++)
            {
                Horizontal(a, b, w, h, radii[p]);
                Vertical(b, a, w, h, radii[p]);
            }

            // Brighten by scaling plus a small lift so dark areas open up too; negative values darken.
            float bri = (float)Math.Max(-0.6, Math.Min(0.6, brightness));
            float mul = 1 + bri;
            float lift = bri > 0 ? bri * 40f : 0f;

            // Water tint: a little extra saturation plus a cool, clear-blue cast (buffer is B,G,R,A).
            float tintAmt = (float)Math.Max(0, Math.Min(0.5, tint));
            if (tintAmt > 0)
            {
                float sat = 1 + tintAmt * 1.6f, mix = tintAmt * 0.8f;
                for (int i = 0; i < n; i += 4)
                {
                    float lum = 0.114f * a[i] + 0.587f * a[i + 1] + 0.299f * a[i + 2];
                    a[i] = (lum + (a[i] - lum) * sat) * (1 - mix) + 255f * mix;
                    a[i + 1] = (lum + (a[i + 1] - lum) * sat) * (1 - mix) + 205f * mix;
                    a[i + 2] = (lum + (a[i + 2] - lum) * sat) * (1 - mix) + 150f * mix;
                }
            }
            Random rnd = new Random(1);
            for (int i = 0; i < n; i += 4)
            {
                for (int c = 0; c < 3; c++)
                {
                    float v = a[i + c] * mul + lift + (float)(rnd.NextDouble() - 0.5);
                    px[i + c] = (byte)(v < 0 ? 0 : (v > 255 ? 255 : v + 0.5f));
                }
                px[i + 3] = 255;
            }
        }

        /// <summary>
        /// Box radii for n passes whose combined variance matches a Gaussian of the given sigma. Mixing two
        /// neighbouring box sizes makes the blur change smoothly with sigma instead of in whole-pixel jumps.
        /// </summary>
        public static int[] Radii(double sigma, int n)
        {
            int[] r = new int[n];
            if (sigma < 0.3) return r;
            double ideal = Math.Sqrt(12 * sigma * sigma / n + 1);
            int wl = (int)Math.Floor(ideal);
            if (wl % 2 == 0) wl--;
            if (wl < 1) wl = 1;
            int wu = wl + 2;
            double mIdeal = (12 * sigma * sigma - n * wl * wl - 4 * n * wl - 3 * n) / (-4.0 * wl - 4);
            int m = (int)Math.Round(mIdeal);
            m = Math.Max(0, Math.Min(n, m));
            for (int i = 0; i < n; i++) r[i] = ((i < m ? wl : wu) - 1) / 2;
            return r;
        }
        static int Clamp(int v, int max) { return v < 0 ? 0 : (v > max ? max : v); }

        static void Horizontal(float[] src, float[] dst, int w, int h, int r)
        {
            double inv = 1.0 / (2 * r + 1);
            for (int y = 0; y < h; y++)
            {
                int row = y * w * 4;
                double s0 = 0, s1 = 0, s2 = 0;
                for (int i = -r; i <= r; i++)
                {
                    int o = row + Clamp(i, w - 1) * 4;
                    s0 += src[o]; s1 += src[o + 1]; s2 += src[o + 2];
                }
                for (int x = 0; x < w; x++)
                {
                    int d = row + x * 4;
                    dst[d] = (float)(s0 * inv); dst[d + 1] = (float)(s1 * inv); dst[d + 2] = (float)(s2 * inv);
                    int add = row + Clamp(x + r + 1, w - 1) * 4;
                    int sub = row + Clamp(x - r, w - 1) * 4;
                    s0 += src[add] - src[sub]; s1 += src[add + 1] - src[sub + 1]; s2 += src[add + 2] - src[sub + 2];
                }
            }
        }

        static void Vertical(float[] src, float[] dst, int w, int h, int r)
        {
            double inv = 1.0 / (2 * r + 1);
            int w4 = w * 4;
            double[] sum = new double[w4];
            for (int i = -r; i <= r; i++)
            {
                int o = Clamp(i, h - 1) * w4;
                for (int k = 0; k < w4; k++) sum[k] += src[o + k];
            }
            for (int y = 0; y < h; y++)
            {
                int d = y * w4;
                for (int k = 0; k < w4; k++) dst[d + k] = (float)(sum[k] * inv);
                int add = Clamp(y + r + 1, h - 1) * w4;
                int sub = Clamp(y - r, h - 1) * w4;
                for (int k = 0; k < w4; k++) sum[k] += src[add + k] - src[sub + k];
            }
        }
    }
}