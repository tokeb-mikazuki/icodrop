using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WallBlur
{
    /// <summary>
    /// Watches the desktop icon layout and renders the user's wallpaper with a blurred region behind the
    /// icons, installing the result as the live Windows wallpaper. The original wallpaper and fit style
    /// are remembered and restored on exit.
    ///
    /// Rendering runs on its own thread and always works on the newest request only: while a frame is
    /// being produced, any number of newer requests collapse into one, so dragging a slider never
    /// blocks the UI and never builds a backlog.
    /// </summary>
    class BlurEngine
    {
        /// <summary>Everything the render thread needs, captured on the UI thread.</summary>
        internal class Job
        {
            public Settings S;
            public List<Rect> Cells;
            public string OrigPath;
            public int Style;
            public bool Tile;
            public Rect Virt;
            public Rect[] Monitors;
            public int Generation;
        }

        readonly Settings settings;
        readonly DesktopShell shell = new DesktopShell();
        readonly DesktopIconReader icons = new DesktopIconReader();
        readonly DispatcherTimer timer = new DispatcherTimer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Dispatcher ui = Dispatcher.CurrentDispatcher;

        // UI-thread state
        Rect virt;
        Rect[] monitors = new Rect[0];
        string origPath = "";
        int origStyle = 10;
        bool origTile;
        List<Rect> cells = new List<Rect>();
        string lastSig = "";
        bool pending;
        double changedAt;
        int tick;

        // shared with the render thread
        readonly object gate = new object();
        readonly object applyLock = new object();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        Job latest;
        Thread worker;
        volatile bool stopping;
        volatile bool applied;
        int generation;

        // render-thread-only state
        Wallpaper.Layers layers;
        string layersKey = "";
        MaskBuilder mask;
        int flip;

        const double Debounce = 0.25;

        public BlurEngine(Settings s)
        {
            settings = s;
        }

        // ------------------------------------------------------------------ lifetime

        public void Start()
        {
            ReadMonitors();
            RecoverOrKeepOriginal();

            worker = new Thread(Work);
            worker.IsBackground = true;
            worker.SetApartmentState(ApartmentState.STA);   // WPF imaging wants an STA thread
            worker.Start();

            timer.Interval = TimeSpan.FromMilliseconds(250);
            timer.Tick += delegate { Safe(Tick); };
            timer.Start();

            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            pending = true;
            changedAt = 0;
        }

        public void Stop()
        {
            timer.Stop();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            stopping = true;
            wake.Set();
            if (worker != null) worker.Join(5000);
            Safe(Restore);
            icons.Dispose();
        }

        static void Safe(Action a)
        {
            try { a(); }
            catch (Exception ex) { Settings.Log(ex.ToString()); }
        }

        void OnDisplayChanged(object s, EventArgs e)
        {
            ReadMonitors();
            pending = true;
            changedAt = clock.Elapsed.TotalSeconds;
        }

        void ReadMonitors()
        {
            System.Windows.Forms.Screen[] screens = System.Windows.Forms.Screen.AllScreens;
            Rect[] list = new Rect[screens.Length];
            for (int i = 0; i < screens.Length; i++)
            {
                System.Drawing.Rectangle b = screens[i].Bounds;
                list[i] = new Rect(b.X, b.Y, b.Width, b.Height);
            }
            monitors = list;
            System.Drawing.Rectangle v = System.Windows.Forms.SystemInformation.VirtualScreen;
            virt = new Rect(v.X, v.Y, v.Width, v.Height);
        }

        // ------------------------------------------------------------------ original wallpaper bookkeeping

        static bool IsOurFile(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                path.StartsWith(Settings.ImageDir, StringComparison.OrdinalIgnoreCase);
        }

        static string ReadDesktopValue(string name, string fallback)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                return k == null ? fallback : Convert.ToString(k.GetValue(name, fallback));
        }

        static void WriteDesktopValue(string name, string value)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true))
                if (k != null) k.SetValue(name, value, RegistryValueKind.String);
        }

        /// <summary>
        /// If a previous run died while our image was live, the saved original is still valid; otherwise
        /// whatever is on the desktop right now is the original.
        /// </summary>
        void RecoverOrKeepOriginal()
        {
            string cur = Wallpaper.CurrentPath();
            if (settings.Active && IsOurFile(cur) && File.Exists(settings.OrigPath))
            {
                origPath = settings.OrigPath;
                int.TryParse(settings.OrigStyle, out origStyle);
                origTile = settings.OrigTile == "1";
                return;
            }
            CaptureOriginal(cur);
        }

        void CaptureOriginal(string path)
        {
            origPath = path;
            if (!int.TryParse(ReadDesktopValue("WallpaperStyle", "10"), out origStyle)) origStyle = 10;
            origTile = ReadDesktopValue("TileWallpaper", "0") == "1";
            settings.OrigPath = origPath;
            settings.OrigStyle = origStyle.ToString();
            settings.OrigTile = origTile ? "1" : "0";
        }

        void Restore()
        {
            lock (applyLock)
            {
                generation++;   // anything still being rendered must not overwrite the restored wallpaper
                lock (gate) { latest = null; }
                if (!applied && !settings.Active) return;
                if (!string.IsNullOrEmpty(origPath) && File.Exists(origPath))
                {
                    WriteDesktopValue("WallpaperStyle", origStyle.ToString());
                    WriteDesktopValue("TileWallpaper", origTile ? "1" : "0");
                    Native.SetWallpaperPath(0x14, 0, origPath, 0x3);
                }
                applied = false;
            }
            settings.Active = false;
            settings.Save();
        }

        // ------------------------------------------------------------------ requests (UI thread)

        /// <summary>Called whenever any setting changes; cheap, the real work happens on the render thread.</summary>
        public void SettingsChanged(bool unused)
        {
            if (!settings.Enabled)
            {
                Safe(Restore);
                return;
            }
            Request();
        }

        void Request()
        {
            if (!settings.Enabled) return;
            Job j = new Job();
            j.S = settings.Clone();
            j.Cells = cells;
            j.OrigPath = origPath;
            j.Style = origStyle;
            j.Tile = origTile;
            j.Virt = virt;
            j.Monitors = monitors;
            j.Generation = generation;
            lock (gate) { latest = j; }
            wake.Set();
        }

        void Tick()
        {
            tick++;
            double now = clock.Elapsed.TotalSeconds;

            // The user (or a slideshow) picked a different wallpaper: that becomes the new original.
            string cur = Wallpaper.CurrentPath();
            if (!IsOurFile(cur) && !string.IsNullOrEmpty(cur) &&
                !string.Equals(cur, origPath, StringComparison.OrdinalIgnoreCase))
            {
                CaptureOriginal(cur);
                applied = false;
                pending = true;
                changedAt = now;
            }

            if (!settings.Enabled) return;

            if (tick % 8 == 1 || shell.ListView == IntPtr.Zero)
            {
                shell.Locate();
                icons.Bind(shell.ListView);
            }

            List<Rect> nowCells = settings.TrackIcons ? icons.Read() : new List<Rect>();
            string sig = Signature(nowCells);
            if (sig != lastSig)
            {
                lastSig = sig;
                cells = nowCells;
                pending = true;
                changedAt = now;
            }

            if (pending && now - changedAt >= Debounce)
            {
                pending = false;
                Request();
            }
        }

        static string Signature(List<Rect> list)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Rect r in list)
                sb.Append((int)r.X).Append(',').Append((int)r.Y).Append(',').Append((int)r.Width).Append(',').Append((int)r.Height).Append(';');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ render thread

        void Work()
        {
            while (true)
            {
                wake.WaitOne();
                if (stopping) return;

                Job j;
                lock (gate) { j = latest; latest = null; }
                if (j == null) continue;

                try { Process(j); }
                catch (Exception ex) { Settings.Log(ex.ToString()); }

                Thread.Sleep(50);   // give Explorer a breather between wallpaper changes
            }
        }

        /// <summary>Draws the finished frame (blur, shadow, rim) without touching the desktop; also used to render previews.</summary>
        internal byte[] RenderFrame(Job j, out int w, out int h)
        {
            w = 0; h = 0;
            if (string.IsNullOrEmpty(j.OrigPath) || !File.Exists(j.OrigPath)) return null;
            Settings s = j.S;

            string key = j.OrigPath + "|" + File.GetLastWriteTimeUtc(j.OrigPath).Ticks + "|" + j.Style + "|" + j.Tile + "|" +
                j.Virt + "|" + s.Blur.ToString("R") + "|" + s.Brightness.ToString("R") + "|" + s.WaterTint.ToString("R");
            if (layers == null || key != layersKey)
            {
                layers = Wallpaper.Build(j.OrigPath, j.Style, j.Tile, j.Virt, j.Monitors, s.Blur, s.Brightness, s.WaterTint);
                layersKey = key;
            }

            int mw = layers.BlurWidth, mh = layers.BlurHeight;
            if (mask == null || mask.Width != mw || mask.Height != mh) mask = new MaskBuilder(mw, mh);

            double scale = Wallpaper.Scale;
            List<Shape> shapes = BuildShapes(j.Cells, s, j.Virt, scale);

            double feather = Math.Max(2, s.Feather);
            const float clamp = 36f;
            double contourSigma = s.Contour > 0 ? s.Contour / scale * 0.43 : 0;
            int[] radii = Blur.Radii(contourSigma, 3);
            float extent = clamp + (radii[0] + radii[1] + radii[2]) + 4;
            float shadowExtent = (float)((s.ShadowSize + s.ShadowOffset + feather) / scale) + 4;
            double tension = Math.Max(0, Math.Min(1, s.Tension));
            float neck = (float)Math.Max(1, (s.Smooth + (1 - tension) * 60) / scale);
            mask.Build(shapes, neck, Math.Max(extent, shadowExtent));
            mask.Round(contourSigma, Math.Max(clamp, shadowExtent));

            w = layers.Width; h = layers.Height;
            byte[] px = (byte[])layers.SharpPx.Clone();
            if (shapes.Count > 0) Composite(px, w, h, feather, s);
            return px;
        }

        void Process(Job j)
        {
            int w, h;
            byte[] px = RenderFrame(j, out w, out h);
            if (px == null) return;

            BitmapSource result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, w * 4);

            // Alternate file names so Explorer always sees a new path and fades to it.
            flip ^= 1;
            string file = Path.Combine(Settings.ImageDir, "wallpaper_" + flip + ".bmp");
            BmpBitmapEncoder enc = new BmpBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(result, PixelFormats.Bgr24, null, 0)));
            using (FileStream fs = File.Create(file)) enc.Save(fs);

            lock (applyLock)
            {
                if (j.Generation != generation || stopping) return;   // restored or shut down meanwhile
                if (!applied)
                {
                    settings.Active = true;   // must be on record before our image goes live
                    ui.BeginInvoke(new Action(delegate { settings.Save(); }));
                    applied = true;
                }
                WriteDesktopValue("WallpaperStyle", j.Monitors.Length > 1 ? "22" : "10");
                WriteDesktopValue("TileWallpaper", "0");
                Native.SetWallpaperPath(0x14, 0, file, 0x3);
            }
        }

        // ------------------------------------------------------------------ shapes

        /// <summary>Spacing of a list of coordinates: smallest gap between distinct values (0 if only one).</summary>
        static double Pitch(List<double> values)
        {
            values.Sort();
            double best = 0, prev = double.NaN;
            foreach (double v in values)
            {
                if (!double.IsNaN(prev) && v - prev > 8 && (best == 0 || v - prev < best)) best = v - prev;
                if (double.IsNaN(prev) || v - prev > 8) prev = v;
            }
            return best;
        }

        /// <summary>
        /// Turns icon cells into rounded rectangles (mask pixels). Icons that sit on a regular grid are
        /// stretched to meet their neighbours, so a block of icons becomes one solid block with straight
        /// edges instead of a string of separate bumps; free-floating icons keep their own padded box.
        /// </summary>
        static List<Shape> BuildShapes(List<Rect> list, Settings st, Rect virt, double s)
        {
            double pad = st.Padding, rad = st.Radius;
            double overlap = rad + 1;
            double e = Math.Max(0, Math.Min(1, st.Tension));   // 1 = pool, 0 = droplets
            const double dropScale = 0.64;                      // a droplet is smaller than its icon cell
            List<Shape> shapes = new List<Shape>(list.Count);
            if (list.Count == 0) return shapes;

            List<double> xs = new List<double>(), ys = new List<double>();
            foreach (Rect r in list) { xs.Add(r.X + r.Width / 2); ys.Add(r.Y); }
            double minX = double.MaxValue, minY = double.MaxValue;
            foreach (double v in xs) minX = Math.Min(minX, v);
            foreach (double v in ys) minY = Math.Min(minY, v);
            double dx = Pitch(new List<double>(xs)), dy = Pitch(new List<double>(ys));

            int n = list.Count;
            int[] col = new int[n], row = new int[n];
            bool[] onGrid = new bool[n];
            Dictionary<long, int> at = new Dictionary<long, int>();
            Dictionary<int, double> colW = new Dictionary<int, double>(), rowH = new Dictionary<int, double>();

            for (int i = 0; i < n; i++)
            {
                double fx = dx > 0 ? (xs[i] - minX) / dx : 0, fy = dy > 0 ? (ys[i] - minY) / dy : 0;
                col[i] = (int)Math.Round(fx); row[i] = (int)Math.Round(fy);
                onGrid[i] = Math.Abs(fx - col[i]) < 0.2 && Math.Abs(fy - row[i]) < 0.2;
                if (!onGrid[i]) continue;
                at[Key(col[i], row[i])] = i;
                double w, h;
                colW[col[i]] = colW.TryGetValue(col[i], out w) ? Math.Max(w, list[i].Width) : list[i].Width;
                rowH[row[i]] = rowH.TryGetValue(row[i], out h) ? Math.Max(h, list[i].Height) : list[i].Height;
            }

            for (int i = 0; i < n; i++)
            {
                double cx = xs[i], cy = list[i].Y + list[i].Height / 2;
                double w = list[i].Width, h = list[i].Height;
                double l, r, t, b;
                double pl, pr, pt, pb;   // pool: neighbours stretched until they meet
                if (onGrid[i])
                {
                    w = colW[col[i]]; h = rowH[row[i]];
                    bool L = dx > 0 && at.ContainsKey(Key(col[i] - 1, row[i]));
                    bool R = dx > 0 && at.ContainsKey(Key(col[i] + 1, row[i]));
                    bool U = dy > 0 && at.ContainsKey(Key(col[i], row[i] - 1));
                    bool D = dy > 0 && at.ContainsKey(Key(col[i], row[i] + 1));
                    pl = cx - (L ? dx / 2 + overlap : w / 2 + pad);
                    pr = cx + (R ? dx / 2 + overlap : w / 2 + pad);
                    // Icons in a row share their top edge; only the labels below differ in height.
                    double top = list[i].Y, gap = Math.Max(0, dy - h) / 2;
                    pt = top - (U ? gap + overlap : pad);
                    pb = top + h + (D ? gap + overlap : pad);
                    cy = top + h / 2;
                }
                else
                {
                    pl = cx - w / 2 - pad; pr = cx + w / 2 + pad;
                    pt = cy - h / 2 - pad; pb = cy + h / 2 + pad;
                }
                // droplet: its own smaller blob around the same centre
                double hx2 = (w / 2 + pad) * dropScale, hy2 = (h / 2 + pad) * dropScale;
                double dl = cx - hx2, dr = cx + hx2, dt = cy - hy2, db = cy + hy2;
                l = dl + (pl - dl) * e; r = dr + (pr - dr) * e;
                t = dt + (pt - dt) * e; b = db + (pb - db) * e;
                Shape sh = new Shape();
                sh.Cx = (float)(((l + r) / 2 - virt.X) / s);
                sh.Cy = (float)(((t + b) / 2 - virt.Y) / s);
                sh.Hx = (float)((r - l) / 2 / s);
                sh.Hy = (float)((b - t) / 2 / s);
                float radMask = (float)(rad / s);
                float dropMask = 0.9f * Math.Min(sh.Hx, sh.Hy);      // droplets are nearly round
                float radius = radMask + (dropMask - radMask) * (float)(1 - e);
                if (radius < radMask) radius = radMask;
                sh.Radius = Math.Min(radius, Math.Min(sh.Hx, sh.Hy));
                shapes.Add(sh);
            }
            return shapes;
        }

        static long Key(int col, int row)
        {
            return ((long)(col + 1000) << 24) | (long)(row + 1000);
        }

        // ------------------------------------------------------------------ compositing

        static float Smooth01(float t)
        {
            t = t < 0 ? 0 : (t > 1 ? 1 : t);
            return t * t * (3 - 2 * t);
        }

        /// <summary>Bilinear sample of the distance field; Far when any tap has no data.</summary>
        static float SampleField(float[] field, int fw, int fh, float fx, float fy)
        {
            int ix = (int)Math.Floor(fx), iy = (int)Math.Floor(fy);
            float tx = fx - ix, ty = fy - iy;
            int x0 = ix < 0 ? 0 : (ix > fw - 1 ? fw - 1 : ix);
            int x1 = ix + 1 < 0 ? 0 : (ix + 1 > fw - 1 ? fw - 1 : ix + 1);
            int y0 = iy < 0 ? 0 : (iy > fh - 1 ? fh - 1 : iy);
            int y1 = iy + 1 < 0 ? 0 : (iy + 1 > fh - 1 ? fh - 1 : iy + 1);
            float d00 = field[y0 * fw + x0], d10 = field[y0 * fw + x1];
            float d01 = field[y1 * fw + x0], d11 = field[y1 * fw + x1];
            if (d00 >= MaskBuilder.Far || d10 >= MaskBuilder.Far || d01 >= MaskBuilder.Far || d11 >= MaskBuilder.Far)
                return MaskBuilder.Far;
            return (d00 * (1 - tx) + d10 * tx) * (1 - ty) + (d01 * (1 - tx) + d11 * tx) * ty;
        }

        static float SampleBlurChannel(byte[] blur, int bw, int bh, float fx, float fy, int c)
        {
            int ix = (int)Math.Floor(fx), iy = (int)Math.Floor(fy);
            float tx = fx - ix, ty = fy - iy;
            int x0 = ix < 0 ? 0 : (ix > bw - 1 ? bw - 1 : ix);
            int x1 = ix + 1 < 0 ? 0 : (ix + 1 > bw - 1 ? bw - 1 : ix + 1);
            int y0 = iy < 0 ? 0 : (iy > bh - 1 ? bh - 1 : iy);
            int y1 = iy + 1 < 0 ? 0 : (iy + 1 > bh - 1 ? bh - 1 : iy + 1);
            float p00 = blur[(y0 * bw + x0) * 4 + c], p10 = blur[(y0 * bw + x1) * 4 + c];
            float p01 = blur[(y1 * bw + x0) * 4 + c], p11 = blur[(y1 * bw + x1) * 4 + c];
            return (p00 * (1 - tx) + p10 * tx) * (1 - ty) + (p01 * (1 - tx) + p11 * tx) * ty;
        }

        /// <summary>
        /// Draws the region onto the sharp wallpaper at full resolution: a soft drop shadow, then the
        /// blurred copy (bent a little near the edge, like water), then a lit rim - bright on the edges
        /// that face the light in the upper left, a touch darker on the far side.
        /// </summary>
        void Composite(byte[] px, int w, int h, double featherPx, Settings st)
        {
            float scale = Wallpaper.Scale;
            float half = (float)(featherPx / 2);
            float opacity = (float)st.ShadowOpacity;
            float size = (float)Math.Max(1, st.ShadowSize);
            float offY = (float)st.ShadowOffset;
            float rim = (float)Math.Max(0, Math.Min(1, st.RimLight));
            float refr = (float)Math.Max(0, st.Refraction);
            const float rimW = 7f, refrW = 22f;
            const float lx = -0.55f, ly = -0.835f;   // direction towards the light (upper left)
            float blurReach = (half + 2) / scale;
            float shadowReach = (opacity > 0 ? size + offY + 2 : 0) / scale;
            float[] field = mask.Field;
            int fw = mask.Width, fh = mask.Height;
            byte[] blur = layers.BlurPx;
            int bw = layers.BlurWidth, bh = layers.BlurHeight;
            float edgeBand = Math.Max(rimW + 3, refrW);

            System.Threading.Tasks.Parallel.For(0, h, delegate(int y)
            {
                float fy = (y + 0.5f) / scale - 0.5f;

                for (int x = 0; x < w; x++)
                {
                    float fx = (x + 0.5f) / scale - 0.5f;
                    float d = SampleField(field, fw, fh, fx, fy);
                    if (d >= MaskBuilder.Far) continue;
                    float dpx = d * scale;

                    // Shadow: the same field read a few pixels higher, so it falls below the region.
                    float shade = 0;
                    if (opacity > 0 && d <= shadowReach)
                    {
                        float ds = SampleField(field, fw, fh, fx, fy - offY / scale);
                        if (ds < MaskBuilder.Far)
                        {
                            float t = 1 - Smooth01(ds * scale / size);
                            shade = opacity * t * t;
                        }
                    }

                    float alpha = d <= blurReach ? 1 - Smooth01((dpx + half) / (2 * half)) : 0;
                    if (alpha <= 0 && shade <= 0) continue;

                    // Edge normal from the distance field gives refraction and the lit rim.
                    float sx = fx, sy = fy, hi = 0, lo = 0;
                    float depth = -dpx;
                    if (alpha > 0 && (rim > 0 || refr > 0) && depth < edgeBand)
                    {
                        float gx = SampleField(field, fw, fh, fx + 1, fy) - SampleField(field, fw, fh, fx - 1, fy);
                        float gy = SampleField(field, fw, fh, fx, fy + 1) - SampleField(field, fw, fh, fx, fy - 1);
                        float len = (float)Math.Sqrt(gx * gx + gy * gy);
                        if (len > 1e-4f && len < 100f)
                        {
                            float nx = gx / len, ny = gy / len;          // points out of the region
                            float dn = depth < 0 ? 0 : depth;
                            if (refr > 0 && dn < refrW)
                            {
                                float k = 1 - Smooth01(dn / refrW);
                                float shift = refr * k * k / scale;
                                sx = fx + nx * shift; sy = fy + ny * shift;
                            }
                            if (rim > 0 && dn < rimW + 3)
                            {
                                float dl = nx * lx + ny * ly;
                                float lit = dl > 0 ? dl : 0, dark = dl < 0 ? -dl : 0;
                                float prof = 1 - Smooth01(dn / rimW);
                                float spec = lit * lit * lit * (float)Math.Exp(-((dn - 1.2f) * (dn - 1.2f)) / 2.4f);
                                hi = rim * (0.45f * lit * prof + 0.85f * spec);
                                if (hi > 1) hi = 1;
                                lo = rim * 0.30f * dark * prof;
                            }
                        }
                    }

                    int o = (y * w + x) * 4;
                    for (int c = 0; c < 3; c++)
                    {
                        float baseV = px[o + c] * (1 - shade);
                        float v = baseV;
                        if (alpha > 0)
                        {
                            float bl = SampleBlurChannel(blur, bw, bh, sx, sy, c);
                            v = baseV + (bl - baseV) * alpha;
                            v = v + (255 - v) * hi * alpha;
                            v = v * (1 - lo * alpha);
                        }
                        px[o + c] = (byte)(v < 0 ? 0 : (v > 255 ? 255 : v + 0.5f));
                    }
                }
            });
        }
    }
}