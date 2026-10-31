using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WallBlur
{
    /// <summary>
    /// The settings window: acrylic backdrop, rounded cards, thin sliders and an iOS-style switch.
    /// Every change is saved and pushed to the desktop a moment after the slider stops moving.
    /// </summary>
    class SettingsWindow : Window
    {
        const string Styles = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <Style x:Key='KnobThumb' TargetType='Thumb'>
    <Setter Property='Width' Value='22'/>
    <Setter Property='Height' Value='22'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Thumb'>
          <Ellipse Fill='White' Stroke='#26000000' StrokeThickness='0.5' RenderTransformOrigin='0.5,0.5'>
            <Ellipse.RenderTransform><ScaleTransform x:Name='S' ScaleX='1' ScaleY='1'/></Ellipse.RenderTransform>
            <Ellipse.Effect><DropShadowEffect ShadowDepth='1' BlurRadius='5' Opacity='0.3'/></Ellipse.Effect>
          </Ellipse>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName='S' Storyboard.TargetProperty='ScaleX' To='1.14' Duration='0:0:0.12'/>
                    <DoubleAnimation Storyboard.TargetName='S' Storyboard.TargetProperty='ScaleY' To='1.14' Duration='0:0:0.12'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName='S' Storyboard.TargetProperty='ScaleX' To='1' Duration='0:0:0.15'/>
                    <DoubleAnimation Storyboard.TargetName='S' Storyboard.TargetProperty='ScaleY' To='1' Duration='0:0:0.15'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  

  </Style>

  <Style x:Key='FillBtn' TargetType='RepeatButton'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RepeatButton'>
          <Border Height='4' CornerRadius='2' Background='{DynamicResource AccentBrush}'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='EmptyBtn' TargetType='RepeatButton'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RepeatButton'>
          <Border Background='Transparent' Height='24'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='AppleSlider' TargetType='Slider'>
    <Setter Property='Height' Value='26'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Slider'>
          <Grid Background='Transparent'>
            <Border Height='4' CornerRadius='2' Background='{DynamicResource TrackBrush}' VerticalAlignment='Center'/>
            <Track x:Name='PART_Track'>
              <Track.DecreaseRepeatButton>
                <RepeatButton Style='{StaticResource FillBtn}' Command='Slider.DecreaseLarge'/>
              </Track.DecreaseRepeatButton>
              <Track.IncreaseRepeatButton>
                <RepeatButton Style='{StaticResource EmptyBtn}' Command='Slider.IncreaseLarge'/>
              </Track.IncreaseRepeatButton>
              <Track.Thumb>
                <Thumb Style='{StaticResource KnobThumb}'/>
              </Track.Thumb>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Switch' TargetType='ToggleButton'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ToggleButton'>
          <Grid Width='48' Height='28' Background='Transparent'>
            <Border CornerRadius='14' Background='{DynamicResource SwitchOffBrush}'/>
            <Border x:Name='On' CornerRadius='14' Background='{DynamicResource SwitchOnBrush}' Opacity='0'/>
            <Ellipse x:Name='Knob' Width='24' Height='24' Fill='White' HorizontalAlignment='Left' Margin='2,0,0,0'>
              <Ellipse.RenderTransform><TranslateTransform x:Name='T' X='0'/></Ellipse.RenderTransform>
              <Ellipse.Effect><DropShadowEffect ShadowDepth='1' BlurRadius='4' Opacity='0.3'/></Ellipse.Effect>
            </Ellipse>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName='T' Storyboard.TargetProperty='X' To='20' Duration='0:0:0.2'>
                      <DoubleAnimation.EasingFunction><CubicEase EasingMode='EaseOut'/></DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                    <DoubleAnimation Storyboard.TargetName='On' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.2'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName='T' Storyboard.TargetProperty='X' To='0' Duration='0:0:0.2'>
                      <DoubleAnimation.EasingFunction><CubicEase EasingMode='EaseOut'/></DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                    <DoubleAnimation Storyboard.TargetName='On' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.2'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  

  </Style>

  <Style x:Key='LinkBtn' TargetType='Button'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='B' Background='Transparent' Padding='14,7' CornerRadius='9'>
            <TextBlock Text='{TemplateBinding Content}' Foreground='{DynamicResource AccentBrush}' FontSize='13'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='B' Property='Background' Value='{DynamicResource HoverBrush}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='ResetBtn' TargetType='Button'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Width' Value='28'/>
    <Setter Property='Height' Value='28'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='B' CornerRadius='14' Background='Transparent'>
            <Viewbox Width='14' Height='14' HorizontalAlignment='Center' VerticalAlignment='Center' RenderTransformOrigin='0.5,0.5'>
              <Viewbox.RenderTransform><RotateTransform x:Name='Spin' Angle='0'/></Viewbox.RenderTransform>
              <Canvas Width='24' Height='24'>
                <Path x:Name='P' Data='M1,4 L1,10 L7,10 M3.51,15 A9,9 0 1 0 5.64,5.64 L1,10'
                      Stroke='{DynamicResource SubTextBrush}' StrokeThickness='2.4'
                      StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/>
              </Canvas>
            </Viewbox>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='B' Property='Background' Value='{DynamicResource HoverBrush}'/>
              <Setter TargetName='P' Property='Stroke' Value='{DynamicResource AccentBrush}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter TargetName='B' Property='Opacity' Value='0.28'/>
            </Trigger>
            <EventTrigger RoutedEvent='Button.Click'>
              <BeginStoryboard>
                <Storyboard>
                  <DoubleAnimation Storyboard.TargetName='Spin' Storyboard.TargetProperty='Angle' From='0' To='-360' Duration='0:0:0.45'>
                    <DoubleAnimation.EasingFunction><CubicEase EasingMode='EaseOut'/></DoubleAnimation.EasingFunction>
                  </DoubleAnimation>
                </Storyboard>
              </BeginStoryboard>
            </EventTrigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  
  </Style>

  <Style x:Key='PageBtn' TargetType='RepeatButton'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RepeatButton'>
          <Border Background='Transparent'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='ScrollKnob' TargetType='Thumb'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Thumb'>
          <Border x:Name='K' CornerRadius='3' Margin='2,0' Background='{DynamicResource SubTextBrush}' Opacity='0.42'/>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='K' Property='Opacity' Value='0.75'/></Trigger>
            <Trigger Property='IsDragging' Value='True'><Setter TargetName='K' Property='Opacity' Value='0.95'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='10'/>
    <Setter Property='MinWidth' Value='10'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Grid Background='Transparent'>
            <Track x:Name='PART_Track' IsDirectionReversed='True'>
              <Track.DecreaseRepeatButton>
                <RepeatButton Style='{StaticResource PageBtn}' Command='ScrollBar.PageUpCommand'/>
              </Track.DecreaseRepeatButton>
              <Track.IncreaseRepeatButton>
                <RepeatButton Style='{StaticResource PageBtn}' Command='ScrollBar.PageDownCommand'/>
              </Track.IncreaseRepeatButton>
              <Track.Thumb>
                <Thumb Style='{StaticResource ScrollKnob}'/>
              </Track.Thumb>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='OverlayScroll' TargetType='ScrollViewer'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollViewer'>
          <Grid>
            <ScrollContentPresenter CanContentScroll='{TemplateBinding CanContentScroll}'/>
            <ScrollBar x:Name='PART_VerticalScrollBar' HorizontalAlignment='Right' Margin='0,8,3,8'
                       Maximum='{TemplateBinding ScrollableHeight}' ViewportSize='{TemplateBinding ViewportHeight}'
                       Value='{Binding VerticalOffset, Mode=OneWay, RelativeSource={RelativeSource TemplatedParent}}'
                       Visibility='{TemplateBinding ComputedVerticalScrollBarVisibility}'/>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";

        class Row
        {
            public Slider Slider;
            public TextBlock Value;
            public double Scale;
            public double Step;
            public string Unit;
            public bool Relayer;
            public Func<Settings, double> Get;
            public Action<Settings, double> Set;
            public FrameworkElement Element;
        }

        /// <summary>A titled group of sliders with its own "back to default" button.</summary>
        class Section
        {
            public List<Row> Rows = new List<Row>();
            public Button Reset;
            public Panel CaptionRow;
        }

        readonly List<Section> sections = new List<Section>();
        Section current;

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        readonly Settings settings;
        readonly Action<bool> apply;
        readonly Func<bool> getAutoStart;
        readonly Action<bool> setAutoStart;
        readonly DispatcherTimer debounce = new DispatcherTimer();
        readonly List<Row> rows = new List<Row>();
        readonly bool dark;
        StackPanel body;
        Panel host;
        Grid frameGrid;
        StackPanel detailPanel;
        TextBlock chevronText;
        Action<bool> arrangeFn;
        bool detailsOpen;
        int animToken;
        Border pill;
        Grid segGrid;
        TranslateTransform pillShift;          // where BeginSection puts new section titles (the page, or the folded details)
        bool needRelayer;
        bool loading;

        /// <summary>Set when the app is really quitting; otherwise closing only hides the window.</summary>
        public bool Exiting;

        public SettingsWindow(Settings s, Action<bool> applyCallback, Func<bool> getAuto, Action<bool> setAuto)
        {
            settings = s;
            apply = applyCallback;
            getAutoStart = getAuto;
            setAutoStart = setAuto;
            dark = IsDarkTheme();

            Title = "icodrop";
            Width = 400;
            SizeToContent = SizeToContent.Height;
            MaxHeight = Math.Max(480, SystemParameters.WorkArea.Height - 40);
            ResizeMode = ResizeMode.CanMinimize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = SystemParameters.WorkArea.Right - Width - 28;
            Top = SystemParameters.WorkArea.Top + 28;
            Background = Brushes.Transparent;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Malgun Gothic");
            FontSize = 13;
            Icon = BitmapFrame.Create(Imaging.CreateBitmapSourceFromHIcon(
                Program.AppIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()));

            WindowChrome chrome = new WindowChrome();
            chrome.CaptionHeight = 76;
            chrome.GlassFrameThickness = new Thickness(-1);
            chrome.ResizeBorderThickness = new Thickness(0);
            chrome.UseAeroCaptionButtons = true;
            chrome.CornerRadius = new CornerRadius(8);
            WindowChrome.SetWindowChrome(this, chrome);

            Resources.MergedDictionaries.Add(BuildTheme());
            Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Styles));
            SetResourceReference(TextElement.ForegroundProperty, "TextBrush");

            Content = BuildContent();
            SyncFromSettings();

            debounce.Interval = TimeSpan.FromMilliseconds(120);
            debounce.Tick += delegate
            {
                debounce.Stop();
                bool relayer = needRelayer;
                needRelayer = false;
                apply(relayer);
            };
        }

        // ------------------------------------------------------------------ theme + backdrop

        static bool IsDarkTheme()
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

        ResourceDictionary BuildTheme()
        {
            ResourceDictionary d = new ResourceDictionary();
            d["AccentBrush"] = Hex(dark ? "#0A84FF" : "#007AFF");
            d["SwitchOnBrush"] = Hex(dark ? "#30D158" : "#34C759");
            d["SwitchOffBrush"] = Hex(dark ? "#4D8E8E93" : "#4D787880");
            d["TextBrush"] = Hex(dark ? "#F5F5F7" : "#1D1D1F");
            d["SubTextBrush"] = Hex(dark ? "#A1A1A6" : "#6E6E73");
            d["CardBrush"] = Hex(dark ? "#2EFFFFFF" : "#B8FFFFFF");
            d["CardBorderBrush"] = Hex(dark ? "#14FFFFFF" : "#14000000");
            d["SepBrush"] = Hex(dark ? "#1FFFFFFF" : "#1A000000");
            d["TrackBrush"] = Hex(dark ? "#40FFFFFF" : "#33787880");
            d["HoverBrush"] = Hex(dark ? "#1AFFFFFF" : "#12000000");
            d["SegActiveBrush"] = Hex(dark ? "#636366" : "#FFFFFF");
            return d;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr h = new WindowInteropHelper(this).Handle;
            HwndSource src = HwndSource.FromHwnd(h);
            if (src != null && src.CompositionTarget != null)
                src.CompositionTarget.BackgroundColor = Colors.Transparent;

            int on = dark ? 1 : 0;
            DwmSetWindowAttribute(h, 20, ref on, 4);        // immersive dark mode
            int round = 2;
            DwmSetWindowAttribute(h, 33, ref round, 4);     // rounded corners
            int acrylic = 3;
            if (DwmSetWindowAttribute(h, 38, ref acrylic, 4) != 0)   // system backdrop: acrylic
                Background = Hex(dark ? "#FF2B2B2E" : "#FFF2F2F7");   // older Windows: plain surface
        }

        // ------------------------------------------------------------------ layout

        UIElement BuildContent()
        {
            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.Focusable = false;
            scroll.Style = (Style)FindResource("OverlayScroll");

            body = new StackPanel();
            body.Margin = new Thickness(18, 0, 18, 18);
            scroll.Content = body;
            host = body;


            // the one switch that matters
            Border master = Card();
            master.Child = SwitchRow("블러 효과", null, settings.Enabled,
                delegate(bool on) { settings.Enabled = on; apply(false); });
            body.Children.Add(master);

            body.Children.Add(Caption("프리셋"));
            body.Children.Add(BuildStyleSwitcher());

            // These five sliders are built once and move: the front page shows blur + shadow strength,
            // and once the details are open they sit in their own sections instead.
            current = null;
            StackPanel stage = new StackPanel();
            AddSlider(stage, "블러 강도", 0, 80, 0.5, 1, "px", true,
                delegate(Settings x) { return x.Blur; }, delegate(Settings x, double v) { x.Blur = v; });
            Row rBlur = rows[rows.Count - 1];
            AddSlider(stage, "그림자 강도", 0, 100, 0.5, 0.01, "%", false,
                delegate(Settings x) { return x.ShadowOpacity; }, delegate(Settings x, double v) { x.ShadowOpacity = v; });
            Row rShadow = rows[rows.Count - 1];
            AddSlider(stage, "블러 경계", 2, 80, 0.5, 1, "px", false,
                delegate(Settings x) { return x.Feather; }, delegate(Settings x, double v) { x.Feather = v; });
            Row rFeather = rows[rows.Count - 1];
            AddSlider(stage, "그림자 경계", 4, 120, 0.5, 1, "px", false,
                delegate(Settings x) { return x.ShadowSize; }, delegate(Settings x, double v) { x.ShadowSize = v; });
            Row rSize = rows[rows.Count - 1];
            AddSlider(stage, "그림자 위치", 0, 40, 0.5, 1, "px", false,
                delegate(Settings x) { return x.ShadowOffset; }, delegate(Settings x, double v) { x.ShadowOffset = v; });
            Row rOffset = rows[rows.Count - 1];

            Section mainSec = BeginSection("기본");
            StackPanel mainPanel = new StackPanel();
            Border mainCard = Wrap(mainPanel);
            body.Children.Add(mainCard);
            Action<bool> arrange = null;
            // everything else is one click away
            StackPanel detail = new StackPanel();
            detailPanel = detail;
            detail.Visibility = Visibility.Collapsed;

            TextBlock chevron = new TextBlock();
            chevronText = chevron;
            chevron.Text = "›";
            chevron.FontSize = 20;
            chevron.RenderTransformOrigin = new Point(0.5, 0.5);
            chevron.RenderTransform = new RotateTransform(0);
            chevron.SetResourceReference(TextBlock.ForegroundProperty, "SubTextBrush");
            chevron.VerticalAlignment = VerticalAlignment.Center;

            Grid disclose = new Grid();
            disclose.Margin = new Thickness(12, 18, 12, 4);
            disclose.Cursor = Cursors.Hand;
            disclose.Background = Brushes.Transparent;
            disclose.ColumnDefinitions.Add(new ColumnDefinition());
            disclose.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            disclose.Children.Add(Label("세부 설정", 13, true));
            Grid.SetColumn(chevron, 1);
            disclose.Children.Add(chevron);
            disclose.MouseLeftButtonUp += delegate { SetDetails(!detailsOpen); };
            body.Children.Add(disclose);
            body.Children.Add(detail);

            host = detail;

            Section blurSec = BeginSection("블러");
            StackPanel blurPanel = new StackPanel();
            detail.Children.Add(Wrap(blurPanel));

            Section shadowSec = BeginSection("그림자");
            StackPanel shadowPanel = new StackPanel();
            detail.Children.Add(Wrap(shadowPanel));
            current = BeginSection("물방울");
            StackPanel drop = new StackPanel();
            detail.Children.Add(Wrap(drop));
            AddSlider(drop, "표면장력", 0, 100, 0.5, 0.01, "%", false,
                delegate(Settings x) { return x.Tension; }, delegate(Settings x, double v) { x.Tension = v; });
            AddSlider(drop, "테두리 광택", 0, 100, 0.5, 0.01, "%", false,
                delegate(Settings x) { return x.RimLight; }, delegate(Settings x, double v) { x.RimLight = v; });
            AddSlider(drop, "굴절", 0, 30, 0.5, 1, "px", false,
                delegate(Settings x) { return x.Refraction; }, delegate(Settings x, double v) { x.Refraction = v; });
            AddSlider(drop, "물빛", 0, 50, 0.5, 0.01, "%", true,
                delegate(Settings x) { return x.WaterTint; }, delegate(Settings x, double v) { x.WaterTint = v; });

            current = BeginSection("형태");
            StackPanel shape = new StackPanel();
            detail.Children.Add(Wrap(shape));
            AddSlider(shape, "블러 밝기", -30, 40, 0.5, 0.01, "%", true,
                delegate(Settings x) { return x.Brightness; }, delegate(Settings x, double v) { x.Brightness = v; });
            AddSlider(shape, "아이콘 여백", 0, 60, 0.5, 1, "px", false,
                delegate(Settings x) { return x.Padding; }, delegate(Settings x, double v) { x.Padding = v; });
            AddSlider(shape, "모서리 둥글기", 0, 60, 0.5, 1, "px", false,
                delegate(Settings x) { return x.Radius; }, delegate(Settings x, double v) { x.Radius = v; });
            AddSlider(shape, "윤곽 보정", 0, 100, 0.5, 1, "px", false,
                delegate(Settings x) { return x.Contour; }, delegate(Settings x, double v) { x.Contour = v; });

            current = null;
            detail.Children.Add(Caption("일반"));
            Border general = Card();
            general.Child = SwitchRow("로그인 시 자동 실행", null, getAutoStart(),
                delegate(bool on) { setAutoStart(on); });
            detail.Children.Add(general);

            stage.Children.Clear();
            arrange = delegate(bool open)
            {
                mainPanel.Children.Clear(); blurPanel.Children.Clear(); shadowPanel.Children.Clear();
                mainSec.Rows.Clear(); blurSec.Rows.Clear(); shadowSec.Rows.Clear();
                if (open)
                {
                    PlaceRows(blurPanel, blurSec, rBlur, rFeather);
                    PlaceRows(shadowPanel, shadowSec, rShadow, rSize, rOffset);
                }
                else
                {
                    PlaceRows(mainPanel, mainSec, rBlur, rShadow);
                    PlaceRows(blurPanel, blurSec, rFeather);
                    PlaceRows(shadowPanel, shadowSec, rSize, rOffset);
                }
                Visibility v = open ? Visibility.Collapsed : Visibility.Visible;
                mainSec.CaptionRow.Visibility = v;
                mainCard.Visibility = v;
                UpdateResets();
            };
            arrangeFn = arrange;
            arrange(false);

            // The logo row stays put; only the settings below it scroll, so nothing slides under the window buttons.
            FrameworkElement header = (FrameworkElement)BuildHeader();
            header.Margin = new Thickness(18, 0, 18, 0);
            Grid frame = new Grid();
            frame.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            frame.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(scroll, 1);
            frame.Children.Add(header);
            frame.Children.Add(scroll);
            frameGrid = frame;
            return frame;
        }
        UIElement BuildHeader()
        {
            Grid g = new Grid();
            g.Height = 76;
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());

            Image logo = new Image();
            logo.Source = LogoImage();
            logo.Width = 48; logo.Height = 48;
            logo.VerticalAlignment = VerticalAlignment.Center;
            logo.Margin = new Thickness(0, 4, 12, 0);
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            g.Children.Add(logo);

            StackPanel titles = new StackPanel();
            titles.VerticalAlignment = VerticalAlignment.Center;
            titles.Margin = new Thickness(0, 6, 0, 0);
            TextBlock title = Label("icodrop", 22, false);
            title.FontWeight = FontWeights.SemiBold;
            titles.Children.Add(title);
            titles.Children.Add(Label("바탕화면 아이콘을 깔끔하게", 12, true));
            Grid.SetColumn(titles, 1);
            g.Children.Add(titles);
            return g;
        }

        // ------------------------------------------------------------------ small builders

        Border[] segs;
        TextBlock[] segText;

        UIElement BuildStyleSwitcher()
        {
            string[] names = new string[] { "잔물결", "물방울", "유리" };
            Border track = new Border();
            track.CornerRadius = new CornerRadius(10);
            track.Padding = new Thickness(3);
            track.SetResourceReference(Border.BackgroundProperty, "TrackBrush");

            Grid g = new Grid();
            segGrid = g;
            for (int i = 0; i < names.Length; i++) g.ColumnDefinitions.Add(new ColumnDefinition());

            // one highlight that glides between the three choices
            pill = new Border();
            pill.CornerRadius = new CornerRadius(8);
            pill.HorizontalAlignment = HorizontalAlignment.Left;
            pill.IsHitTestVisible = false;
            pill.Opacity = 0;
            pill.Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 1, BlurRadius = 4, Opacity = 0.2 };
            pill.SetResourceReference(Border.BackgroundProperty, "SegActiveBrush");
            pillShift = new TranslateTransform();
            pill.RenderTransform = pillShift;
            Grid.SetColumnSpan(pill, names.Length);
            g.Children.Add(pill);
            g.SizeChanged += delegate { pill.Width = Math.Max(0, g.ActualWidth / names.Length); MovePill(false); };

            segs = new Border[names.Length];
            segText = new TextBlock[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                Border b = new Border();
                b.CornerRadius = new CornerRadius(8);
                b.Padding = new Thickness(0, 7, 0, 7);
                b.Cursor = Cursors.Hand;
                b.Background = Brushes.Transparent;
                TextBlock tb = Label(names[i], 13, false);
                tb.HorizontalAlignment = HorizontalAlignment.Center;
                b.Child = tb;
                int index = i;
                b.MouseLeftButtonUp += delegate { ApplyPreset(index); };
                Grid.SetColumn(b, i);
                g.Children.Add(b);
                segs[i] = b;
                segText[i] = tb;
            }
            track.Child = g;
            UpdateSegments();
            return track;
        }

        void UpdateSegments()
        {
            if (segs == null) return;
            for (int i = 0; i < segs.Length; i++)
                segText[i].FontWeight = settings.Preset == i ? FontWeights.SemiBold : FontWeights.Normal;
            MovePill(true);
        }

        /// <summary>Slides (or fades) the highlight to the active preset.</summary>
        void MovePill(bool animate)
        {
            if (pill == null || segGrid == null) return;
            int i = settings.Preset;
            double x = segGrid.ActualWidth / 3.0 * Math.Max(i, 0);
            double opacity = i >= 0 ? 1 : 0;
            if (!animate || !IsLoaded)
            {
                pillShift.BeginAnimation(TranslateTransform.XProperty, null);
                pill.BeginAnimation(OpacityProperty, null);
                pillShift.X = x;
                pill.Opacity = opacity;
                return;
            }
            if (i >= 0)
            {
                DoubleAnimation slide = new DoubleAnimation(x, TimeSpan.FromMilliseconds(240));
                slide.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                pillShift.BeginAnimation(TranslateTransform.XProperty, slide);
            }
            pill.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(180)));
        }
        /// <summary>Three ready-made looks; each one only sets the "droplet" controls, never blur or shadow.</summary>
        void ApplyPreset(int index)
        {
            settings.Preset = index;
            switch (index)
            {
                case 0:   // surface: one calm body of water behind the icons
                    settings.Tension = 1.0; settings.RimLight = 0.45; settings.Refraction = 6; settings.WaterTint = 0.05;
                    settings.Padding = 16; settings.Radius = 18; settings.Contour = 40;
                    break;
                case 1:   // droplets: one drop per icon, joined by thin necks
                    settings.Tension = 0.2; settings.RimLight = 0.6; settings.Refraction = 10; settings.WaterTint = 0.08;
                    settings.Padding = 10; settings.Radius = 30; settings.Contour = 40;
                    break;
                case 2:   // glass: clear, strongly lit edge, no colour cast
                    settings.Tension = 1.0; settings.RimLight = 0.85; settings.Refraction = 12; settings.WaterTint = 0.0;
                    settings.Padding = 16; settings.Radius = 22; settings.Contour = 40;
                    break;
            }
            SyncFromSettings();
            UpdateSegments();
            needRelayer = true;
            debounce.Stop();
            debounce.Start();
        }

        static ImageSource LogoImage()
        {
            try
            {
                using (System.IO.Stream s = typeof(SettingsWindow).Assembly.GetManifestResourceStream("icodrop.ico"))
                {
                    IconBitmapDecoder dec = new IconBitmapDecoder(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    BitmapFrame best = dec.Frames[0];
                    foreach (BitmapFrame f in dec.Frames) if (f.PixelWidth > best.PixelWidth) best = f;
                    return best;
                }
            }
            catch (Exception) { return null; }
        }
        Grid SwitchRow(string title, string subtitle, bool initial, Action<bool> changed)
        {
            Grid row = new Grid();
            row.Margin = subtitle == null ? new Thickness(16, 14, 14, 14) : new Thickness(16, 10, 14, 10);
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel text = new StackPanel();
            text.VerticalAlignment = VerticalAlignment.Center;
            text.Children.Add(Label(title, 14, false));
            if (subtitle != null) text.Children.Add(Label(subtitle, 11.5, true));
            row.Children.Add(text);
            ToggleButton sw = new ToggleButton();
            sw.Style = (Style)FindResource("Switch");
            sw.IsChecked = initial;
            sw.Click += delegate { changed(sw.IsChecked == true); };
            Grid.SetColumn(sw, 1);
            row.Children.Add(sw);
            return row;
        }
        TextBlock Label(string text, double size, bool secondary)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = size;
            t.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "SubTextBrush" : "TextBrush");
            return t;
        }

        TextBlock Caption(string text)
        {
            TextBlock t = Label(text, 12, true);
            t.Margin = new Thickness(12, 16, 0, 6);
            return t;
        }

        Border Card()
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(12);
            b.BorderThickness = new Thickness(1);
            b.SetResourceReference(Border.BackgroundProperty, "CardBrush");
            b.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            return b;
        }

        Border Wrap(Panel inner)
        {
            Border b = Card();
            b.Child = inner;
            return b;
        }

        void AddSlider(Panel card, string title, double min, double max, double step, double scale, string unit, bool relayer,
            Func<Settings, double> get, Action<Settings, double> set)
        {
            if (card.Children.Count > 0)
            {
                Border sep = new Border();
                sep.Height = 1;
                sep.Margin = new Thickness(16, 0, 0, 0);
                sep.SetResourceReference(Border.BackgroundProperty, "SepBrush");
                card.Children.Add(sep);
            }

            Row r = new Row();
            r.Scale = scale; r.Step = step; r.Unit = unit; r.Relayer = relayer; r.Get = get; r.Set = set;

            Grid g = new Grid();
            g.Margin = new Thickness(16, 11, 16, 7);
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock name = Label(title, 13.5, false);
            g.Children.Add(name);

            TextBlock value = Label("", 13, true);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(value, 1);
            g.Children.Add(value);
            r.Value = value;

            Slider s = new Slider();
            s.Style = (Style)FindResource("AppleSlider");
            s.Minimum = min; s.Maximum = max;
            s.TickFrequency = step;
            s.IsSnapToTickEnabled = true;
            s.SmallChange = step;
            s.LargeChange = Math.Max(step, Math.Round((max - min) / 10));
            s.Margin = new Thickness(0, 6, 0, 0);
            Grid.SetRow(s, 1);
            Grid.SetColumnSpan(s, 2);
            g.Children.Add(s);
            r.Slider = s;

            s.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                // Plain wheel scrolls the page; Ctrl + wheel nudges the slider by one step.
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                s.Value = Math.Max(s.Minimum, Math.Min(s.Maximum, s.Value + (e.Delta > 0 ? step : -step)));
                e.Handled = true;
            };

            s.ValueChanged += delegate
            {
                ShowValue(r);
                if (loading) return;
                r.Set(settings, s.Value * r.Scale);
                if (settings.Preset != -1) { settings.Preset = -1; UpdateSegments(); }
                UpdateResets();
                needRelayer |= r.Relayer;
                debounce.Stop();
                debounce.Start();
            };

            r.Element = g;
            rows.Add(r);
            if (current != null) current.Rows.Add(r);
            card.Children.Add(g);
        }

        static void ShowValue(Row r)
        {
            r.Value.Text = r.Slider.Value.ToString("0.0", CultureInfo.InvariantCulture) + " " + r.Unit;
        }

        void SyncFromSettings()
        {
            loading = true;
            foreach (Row r in rows)
            {
                double v = Math.Round(r.Get(settings) / r.Scale / r.Step) * r.Step;
                r.Slider.Value = Math.Max(r.Slider.Minimum, Math.Min(r.Slider.Maximum, v));
                ShowValue(r);
            }
            loading = false;
            UpdateResets();
        }

        /// <summary>Fills a card with the given sliders (hairline between them) and tracks them for that section's reset.</summary>
        void PlaceRows(Panel panel, Section sec, params Row[] rs)
        {
            foreach (Row r in rs)
            {
                if (panel.Children.Count > 0)
                {
                    Border sep = new Border();
                    sep.Height = 1;
                    sep.Margin = new Thickness(16, 0, 0, 0);
                    sep.SetResourceReference(Border.BackgroundProperty, "SepBrush");
                    panel.Children.Add(sep);
                }
                panel.Children.Add(r.Element);
                sec.Rows.Add(r);
            }
        }

        /// <summary>
        /// Opens or closes the details: the window grows or shrinks to its new height, the arrow turns and
        /// the details fade and settle in. The window is held at its current size meanwhile and handed back
        /// to size-to-content once the motion has finished.
        /// </summary>
        void SetDetails(bool open)
        {
            if (open == detailsOpen || frameGrid == null) return;
            detailsOpen = open;
            int token = ++animToken;

            double startH = ActualHeight;
            double chrome = startH - frameGrid.ActualHeight;
            TimeSpan move = TimeSpan.FromMilliseconds(300);
            IEasingFunction ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            // hold the current height, then work out where we are going
            SizeToContent = SizeToContent.Manual;
            Height = startH;

            detailPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (arrangeFn != null) arrangeFn(open);
            frameGrid.Measure(new Size(ActualWidth, double.PositiveInfinity));
            double target = Math.Min(MaxHeight, frameGrid.DesiredSize.Height + chrome);
            if (!open) detailPanel.Visibility = Visibility.Visible;   // keep it on screen while it fades out

            DoubleAnimation turn = new DoubleAnimation(open ? 90 : 0, TimeSpan.FromMilliseconds(240));
            turn.EasingFunction = ease;
            ((RotateTransform)chevronText.RenderTransform).BeginAnimation(RotateTransform.AngleProperty, turn);

            TranslateTransform settle = detailPanel.RenderTransform as TranslateTransform;
            if (settle == null) { settle = new TranslateTransform(); detailPanel.RenderTransform = settle; }
            if (open)
            {
                detailPanel.Opacity = 0;
                DoubleAnimation fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240));
                fade.BeginTime = TimeSpan.FromMilliseconds(70);
                detailPanel.BeginAnimation(OpacityProperty, fade);
                DoubleAnimation drop = new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(320));
                drop.EasingFunction = ease;
                settle.BeginAnimation(TranslateTransform.YProperty, drop);
            }
            else
            {
                detailPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140)));
            }

            DoubleAnimation grow = new DoubleAnimation(startH, target, move);
            grow.EasingFunction = ease;
            grow.Completed += delegate
            {
                if (token != animToken) return;   // a newer toggle took over
                BeginAnimation(HeightProperty, null);
                Height = target;
                detailPanel.BeginAnimation(OpacityProperty, null);
                detailPanel.Opacity = 1;
                settle.BeginAnimation(TranslateTransform.YProperty, null);
                settle.Y = 0;
                if (!detailsOpen) detailPanel.Visibility = Visibility.Collapsed;
                SizeToContent = SizeToContent.Height;
            };
            BeginAnimation(HeightProperty, grow);
        }

        Section NewSection()
        {
            Section sec = new Section();
            sections.Add(sec);
            return sec;
        }

        /// <summary>Section title on the left, its reset icon on the right.</summary>
        Section BeginSection(string title)
        {
            Section sec = NewSection();
            Grid g = new Grid();
            g.Margin = new Thickness(12, 8, 6, 0);
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock t = Label(title, 12, true);
            t.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(t);
            Button b = MakeResetButton(sec);
            Grid.SetColumn(b, 1);
            g.Children.Add(b);
            sec.CaptionRow = g;
            host.Children.Add(g);
            return sec;
        }

        Button MakeResetButton(Section sec)
        {
            Button b = new Button();
            b.Style = (Style)FindResource("ResetBtn");
            b.ToolTip = "재설정";
            b.Click += delegate { ResetSection(sec); };
            sec.Reset = b;
            return b;
        }

        /// <summary>Puts only this section's sliders back to their defaults.</summary>
        void ResetSection(Section sec)
        {
            Settings d = new Settings();
            foreach (Row r in sec.Rows) r.Set(settings, r.Get(d));
            settings.Preset = MatchesSurfaceLook() ? 0 : -1;
            SyncFromSettings();
            UpdateSegments();
            needRelayer = true;
            debounce.Stop();
            debounce.Start();
        }

        /// <summary>True when every value the style presets control equals the first preset ("잔물결").</summary>
        bool MatchesSurfaceLook()
        {
            Settings d = new Settings();
            return Math.Abs(settings.Tension - d.Tension) < 1e-6 && Math.Abs(settings.RimLight - d.RimLight) < 1e-6 &&
                Math.Abs(settings.Refraction - d.Refraction) < 1e-6 && Math.Abs(settings.WaterTint - d.WaterTint) < 1e-6 &&
                Math.Abs(settings.Padding - d.Padding) < 1e-6 && Math.Abs(settings.Radius - d.Radius) < 1e-6 &&
                Math.Abs(settings.Contour - d.Contour) < 1e-6;
        }

        /// <summary>A reset icon only makes sense while something in its section differs from the default.</summary>
        void UpdateResets()
        {
            Settings d = new Settings();
            foreach (Section sec in sections)
            {
                bool changed = false;
                foreach (Row r in sec.Rows)
                    if (Math.Abs(r.Get(settings) - r.Get(d)) > 1e-6) { changed = true; break; }
                if (sec.Reset != null) sec.Reset.IsEnabled = changed;
            }
        }
        // ------------------------------------------------------------------ lifetime

        /// <summary>Shows the window and pulls it in front of whatever is covering it.</summary>
        public void Present()
        {
            bool appearing = !IsVisible;
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Topmost = true;
            Activate();
            Topmost = false;
            if (appearing && frameGrid != null) FadeIn();
        }

        /// <summary>The content eases in a touch whenever the window is brought up from the tray.</summary>
        void FadeIn()
        {
            TranslateTransform lift = new TranslateTransform();
            frameGrid.RenderTransform = lift;
            frameGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            DoubleAnimation rise = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(280));
            rise.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            lift.BeginAnimation(TranslateTransform.YProperty, rise);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!Exiting)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnClosing(e);
        }
    }
}
