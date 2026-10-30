using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallBlur;

// Renders the README preview images without touching the real desktop.
//
// It runs icodrop's own drawing code (BlurEngine.RenderFrame) on a sample wallpaper with an invented grid
// of icons, then paints real application icons and their labels on top, the way Explorer would.
//
//   icodrop-preview.exe <wallpaper> <icons.txt> <outDir>
//
// icons.txt: one "Label|path" per line; a path is an .exe, or a shell parsing name such as shell:::{GUID}.
static class PreviewTool
{
    const int W = 2560, H = 1600;
    const double IconSize = 62, CellW = 114, CellH = 104, PitchX = 115, PitchY = 147;

    // ---- shell icons ----------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    struct SIZE { public int cx, cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr hbitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

    static BitmapSource LoadIcon(string path)
    {
        IShellItemImageFactory f;
        SHCreateItemFromParsingName(path, IntPtr.Zero, new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), out f);
        SIZE size = new SIZE(); size.cx = 256; size.cy = 256;
        IntPtr hbm;
        int hr = f.GetImage(size, 0x4 | 0x1, out hbm);      // ICONONLY | BIGGERSIZEOK
        if (hr != 0) throw new Exception("GetImage failed 0x" + hr.ToString("x"));
        try
        {
            BitmapSource bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            FormatConvertedBitmap conv = new FormatConvertedBitmap(bs, PixelFormats.Pbgra32, null, 0);
            conv.Freeze();
            return conv;
        }
        finally { DeleteObject(hbm); }
    }

    // ---- the scene ------------------------------------------------------------------------------
    class Entry { public string Label; public BitmapSource Icon; public Rect Cell; }

    static List<Entry> BuildScene(string manifest)
    {
        List<Entry> list = new List<Entry>();
        foreach (string line in File.ReadAllLines(manifest))
        {
            if (line.Trim().Length == 0 || line.StartsWith("#")) continue;
            int bar = line.IndexOf('|');
            Entry e = new Entry();
            e.Label = line.Substring(0, bar).Trim();
            e.Icon = LoadIcon(Environment.ExpandEnvironmentVariables(line.Substring(bar + 1).Trim()));
            list.Add(e);
        }

        // Left block: 3 columns, the last row only half full. Right edge: one column of three.
        double x0 = 30, y0 = 34;
        int[] colOf = new int[list.Count], rowOf = new int[list.Count];
        int i = 0;
        for (int r = 0; r < 5 && i < list.Count; r++)
            for (int c = 0; c < 3 && i < list.Count; c++)
            {
                if (r == 4 && c == 2) break;
                list[i].Cell = new Rect(x0 + c * PitchX, y0 + r * PitchY, CellW, CellH);
                i++;
            }
        for (int r = 0; i < list.Count; r++, i++)
            list[i].Cell = new Rect(W - 30 - CellW, y0 + r * PitchY, CellW, CellH);
        return list;
    }

    static void DrawLabel(DrawingContext dc, string text, Rect cell)
    {
        Typeface face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double top = cell.Y + 12 + IconSize + 7;
        FormattedText ft = new FormattedText(text, CultureInfo.GetCultureInfo("en-US"), FlowDirection.LeftToRight,
            face, 16.5, Brushes.White, 1.0);
        ft.MaxTextWidth = CellW - 6; ft.MaxLineCount = 2; ft.TextAlignment = TextAlignment.Center;
        ft.Trimming = TextTrimming.CharacterEllipsis;
        Point p = new Point(cell.X + 3, top);

        // Explorer's soft dark halo behind desktop labels
        FormattedText shadow = new FormattedText(text, CultureInfo.GetCultureInfo("en-US"), FlowDirection.LeftToRight,
            face, 16.5, new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), 1.0);
        shadow.MaxTextWidth = ft.MaxTextWidth; shadow.MaxLineCount = 2; shadow.TextAlignment = TextAlignment.Center;
        shadow.Trimming = TextTrimming.CharacterEllipsis;
        double[][] offs = new double[][] { new double[] { 0, 1.5 }, new double[] { 1.2, 1.2 }, new double[] { -1.2, 1.2 }, new double[] { 0, 2.5 } };
        foreach (double[] o in offs) dc.DrawText(shadow, new Point(p.X + o[0], p.Y + o[1]));
        dc.DrawText(ft, p);
    }

    static BitmapSource Compose(BitmapSource background, List<Entry> scene)
    {
        DrawingVisual dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
        using (DrawingContext dc = dv.RenderOpen())
        {
            dc.DrawImage(background, new Rect(0, 0, W, H));
            foreach (Entry e in scene)
            {
                double ix = e.Cell.X + (e.Cell.Width - IconSize) / 2, iy = e.Cell.Y + 12;
                // a faint shadow under each icon, as on the real desktop
                dc.PushOpacity(0.35);
                dc.DrawEllipse(Brushes.Black, null, new Point(ix + IconSize / 2, iy + IconSize - 2), IconSize * 0.34, 4);
                dc.Pop();
                dc.DrawImage(e.Icon, new Rect(ix, iy, IconSize, IconSize));
                DrawLabel(dc, e.Label, e.Cell);
            }
        }
        RenderTargetBitmap rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    static void SaveJpeg(BitmapSource src, string path, int width, int quality)
    {
        double k = (double)width / src.PixelWidth;
        TransformedBitmap small = new TransformedBitmap(src, new ScaleTransform(k, k));
        JpegBitmapEncoder enc = new JpegBitmapEncoder();
        enc.QualityLevel = quality;
        enc.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(small, PixelFormats.Bgr24, null, 0)));
        using (FileStream fs = File.Create(path)) enc.Save(fs);
    }

    // ---- presets: the same values the settings window applies -----------------------------------
    static Settings Preset(int index)
    {
        Settings s = new Settings();      // blur 14, shadow 14 %, brightness +8 % ... the shipped defaults
        switch (index)
        {
            case 0: s.Tension = 1.0; s.RimLight = 0.45; s.Refraction = 6; s.WaterTint = 0.05; s.Padding = 16; s.Radius = 18; s.Contour = 40; break;
            case 1: s.Tension = 0.2; s.RimLight = 0.6; s.Refraction = 10; s.WaterTint = 0.08; s.Padding = 10; s.Radius = 30; s.Contour = 40; break;
            case 2: s.Tension = 1.0; s.RimLight = 0.85; s.Refraction = 12; s.WaterTint = 0.0; s.Padding = 16; s.Radius = 22; s.Contour = 40; break;
        }
        s.Preset = index;
        return s;
    }

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length < 3) { Console.WriteLine("usage: icodrop-preview <wallpaper> <icons.txt> <outDir>"); return 1; }
        string wallpaper = Path.GetFullPath(args[0]), manifest = args[1], outDir = args[2];
        Directory.CreateDirectory(outDir);

        List<Entry> scene = BuildScene(manifest);
        List<Rect> cells = new List<Rect>();
        foreach (Entry e in scene) cells.Add(e.Cell);
        Console.WriteLine("icons: " + scene.Count);

        string[] names = new string[] { "surface", "drops", "glass" };
        Rect screen = new Rect(0, 0, W, H);

        // before: the wallpaper as it is, icons straight on top
        Settings plain = new Settings();
        BlurEngine engine = new BlurEngine(plain);
        BlurEngine.Job job = new BlurEngine.Job();
        job.S = plain; job.Cells = new List<Rect>(); job.OrigPath = wallpaper; job.Style = 10; job.Tile = false;
        job.Virt = screen; job.Monitors = new Rect[] { screen };
        int w, h;
        byte[] px = engine.RenderFrame(job, out w, out h);
        if (px == null) { Console.WriteLine("render failed"); return 2; }
        BitmapSource sharp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, w * 4);
        SaveJpeg(Compose(sharp, scene), Path.Combine(outDir, "preview-before.jpg"), 2000, 90);
        Console.WriteLine("wrote preview-before.jpg");

        for (int p = 0; p < names.Length; p++)
        {
            BlurEngine.Job j = new BlurEngine.Job();
            j.S = Preset(p); j.Cells = cells; j.OrigPath = wallpaper; j.Style = 10; j.Tile = false;
            j.Virt = screen; j.Monitors = new Rect[] { screen };
            byte[] frame = engine.RenderFrame(j, out w, out h);
            BitmapSource bg = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, frame, w * 4);
            string file = "preview-" + names[p] + ".jpg";
            SaveJpeg(Compose(bg, scene), Path.Combine(outDir, file), 2000, 90);
            Console.WriteLine("wrote " + file);
        }
        return 0;
    }
}
