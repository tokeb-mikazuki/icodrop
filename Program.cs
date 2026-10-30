using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("icodrop-preview")]

namespace WallBlur
{
    static class Program
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string AppName = "icodrop";
        const string OldAppName = "WallBlur";

        static Settings settings;
        static BlurEngine host;
        static NotifyIcon tray;
        static SettingsWindow settingsWindow;
        static Icon appIcon;
        static System.Windows.Application app;

        public static Icon AppIcon
        {
            get { if (appIcon == null) appIcon = LoadIcon(32); return appIcon; }
        }

        [STAThread]
        static void Main(string[] args)
        {
            bool background = Array.IndexOf(args, "--background") >= 0;

            // "icodrop.exe --quit": ask the running copy to exit cleanly (restoring the wallpaper), then leave.
            if (Array.IndexOf(args, "--quit") >= 0)
            {
                try { EventWaitHandle.OpenExisting("WallBlur.Quit").Set(); }
                catch (Exception) { }
                return;
            }

            bool created;
            using (Mutex mutex = new Mutex(true, "WallBlur.SingleInstance", out created))
            {
                if (!created)
                {
                    // Already running: ask that instance to bring its settings window forward.
                    try { EventWaitHandle.OpenExisting("WallBlur.ShowSettings").Set(); }
                    catch (Exception) { }
                    return;
                }

                EventWaitHandle showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "WallBlur.ShowSettings");
                EventWaitHandle quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "WallBlur.Quit");

                app = new System.Windows.Application();
                app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                app.DispatcherUnhandledException += delegate(object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                {
                    Settings.Log(e.Exception.ToString());
                    e.Handled = true;
                };

                Thread listener = new Thread(delegate()
                {
                    while (true)
                    {
                        int which = WaitHandle.WaitAny(new WaitHandle[] { showSignal, quitSignal });
                        if (which == 0) app.Dispatcher.BeginInvoke(new Action(ShowSettings));
                        else app.Dispatcher.BeginInvoke(new Action(delegate { app.Shutdown(); }));
                    }
                });
                listener.IsBackground = true;
                listener.Start();

                MigrateAutoStart();
                settings = Settings.Load();
                // Start with Windows by default: switched on the first time only, so turning it off sticks.
                if (!settings.AutoStartInit)
                {
                    SetAutoStart(true);
                    settings.AutoStartInit = true;
                    settings.Save();
                }
                else if (IsAutoStart()) SetAutoStart(true);   // keep the registered path current if the exe moved
                host = new BlurEngine(settings);
                host.Start();
                BuildTray();
                if (!background) ShowSettings();

                app.Run();

                tray.Visible = false;
                tray.Dispose();
                if (settingsWindow != null) { settingsWindow.Exiting = true; settingsWindow.Close(); }
                host.Stop();
            }
        }

        // ------------------------------------------------------------------ tray

        static void BuildTray()
        {
            ContextMenuStrip menu = new ContextMenuStrip();

            ToolStripMenuItem open = new ToolStripMenuItem("설정 열기...");
            open.Font = new Font(open.Font, FontStyle.Bold);
            open.Click += delegate { ShowSettings(); };
            menu.Items.Add(open);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem enabled = new ToolStripMenuItem("블러 사용");
            enabled.Click += delegate { settings.Enabled = !settings.Enabled; Apply(false); };
            menu.Items.Add(enabled);

            ToolStripMenuItem auto = new ToolStripMenuItem("Windows 시작 시 자동 실행");
            auto.Click += delegate { SetAutoStart(!IsAutoStart()); };
            menu.Items.Add(auto);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exit = new ToolStripMenuItem("종료 (원래 배경으로 복원)");
            exit.Click += delegate { app.Shutdown(); };
            menu.Items.Add(exit);

            // The settings window can change these too, so refresh the check marks whenever the menu opens.
            menu.Opening += delegate
            {
                enabled.Checked = settings.Enabled;
                auto.Checked = IsAutoStart();
            };

            tray = new NotifyIcon();
            tray.Icon = LoadIcon(SystemInformation.SmallIconSize.Width);
            tray.Text = "icodrop";
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowSettings(); };
            tray.Visible = true;
        }

        static void ShowSettings()
        {
            if (settingsWindow == null) settingsWindow = new SettingsWindow(settings, Apply, IsAutoStart, SetAutoStart);
            settingsWindow.Present();
        }

        static void Apply(bool relayer)
        {
            settings.Save();
            host.SettingsChanged(relayer);
        }

        /// <summary>Loads the embedded multi-size icon at the size that suits the caller.</summary>
        static Icon LoadIcon(int size)
        {
            try
            {
                using (System.IO.Stream s = typeof(Program).Assembly.GetManifestResourceStream("icodrop.ico"))
                    if (s != null) return new Icon(s, size, size);
            }
            catch (Exception) { }
            return MakeIcon();
        }

        static Icon MakeIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (LinearGradientBrush bg = new LinearGradientBrush(new Rectangle(0, 0, 32, 32),
                    Color.FromArgb(70, 110, 170), Color.FromArgb(150, 175, 215), 60f))
                    g.FillEllipse(bg, 1, 1, 30, 30);
                using (Brush tile = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                {
                    g.FillRectangle(tile, 7, 7, 7, 7);
                    g.FillRectangle(tile, 18, 7, 7, 7);
                    g.FillRectangle(tile, 7, 18, 7, 7);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        // ------------------------------------------------------------------ autostart

        /// <summary>Earlier builds registered themselves as "WallBlur"; carry that over to the new name.</summary>
        static void MigrateAutoStart()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (k == null || k.GetValue(OldAppName) == null) return;
                k.DeleteValue(OldAppName, false);
                k.SetValue(AppName, "\"" + Application.ExecutablePath + "\" --background");
            }
        }

        static bool IsAutoStart()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(AppName) != null;
        }

        static void SetAutoStart(bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (k == null) return;
                if (on) k.SetValue(AppName, "\"" + Application.ExecutablePath + "\" --background");
                else k.DeleteValue(AppName, false);
            }
        }
    }
}
