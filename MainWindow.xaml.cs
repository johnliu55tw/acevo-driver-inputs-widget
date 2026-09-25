using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ACEvo_Simple_Telemetry;

public partial class MainWindow : Window
{
    private const double LogicalMinWidth = 560;
    private const double LogicalMinTelemetryHeight = 180;
    private const double LogicalStatusWidth = 360;
    private const double LogicalStatusHeight = 58;
    private const double LogicalSettingsMinWidth = 820;
    private const double CornerRadius = 10;

    private readonly ACEvoTelemetryReader _reader = new();
    private readonly DispatcherTimer _timer;
    private readonly AppSettings _settings;
    private DateTime _nextConnectAttempt = DateTime.MinValue;
    private DateTime _nextTopmostRefresh = DateTime.MinValue;
    private double _currentScale = 1.0;
    private double _settingsPanelLogicalHeight;
    private double _liveLogicalWidth = 980;
    private double _liveLogicalHeight = 286;
    private double _statusLogicalWidth = LogicalStatusWidth;
    private ACEvoStatus? _displayStatus;
    private bool _isLiveDisplay;

    public MainWindow()
    {
        InitializeComponent();
        DebugLog.Initialize(Environment.GetCommandLineArgs());
        DebugLog.Info("AC EVO Simple Telemetry started.");
        _settings = AppSettings.Load();
        ApplySavedSettings();
        SetDisplayStatus(ACEvoStatus.Off);

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(1000.0 / 60.0)
        };
        _timer.Tick += Timer_Tick;
        _timer.Start();

        SourceInitialized += (_, _) => EnsureTopmost();
        Activated += (_, _) => EnsureTopmost();
        Deactivated += (_, _) => EnsureTopmost();

        Closed += (_, _) =>
        {
            _timer.Stop();
            _reader.Dispose();
            DebugLog.Shutdown();
        };
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow >= _nextTopmostRefresh)
        {
            EnsureTopmost();
            _nextTopmostRefresh = DateTime.UtcNow.AddSeconds(1);
        }

        if (!_reader.IsConnected && DateTime.UtcNow >= _nextConnectAttempt)
        {
            _reader.TryConnect();
            _nextConnectAttempt = DateTime.UtcNow.AddSeconds(1);
        }

        if (!_reader.TryRead(out TelemetrySample sample))
        {
            if (!_reader.IsConnected)
            {
                SetDisplayStatus(ACEvoStatus.Off);
            }
            return;
        }

        SetDisplayStatus(sample.Status);
        if (sample.Status == ACEvoStatus.Live)
        {
            UpdateTelemetry(sample);
        }
    }

    private void UpdateTelemetry(TelemetrySample sample)
    {
        double throttle = sample.Throttle * 100.0;
        double brake = sample.Brake * 100.0;
        double clutch = sample.Clutch * 100.0;
        double degrees = sample.SteeringRadians * 180.0 / Math.PI;

        ThrottleBar.Value = throttle;
        BrakeBar.Value = brake;
        ClutchBar.Value = clutch;
        ThrottleValue.Text = Math.Round(throttle).ToString(CultureInfo.InvariantCulture);
        BrakeValue.Text = Math.Round(brake).ToString(CultureInfo.InvariantCulture);
        ClutchValue.Text = Math.Round(clutch).ToString(CultureInfo.InvariantCulture);
        WheelAngleText.Text = $"{degrees:+0;-0;0}°";
        SteeringWheel.AngleDegrees = degrees;
        GearText.Text = FormatGear(sample.Gear);
        PedalGraph.AddSample(sample.Throttle, sample.Brake, sample.Clutch);
    }

    private static string FormatGear(int rawGear) => rawGear switch
    {
        0 => "R",
        1 => "N",
        > 1 => (rawGear - 1).ToString(CultureInfo.InvariantCulture),
        _ => "–"
    };

    private void SetDisplayStatus(ACEvoStatus status)
    {
        bool hadDisplayState = _displayStatus.HasValue;
        bool showLive = status == ACEvoStatus.Live;

        _displayStatus = status;
        StatusText.Text = status switch
        {
            ACEvoStatus.Replay => "Replay in progress...",
            ACEvoStatus.Pause => "Paused",
            _ => "Waiting for session to start..."
        };
        Title = status switch
        {
            ACEvoStatus.Live => "AC EVO Simple Telemetry — Live",
            ACEvoStatus.Replay => "AC EVO Simple Telemetry — Replay",
            ACEvoStatus.Pause => "AC EVO Simple Telemetry — Paused",
            _ => "AC EVO Simple Telemetry — Waiting for session"
        };

        if (hadDisplayState && showLive == _isLiveDisplay)
        {
            return;
        }

        double settingsHeight = GetVisibleSettingsHeight();
        double logicalWidth = (ActualWidth > 0 ? ActualWidth : Width) / _currentScale;
        double logicalHeight = (ActualHeight > 0 ? ActualHeight : Height) / _currentScale;

        if (hadDisplayState && _isLiveDisplay)
        {
            _liveLogicalWidth = Math.Max(LogicalMinWidth, logicalWidth);
            _liveLogicalHeight = Math.Max(LogicalMinTelemetryHeight, logicalHeight - settingsHeight);
        }
        else if (hadDisplayState && SettingsPanel.Visibility != Visibility.Visible)
        {
            _statusLogicalWidth = Math.Max(LogicalStatusWidth, logicalWidth);
        }

        _isLiveDisplay = showLive;
        TelemetryArea.Visibility = showLive ? Visibility.Visible : Visibility.Collapsed;
        StatusArea.Visibility = showLive ? Visibility.Collapsed : Visibility.Visible;

        if (showLive)
        {
            PedalGraph.Clear();
        }

        UpdateMinimumSize();
        Width = (showLive
            ? Math.Max(LogicalMinWidth, _liveLogicalWidth)
            : SettingsPanel.Visibility == Visibility.Visible
                ? Math.Max(LogicalSettingsMinWidth, _liveLogicalWidth)
                : Math.Max(LogicalStatusWidth, _statusLogicalWidth)) * _currentScale;
        Height = ((showLive ? _liveLogicalHeight : LogicalStatusHeight) + settingsHeight) * _currentScale;
        SyncScaledRootSize();
    }

    private double GetVisibleSettingsHeight() => SettingsPanel.Visibility == Visibility.Visible
        ? (_settingsPanelLogicalHeight > 0 ? _settingsPanelLogicalHeight : SettingsPanel.ActualHeight)
        : 0;

    private void EnsureTopmost()
    {
        nint handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero || WindowState == WindowState.Minimized)
        {
            return;
        }

        SetWindowPos(
            handle,
            HwndTopmost,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsPanel.Visibility == Visibility.Visible)
        {
            double panelHeight = _settingsPanelLogicalHeight > 0
                ? _settingsPanelLogicalHeight
                : SettingsPanel.ActualHeight;
            SettingsPanel.Visibility = Visibility.Collapsed;
            UpdateMinimumSize();
            Height = Math.Max(MinHeight, Height - panelHeight * _currentScale);
            if (!_isLiveDisplay)
            {
                Width = Math.Max(LogicalStatusWidth, _statusLogicalWidth) * _currentScale;
            }
            SyncScaledRootSize();
            return;
        }

        if (!_isLiveDisplay)
        {
            _statusLogicalWidth = Math.Max(
                LogicalStatusWidth,
                (ActualWidth > 0 ? ActualWidth : Width) / _currentScale);
            Width = Math.Max(LogicalSettingsMinWidth, _liveLogicalWidth) * _currentScale;
        }
        SettingsPanel.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            SettingsPanel.UpdateLayout();
            _settingsPanelLogicalHeight = Math.Max(SettingsPanel.DesiredSize.Height, SettingsPanel.ActualHeight);
            Height += _settingsPanelLogicalHeight * _currentScale;
            UpdateMinimumSize();
            SyncScaledRootSize();
        });
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void InputSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ApplyInputSelection();
        SaveSettings();
    }

    private void ApplyInputSelection()
    {
        bool throttle = ThrottleCheck.IsChecked == true;
        bool brake = BrakeCheck.IsChecked == true;
        bool clutch = ClutchCheck.IsChecked == true;

        ThrottleGauge.Visibility = throttle ? Visibility.Visible : Visibility.Collapsed;
        BrakeGauge.Visibility = brake ? Visibility.Visible : Visibility.Collapsed;
        ClutchGauge.Visibility = clutch ? Visibility.Visible : Visibility.Collapsed;
        PedalGraph.ShowThrottle = throttle;
        PedalGraph.ShowBrake = brake;
        PedalGraph.ShowClutch = clutch;
    }

    private void TimeSpanSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TimeSpanValue is null || PedalGraph is null)
        {
            return;
        }

        int seconds = (int)Math.Round(e.NewValue);
        TimeSpanValue.Text = $"{seconds} seconds";
        PedalGraph.TimeSpanSeconds = seconds;
        if (IsLoaded)
        {
            SaveSettings();
        }
    }

    private void ScaleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScaledRoot is null || ScaleCombo.SelectedItem is not ComboBoxItem item ||
            !double.TryParse(item.Tag?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double scale))
        {
            return;
        }

        double oldScale = _currentScale <= 0 ? 1.0 : _currentScale;
        double currentWidth = ActualWidth > 0 ? ActualWidth : Width;
        double currentHeight = ActualHeight > 0 ? ActualHeight : Height;
        double logicalWidth = currentWidth / oldScale;
        double logicalHeight = currentHeight / oldScale;

        _currentScale = scale;
        ScaledRoot.LayoutTransform = new ScaleTransform(scale, scale);
        UpdateMinimumSize();
        Width = Math.Max(MinWidth, logicalWidth * scale);
        Height = Math.Max(MinHeight, logicalHeight * scale);
        SyncScaledRootSize();
        if (IsLoaded)
        {
            SaveSettings();
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => SyncScaledRootSize();

    private void SyncScaledRootSize()
    {
        if (ScaledRoot is null || _currentScale <= 0)
        {
            return;
        }

        double availableWidth = ActualWidth > 0 ? ActualWidth : Width;
        double availableHeight = ActualHeight > 0 ? ActualHeight : Height;
        if (double.IsFinite(availableWidth) && availableWidth > 0)
        {
            ScaledRoot.Width = availableWidth / _currentScale;
        }
        if (double.IsFinite(availableHeight) && availableHeight > 0)
        {
            ScaledRoot.Height = availableHeight / _currentScale;
        }
    }

    private void UpdateMinimumSize()
    {
        double logicalMinWidth = _isLiveDisplay ? LogicalMinWidth : LogicalStatusWidth;
        if (SettingsPanel?.Visibility == Visibility.Visible)
        {
            logicalMinWidth = Math.Max(logicalMinWidth, LogicalSettingsMinWidth);
        }

        MinWidth = logicalMinWidth * _currentScale;
        double settingsHeight = GetVisibleSettingsHeight();
        double contentHeight = _isLiveDisplay ? LogicalMinTelemetryHeight : LogicalStatusHeight;
        MinHeight = (contentHeight + settingsHeight) * _currentScale;
    }

    private void ScaledRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ScaledRoot.Clip = new RectangleGeometry(
            new Rect(0, 0, e.NewSize.Width, e.NewSize.Height),
            CornerRadius,
            CornerRadius);
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        ApplyTheme(string.Equals(item.Tag?.ToString(), "Light", StringComparison.OrdinalIgnoreCase));
        if (IsLoaded)
        {
            SaveSettings();
        }
    }

    private void ApplyTheme(bool light)
    {
        if (light)
        {
            SetThemeBrush("PrimaryTextBrush", "#16161A");
            SetThemeBrush("SecondaryTextBrush", "#5F6268");
            SetThemeBrush("MutedTextBrush", "#737780");
            SetThemeBrush("OverlayBackgroundBrush", "#F2F4F7");
            SetThemeBrush("SettingsBackgroundBrush", "#E5E7EB");
            SetThemeBrush("PanelBackgroundBrush", "#00FFFFFF");
            SetThemeBrush("GaugeTrackBrush", "#00FFFFFF");
            SetThemeBrush("ControlBorderBrush", "#B9BEC8");
            SetThemeBrush("HoverControlsBrush", "#D9FFFFFF");
            SetThemeBrush("GraphBackgroundBrush", "#00FFFFFF");
            SetThemeBrush("GraphGridBrush", "#506B7280");
            SetThemeBrush("WheelBackgroundBrush", "#00FFFFFF");
            SetThemeBrush("WheelRimBrush", "#24262A");
            SetThemeBrush("WheelSpokeBrush", "#5A5E66");
            SetThemeBrush("WheelHubBrush", "#AEB3BC");
        }
        else
        {
            SetThemeBrush("PrimaryTextBrush", "#F4F4F4");
            SetThemeBrush("SecondaryTextBrush", "#85858C");
            SetThemeBrush("MutedTextBrush", "#77777E");
            SetThemeBrush("OverlayBackgroundBrush", "#EC0A0A0C");
            SetThemeBrush("SettingsBackgroundBrush", "#18181C");
            SetThemeBrush("PanelBackgroundBrush", "#00000000");
            SetThemeBrush("GaugeTrackBrush", "#00000000");
            SetThemeBrush("ControlBorderBrush", "#34343A");
            SetThemeBrush("HoverControlsBrush", "#B8151518");
            SetThemeBrush("GraphBackgroundBrush", "#00000000");
            SetThemeBrush("GraphGridBrush", "#465A5A60");
            SetThemeBrush("WheelBackgroundBrush", "#00000000");
            SetThemeBrush("WheelRimBrush", "#E1E1E4");
            SetThemeBrush("WheelSpokeBrush", "#B6B6BC");
            SetThemeBrush("WheelHubBrush", "#2D2D32");
        }

        PedalGraph?.InvalidateVisual();
        SteeringWheel?.InvalidateVisual();
    }

    private void SetThemeBrush(string key, string color) =>
        Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

    private void ApplySavedSettings()
    {
        ThrottleCheck.IsChecked = _settings.ShowThrottle;
        BrakeCheck.IsChecked = _settings.ShowBrake;
        ClutchCheck.IsChecked = _settings.ShowClutch;
        TimeSpanSlider.Value = Math.Clamp(_settings.GraphTimeSpanSeconds, 5, 30);

        foreach (ComboBoxItem item in ScaleCombo.Items)
        {
            if (double.TryParse(item.Tag?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
                Math.Abs(value - _settings.WindowScale) < 0.001)
            {
                ScaleCombo.SelectedItem = item;
                break;
            }
        }

        string savedTheme = string.Equals(_settings.Theme, "Light", StringComparison.OrdinalIgnoreCase)
            ? "Light"
            : "Dark";
        foreach (ComboBoxItem item in ThemeCombo.Items)
        {
            if (string.Equals(item.Tag?.ToString(), savedTheme, StringComparison.OrdinalIgnoreCase))
            {
                ThemeCombo.SelectedItem = item;
                break;
            }
        }
        ApplyTheme(savedTheme == "Light");

        ApplyInputSelection();
        TimeSpanValue.Text = $"{(int)TimeSpanSlider.Value} seconds";
        PedalGraph.TimeSpanSeconds = TimeSpanSlider.Value;
    }

    private void SaveSettings()
    {
        if (ScaleCombo.SelectedItem is not ComboBoxItem item ||
            !double.TryParse(item.Tag?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double scale))
        {
            scale = 1.0;
        }

        _settings.ShowThrottle = ThrottleCheck.IsChecked == true;
        _settings.ShowBrake = BrakeCheck.IsChecked == true;
        _settings.ShowClutch = ClutchCheck.IsChecked == true;
        _settings.GraphTimeSpanSeconds = (int)Math.Round(TimeSpanSlider.Value);
        _settings.WindowScale = scale;
        _settings.Theme = ThemeCombo.SelectedItem is ComboBoxItem themeItem
            ? themeItem.Tag?.ToString() ?? "Dark"
            : "Dark";
        _settings.Save();
    }

    private static readonly nint HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
