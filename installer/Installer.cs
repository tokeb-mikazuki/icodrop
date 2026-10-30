using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace IcodropSetup
{
    /// <summary>What the user (or the command line) asked the installer to do.</summary>
    class Options
    {
        public string Dir;
        public bool DesktopShortcut = true;
        public bool StartMenu = true;
        public bool AutoStart = true;
        public bool LaunchAfter = true;
        public bool RemoveData;
    }

    /// <summary>The real work: copying files, shortcuts, registry, uninstalling. No UI in here.</summary>
    static class Setup
    {
        public const string AppName = "icodrop";
        public const string Version = "1.0.0";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\icodrop";
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SystemParametersInfoW")]
        static extern bool SpiSet(uint action, uint param, string path, uint winIni);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SystemParametersInfoW")]
        static extern bool SpiGet(uint action, uint param, StringBuilder buf, uint winIni);

        public static string DefaultDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
            }
        }

        public static string DataDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WallBlur"); }
        }

        public static string ImageDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "WallBlur"); }
        }

        static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"); } }
        static string StartMenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"); } }

        /// <summary>Install folder of an existing installation, or null.</summary>
        public static string FindInstalled()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstallKey))
            {
                if (k == null) return null;
                string dir = k.GetValue("InstallLocation") as string;
                return !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : null;
            }
        }

        // ------------------------------------------------------------------ helpers

        static Stream Resource(string name)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        }

        static void Extract(string resource, string target)
        {
            using (Stream src = Resource(resource))
            using (FileStream dst = File.Create(target))
                src.CopyTo(dst);
        }

        static void CreateShortcut(string lnk, string target, string description)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(t);
            dynamic s = shell.CreateShortcut(lnk);
            s.TargetPath = target;
            s.WorkingDirectory = Path.GetDirectoryName(target);
            s.IconLocation = target + ",0";
            s.Description = description;
            s.Save();
        }

        static void DeleteQuietly(string file)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch (Exception) { }
        }

        /// <summary>Asks a running icodrop to quit through its own exit path (which restores the wallpaper).</summary>
        public static void StopRunning()
        {
            if (Process.GetProcessesByName("icodrop").Length == 0) return;

            string temp = Path.Combine(Path.GetTempPath(), "icodrop-quit-" + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                Extract("payload.exe", temp);
                Process q = Process.Start(new ProcessStartInfo(temp, "--quit") { UseShellExecute = false, CreateNoWindow = true });
                if (q != null) q.WaitForExit(5000);
            }
            catch (Exception) { }
            finally { DeleteQuietly(temp); }

            for (int i = 0; i < 100 && Process.GetProcessesByName("icodrop").Length > 0; i++) Thread.Sleep(100);
            foreach (Process p in Process.GetProcessesByName("icodrop"))   // an old build without --quit
            {
                try { p.Kill(); p.WaitForExit(2000); }
                catch (Exception) { }
            }
        }

        /// <summary>
        /// If a crash left our generated wallpaper on screen, put the user's own wallpaper back before
        /// the generated images are deleted.
        /// </summary>
        static void RestoreWallpaperIfOurs()
        {
            try
            {
                StringBuilder cur = new StringBuilder(520);
                SpiGet(0x73, 520, cur, 0);
                if (!cur.ToString().StartsWith(ImageDir, StringComparison.OrdinalIgnoreCase)) return;

                string settings = Path.Combine(DataDir, "settings.txt");
                if (!File.Exists(settings)) return;
                string orig = null, style = "10", tile = "0";
                foreach (string line in File.ReadAllLines(settings))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq), v = line.Substring(eq + 1).Trim();
                    if (k == "OrigPath") orig = v;
                    else if (k == "OrigStyle") style = v;
                    else if (k == "OrigTile") tile = v;
                }
                if (string.IsNullOrEmpty(orig) || !File.Exists(orig)) return;
                using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true))
                {
                    if (rk != null)
                    {
                        rk.SetValue("WallpaperStyle", style, RegistryValueKind.String);
                        rk.SetValue("TileWallpaper", tile, RegistryValueKind.String);
                    }
                }
                SpiSet(0x14, 0, orig, 3);
            }
            catch (Exception) { }
        }

        /// <summary>Records the autostart choice so the app does not apply its own first-run default over it.</summary>
        static void WriteAutoStart(bool on, string exe)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(AppName, "\"" + exe + "\" --background");
                else k.DeleteValue(AppName, false);
            }

            Directory.CreateDirectory(DataDir);
            string file = Path.Combine(DataDir, "settings.txt");
            List<string> lines = new List<string>();
            if (File.Exists(file))
                foreach (string l in File.ReadAllLines(file))
                    if (!l.StartsWith("AutoStartInit=")) lines.Add(l);
            lines.Add("AutoStartInit=1");
            File.WriteAllLines(file, lines.ToArray());
        }

        // ------------------------------------------------------------------ install

        public static void Install(Options o, Action<string, double> progress)
        {
            string exe = Path.Combine(o.Dir, "icodrop.exe");
            string uninstaller = Path.Combine(o.Dir, "uninstall.exe");

            progress("실행 중인 icodrop을 종료하는 중...", 0.05);
            StopRunning();

            progress("파일을 복사하는 중...", 0.25);
            Directory.CreateDirectory(o.Dir);
            Extract("payload.exe", exe);
            string self = Assembly.GetExecutingAssembly().Location;
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(uninstaller), StringComparison.OrdinalIgnoreCase))
                File.Copy(self, uninstaller, true);

            progress("바로가기를 만드는 중...", 0.5);
            if (o.DesktopShortcut) CreateShortcut(DesktopLink, exe, "icodrop - 배경과 아이콘을 더욱 조화롭게");
            else DeleteQuietly(DesktopLink);
            if (o.StartMenu) CreateShortcut(StartMenuLink, exe, "icodrop - 배경과 아이콘을 더욱 조화롭게");
            else DeleteQuietly(StartMenuLink);

            progress("Windows에 등록하는 중...", 0.7);
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", AppName);
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", AppName);
                k.SetValue("InstallLocation", o.Dir);
                k.SetValue("DisplayIcon", exe + ",0");
                k.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)((new FileInfo(exe).Length + new FileInfo(uninstaller).Length) / 1024), RegistryValueKind.DWord);
            }
            WriteAutoStart(o.AutoStart, exe);

            progress("마무리하는 중...", 0.95);
            if (o.LaunchAfter) Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = o.Dir });
            progress("완료", 1.0);
        }

        // ------------------------------------------------------------------ uninstall

        public static void Uninstall(Options o, Action<string, double> progress)
        {
            string dir = FindInstalled() ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            progress("실행 중인 icodrop을 종료하는 중...", 0.1);
            StopRunning();
            RestoreWallpaperIfOurs();

            progress("바로가기와 자동 실행 등록을 지우는 중...", 0.4);
            DeleteQuietly(DesktopLink);
            DeleteQuietly(StartMenuLink);
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                if (k != null) { k.DeleteValue(AppName, false); k.DeleteValue("WallBlur", false); }

            if (o.RemoveData)
            {
                progress("설정과 생성된 배경 이미지를 지우는 중...", 0.6);
                try { if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true); } catch (Exception) { }
                try { if (Directory.Exists(ImageDir)) Directory.Delete(ImageDir, true); } catch (Exception) { }
            }

            progress("프로그램을 지우는 중...", 0.8);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            DeleteQuietly(Path.Combine(dir, "icodrop.exe"));

            // This process is uninstall.exe itself, so a short-lived helper removes it once we have exited.
            string cmd = "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + dir + "\"";
            Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            progress("완료", 1.0);
        }
    }

    // ====================================================================== window

    class SetupWindow : Window
    {
        const string Styles = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='Primary' TargetType='Button'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='B' CornerRadius='10' Padding='26,10' Background='{DynamicResource AccentBrush}'>
            <TextBlock Text='{TemplateBinding Content}' Foreground='White' FontSize='14' FontWeight='SemiBold' HorizontalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='Opacity' Value='0.88'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter TargetName='B' Property='Opacity' Value='0.4'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='Secondary' TargetType='Button'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='B' CornerRadius='10' Padding='18,10' Background='Transparent'>
            <TextBlock Text='{TemplateBinding Content}' Foreground='{DynamicResource AccentBrush}' FontSize='14' HorizontalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='Background' Value='{DynamicResource HoverBrush}'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='Check' TargetType='CheckBox'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <StackPanel Orientation='Horizontal' Background='Transparent'>
            <Grid Width='22' Height='22'>
              <Border x:Name='Box' CornerRadius='11' BorderThickness='1.5' BorderBrush='{DynamicResource BoxBrush}' Background='Transparent'/>
              <Path x:Name='Tick' Data='M6,11.5 L9.5,15 L16,7.5' Stroke='White' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' Visibility='Collapsed'/>
            </Grid>
            <ContentPresenter Margin='12,0,0,0' VerticalAlignment='Center'/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Box' Property='Background' Value='{DynamicResource AccentBrush}'/>
              <Setter TargetName='Box' Property='BorderBrush' Value='{DynamicResource AccentBrush}'/>
              <Setter TargetName='Tick' Property='Visibility' Value='Visible'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='Bar' TargetType='ProgressBar'>
    <Setter Property='Height' Value='8'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ProgressBar'>
          <Grid>
            <Border x:Name='PART_Track' CornerRadius='4' Background='{DynamicResource TrackBrush}'/>
            <Border x:Name='PART_Indicator' CornerRadius='4' Background='{DynamicResource AccentBrush}' HorizontalAlignment='Left'/>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";

        readonly bool uninstall;
        readonly bool dark;
        readonly Options options = new Options();
        readonly StackPanel root = new StackPanel();
        TextBox pathBox;
        CheckBox cbDesktop, cbMenu, cbAuto, cbRun, cbData;
        ProgressBar bar;
        TextBlock status;
        string existing;

        public SetupWindow(bool uninstallMode)
        {
            uninstall = uninstallMode;
            dark = IsDark();
            existing = Setup.FindInstalled();

            Title = uninstall ? "icodrop 제거" : "icodrop 설치";
            Width = 480;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Malgun Gothic");
            FontSize = 13;
            UseLayoutRounding = true;
            Icon = LoadIcon();

            ResourceDictionary theme = new ResourceDictionary();
            theme["AccentBrush"] = Hex(dark ? "#0A84FF" : "#007AFF");
            theme["TextBrush"] = Hex(dark ? "#F5F5F7" : "#1D1D1F");
            theme["SubTextBrush"] = Hex(dark ? "#A1A1A6" : "#6E6E73");
            theme["CardBrush"] = Hex(dark ? "#2C2C2E" : "#FFFFFF");
            theme["TrackBrush"] = Hex(dark ? "#48484A" : "#E5E5EA");
            theme["BoxBrush"] = Hex(dark ? "#636366" : "#C7C7CC");
            theme["HoverBrush"] = Hex(dark ? "#1AFFFFFF" : "#12000000");
            Resources.MergedDictionaries.Add(theme);
            Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Styles));
            Background = Hex(dark ? "#1C1C1E" : "#F2F2F7");
            SetResourceReference(TextElement.ForegroundProperty, "TextBrush");

            root.Margin = new Thickness(28, 26, 28, 24);
            Content = root;
            ShowOptions();
        }

        // ------------------------------------------------------------------ small helpers

        static bool IsDark()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k != null && Convert.ToInt32(k.GetValue("AppsUseLightTheme", 1)) == 0;
            }
            catch (Exception) { return false; }
        }

        static Brush Hex(string hex)
        {
            Brush b = (Brush)new BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }

        static ImageSource LoadIcon()
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("icodrop.ico"))
                {
                    System.Windows.Media.Imaging.IconBitmapDecoder dec = new System.Windows.Media.Imaging.IconBitmapDecoder(
                        s, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    return dec.Frames[dec.Frames.Count - 1];
                }
            }
            catch (Exception) { return null; }
        }

        TextBlock Text(string text, double size, bool secondary, FontWeight? weight)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = size;
            t.TextWrapping = TextWrapping.Wrap;
            if (weight.HasValue) t.FontWeight = weight.Value;
            t.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "SubTextBrush" : "TextBrush");
            return t;
        }

        Border Card(UIElement child)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(12);
            b.Padding = new Thickness(18, 14, 18, 14);
            b.Margin = new Thickness(0, 0, 0, 14);
            b.SetResourceReference(Border.BackgroundProperty, "CardBrush");
            b.Child = child;
            return b;
        }

        CheckBox Check(string text, bool on)
        {
            CheckBox c = new CheckBox();
            c.Style = (Style)FindResource("Check");
            c.Content = Text(text, 14, false, null);
            c.IsChecked = on;
            c.Margin = new Thickness(0, 5, 0, 5);
            return c;
        }

        UIElement Header(string title, string subtitle)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 0, 0, 20);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());

            Image logo = new Image();
            logo.Source = LoadIcon();
            logo.Width = 56; logo.Height = 56;
            logo.Margin = new Thickness(0, 0, 14, 0);
            logo.VerticalAlignment = VerticalAlignment.Center;
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            g.Children.Add(logo);

            StackPanel s = new StackPanel();
            s.VerticalAlignment = VerticalAlignment.Center;
            s.Children.Add(Text(title, 24, false, FontWeights.SemiBold));
            s.Children.Add(Text(subtitle, 13, true, null));
            Grid.SetColumn(s, 1);
            g.Children.Add(s);
            return g;
        }

        Button MakeButton(string text, string style, RoutedEventHandler click)
        {
            Button b = new Button();
            b.Content = text;
            b.Style = (Style)FindResource(style);
            b.Click += click;
            return b;
        }

        UIElement ButtonRow(params Button[] buttons)
        {
            StackPanel p = new StackPanel();
            p.Orientation = Orientation.Horizontal;
            p.HorizontalAlignment = HorizontalAlignment.Right;
            p.Margin = new Thickness(0, 6, 0, 0);
            foreach (Button b in buttons) { b.Margin = new Thickness(8, 0, 0, 0); p.Children.Add(b); }
            return p;
        }

        // ------------------------------------------------------------------ pages

        void ShowOptions()
        {
            root.Children.Clear();

            if (uninstall)
            {
                root.Children.Add(Header("icodrop 제거", "원래 배경화면으로 되돌린 뒤 프로그램을 지워요"));
                StackPanel u = new StackPanel();
                cbData = Check("설정과 생성된 배경 이미지도 함께 삭제", false);
                cbData.Margin = new Thickness(0);
                u.Children.Add(cbData);
                TextBlock hint = Text("체크하지 않으면 다시 설치할 때 설정이 그대로 남아 있어요.", 12, true, null);
                hint.Margin = new Thickness(34, 4, 0, 0);
                u.Children.Add(hint);
                root.Children.Add(Card(u));
                root.Children.Add(ButtonRow(
                    MakeButton("제거", "Primary", delegate { StartWork(); }),
                    MakeButton("취소", "Secondary", delegate { Close(); })));
                return;
            }

            root.Children.Add(Header("icodrop 설치", "배경과 아이콘을 더욱 조화롭게"));

            StackPanel where = new StackPanel();
            where.Children.Add(Text("설치 위치", 13, true, null));
            Grid row = new Grid();
            row.Margin = new Thickness(0, 8, 0, 0);
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pathBox = new TextBox();
            pathBox.Text = existing ?? Setup.DefaultDir;
            pathBox.IsReadOnly = true;
            pathBox.BorderThickness = new Thickness(0);
            pathBox.Background = Brushes.Transparent;
            pathBox.FontSize = 13;
            pathBox.VerticalAlignment = VerticalAlignment.Center;
            pathBox.SetResourceReference(TextBox.ForegroundProperty, "TextBrush");
            row.Children.Add(pathBox);
            Button change = MakeButton("변경...", "Secondary", delegate { ChooseFolder(); });
            Grid.SetColumn(change, 1);
            row.Children.Add(change);
            where.Children.Add(row);
            root.Children.Add(Card(where));

            StackPanel opts = new StackPanel();
            cbDesktop = Check("바탕화면에 바로가기 만들기", true);
            cbMenu = Check("시작 메뉴에 추가", true);
            cbAuto = Check("Windows 시작 시 자동 실행", true);
            cbRun = Check("설치가 끝나면 바로 실행", true);
            opts.Children.Add(cbDesktop); opts.Children.Add(cbMenu); opts.Children.Add(cbAuto); opts.Children.Add(cbRun);
            root.Children.Add(Card(opts));

            if (existing != null)
            {
                TextBlock note = Text("이미 설치되어 있어요. 이어서 진행하면 최신 버전으로 업데이트돼요.", 12, true, null);
                note.Margin = new Thickness(4, 0, 0, 10);
                root.Children.Add(note);
            }

            root.Children.Add(ButtonRow(
                MakeButton(existing != null ? "업데이트" : "설치", "Primary", delegate { StartWork(); }),
                MakeButton("취소", "Secondary", delegate { Close(); })));
        }

        void ChooseFolder()
        {
            System.Windows.Forms.FolderBrowserDialog dlg = new System.Windows.Forms.FolderBrowserDialog();
            dlg.Description = "icodrop을 설치할 폴더를 선택하세요";
            dlg.SelectedPath = pathBox.Text;
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string chosen = dlg.SelectedPath;
                // Always keep the app in its own folder so uninstalling never touches unrelated files.
                if (!string.Equals(Path.GetFileName(chosen.TrimEnd('\\')), "icodrop", StringComparison.OrdinalIgnoreCase))
                    chosen = Path.Combine(chosen, "icodrop");
                pathBox.Text = chosen;
            }
        }

        void ShowProgress()
        {
            root.Children.Clear();
            root.Children.Add(Header(uninstall ? "icodrop 제거 중" : "icodrop 설치 중", "잠시만 기다려 주세요"));
            StackPanel p = new StackPanel();
            bar = new ProgressBar();
            bar.Style = (Style)FindResource("Bar");
            bar.Minimum = 0; bar.Maximum = 1;
            p.Children.Add(bar);
            status = Text("준비하는 중...", 12.5, true, null);
            status.Margin = new Thickness(0, 10, 0, 0);
            p.Children.Add(status);
            root.Children.Add(Card(p));
        }

        void ShowDone(string error)
        {
            root.Children.Clear();
            if (error != null)
            {
                root.Children.Add(Header("문제가 생겼어요", uninstall ? "제거를 끝내지 못했어요" : "설치를 끝내지 못했어요"));
                root.Children.Add(Card(Text(error, 12.5, true, null)));
            }
            else if (uninstall)
            {
                root.Children.Add(Header("제거했어요", "icodrop을 이 컴퓨터에서 지웠어요"));
            }
            else
            {
                root.Children.Add(Header("설치가 끝났어요", options.LaunchAfter ? "icodrop이 작업 표시줄 트레이에서 실행 중이에요" : "시작 메뉴나 바탕화면에서 실행할 수 있어요"));
            }
            root.Children.Add(ButtonRow(MakeButton("닫기", "Primary", delegate { Close(); })));
        }

        // ------------------------------------------------------------------ run

        void StartWork()
        {
            if (uninstall)
            {
                options.RemoveData = cbData.IsChecked == true;
            }
            else
            {
                options.Dir = pathBox.Text;
                options.DesktopShortcut = cbDesktop.IsChecked == true;
                options.StartMenu = cbMenu.IsChecked == true;
                options.AutoStart = cbAuto.IsChecked == true;
                options.LaunchAfter = cbRun.IsChecked == true;
            }
            ShowProgress();

            Thread t = new Thread(delegate()
            {
                string error = null;
                Action<string, double> progress = delegate(string text, double value)
                {
                    Dispatcher.BeginInvoke(new Action(delegate { status.Text = text; bar.Value = value; }));
                    Thread.Sleep(120);   // let the bar visibly advance; the copy itself is instant
                };
                try
                {
                    if (uninstall) Setup.Uninstall(options, progress);
                    else Setup.Install(options, progress);
                }
                catch (Exception ex) { error = ex.Message; }
                Dispatcher.BeginInvoke(new Action(delegate { ShowDone(error); }));
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            bool uninstall = Array.IndexOf(args, "/uninstall") >= 0 ||
                string.Equals(Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location), "uninstall", StringComparison.OrdinalIgnoreCase);
            bool silent = Array.IndexOf(args, "/silent") >= 0;

            if (silent)
            {
                // Unattended mode: icodrop-setup.exe /silent [/dir=PATH] [/nodesktop] [/nomenu] [/noautostart] [/norun] [/removedata]
                Options o = new Options();
                o.Dir = Setup.FindInstalled() ?? Setup.DefaultDir;
                foreach (string a in args)
                {
                    if (a.StartsWith("/dir=", StringComparison.OrdinalIgnoreCase)) o.Dir = a.Substring(5).Trim('"');
                    else if (a == "/nodesktop") o.DesktopShortcut = false;
                    else if (a == "/nomenu") o.StartMenu = false;
                    else if (a == "/noautostart") o.AutoStart = false;
                    else if (a == "/norun") o.LaunchAfter = false;
                    else if (a == "/removedata") o.RemoveData = true;
                }
                try
                {
                    Action<string, double> quiet = delegate(string t, double v) { };
                    if (uninstall) Setup.Uninstall(o, quiet); else Setup.Install(o, quiet);
                    return 0;
                }
                catch (Exception) { return 1; }
            }

            Application app = new Application();
            return app.Run(new SetupWindow(uninstall));
        }
    }
}
