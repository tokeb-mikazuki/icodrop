using System;
using System.Globalization;
using System.IO;

namespace WallBlur
{
    /// <summary>User-tunable options, stored as key=value lines in %APPDATA%\WallBlur\settings.txt.</summary>
    class Settings
    {
        public bool Enabled = true;
        public bool TrackIcons = true;
        public bool TrackWindows = true;

        /// <summary>Gaussian sigma of the background blur, in screen pixels.</summary>
        public double Blur = 14;
        /// <summary>Margin added around every icon / window before blurring, in screen pixels.</summary>
        public double Padding = 16;
        /// <summary>Corner radius of the blurred region, in screen pixels.</summary>
        public double Radius = 18;
        /// <summary>How strongly neighbouring shapes melt into one another, in screen pixels.</summary>
        public double Smooth = 4;
        /// <summary>Brightness change of the blurred area: -0.5 (darker) .. +0.5 (brighter).</summary>
        public double Brightness = 0.08;
        /// <summary>Width of the soft transition at the blur border, in screen pixels.</summary>
        public double Feather = 2;
        /// <summary>How strongly the outline is smoothed into one flowing curve, in screen pixels (0 = off).</summary>
        public double Contour = 40;
        /// <summary>Strength of the drop shadow around the blurred region, 0 = off.</summary>
        public double ShadowOpacity = 0.14;
        /// <summary>How far the shadow spreads, in screen pixels.</summary>
        public double ShadowSize = 30;
        /// <summary>How far the shadow is shifted downwards, in screen pixels.</summary>
        public double ShadowOffset = 6;
        /// <summary>0 = separate droplets joined by thin necks, 1 = one solid pool.</summary>
        public double Tension = 1.0;
        /// <summary>Strength of the lit/shaded rim along the edge, 0..1.</summary>
        public double RimLight = 0.45;
        /// <summary>How far the backdrop is bent near the edge, in screen pixels.</summary>
        public double Refraction = 6;
        /// <summary>Cool, clear-water colour cast inside the region, 0..0.5.</summary>
        public double WaterTint = 0.05;
        /// <summary>Last chosen style preset (0 pool, 1 droplets, 2 glass); -1 once a slider was moved by hand.</summary>
        public int Preset = 0;

        // Recovery data: set while our generated image is the live wallpaper, so a crash can be undone.
        public bool Active = false;
        /// <summary>True once the first-run "start with Windows" default has been applied.</summary>
        public bool AutoStartInit = false;
        public string OrigPath = "";
        public string OrigStyle = "10";
        public string OrigTile = "0";

        /// <summary>Copy handed to the render thread so it never sees half-edited values.</summary>
        public Settings Clone()
        {
            return (Settings)MemberwiseClone();
        }

        public static string DataDir
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WallBlur");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>
        /// Where generated wallpapers live. Explorer refuses to load wallpapers from some folders under
        /// AppData (it shows black), so this is a normal folder under Pictures.
        /// </summary>
        public static string ImageDir
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "WallBlur");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        static string FilePath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WallBlur");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "settings.txt");
            }
        }

        public static string LogPath
        {
            get { return Path.Combine(Path.GetDirectoryName(FilePath), "log.txt"); }
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    double d;
                    bool isNum = double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
                    switch (k)
                    {
                        case "Enabled": s.Enabled = v == "1"; break;
                        case "TrackIcons": s.TrackIcons = v == "1"; break;
                        case "TrackWindows": s.TrackWindows = v == "1"; break;
                        case "Blur": if (isNum) s.Blur = d; break;
                        case "Padding": if (isNum) s.Padding = d; break;
                        case "Radius": if (isNum) s.Radius = d; break;
                        case "Smooth": if (isNum) s.Smooth = d; break;
                        case "Brightness": if (isNum) s.Brightness = d; break;
                        case "Feather": if (isNum) s.Feather = d; break;
                        case "Contour": if (isNum) s.Contour = d; break;
                        case "ShadowOpacity": if (isNum) s.ShadowOpacity = d; break;
                        case "ShadowSize": if (isNum) s.ShadowSize = d; break;
                        case "ShadowOffset": if (isNum) s.ShadowOffset = d; break;
                        case "Tension": if (isNum) s.Tension = d; break;
                        case "RimLight": if (isNum) s.RimLight = d; break;
                        case "Refraction": if (isNum) s.Refraction = d; break;
                        case "WaterTint": if (isNum) s.WaterTint = d; break;
                        case "Preset": if (isNum) s.Preset = (int)d; break;
                        case "Active": s.Active = v == "1"; break;
                        case "AutoStartInit": s.AutoStartInit = v == "1"; break;
                        case "OrigPath": s.OrigPath = v; break;
                        case "OrigStyle": s.OrigStyle = v; break;
                        case "OrigTile": s.OrigTile = v; break;
                    }
                }
            }
            catch (Exception) { }
            return s;
        }

        public void Save()
        {
            try
            {
                CultureInfo c = CultureInfo.InvariantCulture;
                File.WriteAllLines(FilePath, new string[]
                {
                    "Enabled=" + (Enabled ? "1" : "0"),
                    "TrackIcons=" + (TrackIcons ? "1" : "0"),
                    "TrackWindows=" + (TrackWindows ? "1" : "0"),
                    "Blur=" + Blur.ToString(c),
                    "Padding=" + Padding.ToString(c),
                    "Radius=" + Radius.ToString(c),
                    "Smooth=" + Smooth.ToString(c),
                    "Brightness=" + Brightness.ToString(c),
                    "Feather=" + Feather.ToString(c),
                    "Contour=" + Contour.ToString(c),
                    "ShadowOpacity=" + ShadowOpacity.ToString(c),
                    "ShadowSize=" + ShadowSize.ToString(c),
                    "ShadowOffset=" + ShadowOffset.ToString(c),
                    "Tension=" + Tension.ToString(c),
                    "RimLight=" + RimLight.ToString(c),
                    "Refraction=" + Refraction.ToString(c),
                    "WaterTint=" + WaterTint.ToString(c),
                    "Preset=" + Preset.ToString(c),
                    "Active=" + (Active ? "1" : "0"),
                    "AutoStartInit=" + (AutoStartInit ? "1" : "0"),
                    "OrigPath=" + OrigPath,
                    "OrigStyle=" + OrigStyle,
                    "OrigTile=" + OrigTile
                });
            }
            catch (Exception) { }
        }

        public static void Log(string text)
        {
            try { File.AppendAllText(LogPath, DateTime.Now.ToString("s") + " " + text + Environment.NewLine); }
            catch (Exception) { }
        }
    }
}
