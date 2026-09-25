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
    private static readonly bool NativeTopmostEnforcementEnabled = true;
    private static readonly double[] SupportedScales = [0.5, 0.6, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 2.5];

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
    private double _statusLogicalHeight = LogicalStatusHeight;
    private ACEvoStatus? _displayStatus;
    private bool _isLiveDisplay;
    private HwndSource? _windowSource;

    public MainWindow()
    {
        InitializeComponent();
        DebugLog.Initialize(Environment.GetCommandLineArgs());
        DebugLog.Info("AC EVO Simple Telemetry started.");
        _settings = AppSettings.Load();
        ApplySavedSettings();
        RestoreWindowSizes();
        SetDisplayStatus(ACEvoStatus.Off);
        RestoreWindowPosition();

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(1000.0 / 60.0)
        };
        _timer.Tick += Timer_Tick;
        _timer.Start();

        SourceInitialized += Window_SourceInitialized;
        Closing += (_, _) => SaveSettings();
        if (NativeTopmostEnforcementEnabled)
        {
            Activated += (_, _) => EnsureTopmost();
            Deactivated += (_, _) => EnsureTopmost();
        }

        Closed += (_, _) =>
        {
            _timer.Stop();
            _reader.Dispose();
            _windowSource?.RemoveHook(WindowMessageHook);
            DebugLog.Shutdown();
        };
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (NativeTopmostEnforcementEnabled && DateTime.UtcNow >= _nextTopmostRefresh)
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
        PedalGraph.AddSample(
            sample.Throttle,
            sample.Brake,
            sample.Clutch,
            sample.TcActive,
            sample.AbsActive);
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
        if (hadDisplayState)
        {
            RememberCurrentDisplaySize();
        }

        double anchorLeft = Left;
        double anchorTop = Top;
        _isLiveDisplay = showLive;
        TelemetryArea.Visibility = showLive ? Visibility.Visible : Visibility.Collapsed;
        StatusArea.Visibility = showLive ? Visibility.Collapsed : Visibility.Visible;

        if (showLive)
        {
            PedalGraph.Clear();
        }

        UpdateMinimumSize();
        double width = (showLive
            ? Math.Max(LogicalMinWidth, _liveLogicalWidth)
            : SettingsPanel.Visibility == Visibility.Visible
                ? Math.Max(LogicalSettingsMinWidth, _liveLogicalWidth)
                : Math.Max(LogicalStatusWidth, _statusLogicalWidth)) * _currentScale;
        double height = ((showLive ? _liveLogicalHeight : _statusLogicalHeight) + settingsHeight) * _currentScale;
        SetSizeFromTopLeft(width, height, anchorLeft, anchorTop);
    }

    private void SetSizeFromTopLeft(double width, double height, double anchorLeft, double anchorTop)
    {
        Width = width;
        Height = height;
        if (double.IsFinite(anchorLeft))
        {
            Left = anchorLeft;
        }
        if (double.IsFinite(anchorTop))
        {
            Top = anchorTop;
        }
        SyncScaledRootSize();
    }

    private double GetVisibleSettingsHeight() => SettingsPanel.Visibility == Visibility.Visible
        ? (_settingsPanelLogicalHeight > 0 ? _settingsPanelLogicalHeight : SettingsPanel.ActualHeight)
        : 0;

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        nint handle = new WindowInteropHelper(this).Handle;
        ApplyNoActivateStyle(handle);

        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowMessageHook);

        if (NativeTopmostEnforcementEnabled)
        {
            EnsureTopmost();
        }
    }

    private static void ApplyNoActivateStyle(nint handle)
    {
        Marshal.SetLastPInvokeError(0);
        nint currentStyle = GetWindowLongPtrCompat(handle, GwlExStyle);
        int error = Marshal.GetLastPInvokeError();
        if (currentStyle == nint.Zero && error != 0)
        {
            DebugLog.Warning($"Unable to read extended window styles (Win32 error {error}).");
            return;
        }

        nint updatedStyle = currentStyle | WsExNoActivate;
        if (updatedStyle == currentStyle)
        {
            return;
        }

        Marshal.SetLastPInvokeError(0);
        nint previousStyle = SetWindowLongPtrCompat(handle, GwlExStyle, updatedStyle);
        error = Marshal.GetLastPInvokeError();
        if (previousStyle == nint.Zero && error != 0)
        {
            DebugLog.Warning($"Unable to apply WS_EX_NOACTIVATE (Win32 error {error}).");
            return;
        }

        SetWindowPos(
            handle,
            nint.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        DebugLog.Info("Applied WS_EX_NOACTIVATE to the overlay window.");
    }

    private static nint WindowMessageHook(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message == WmMouseActivate)
        {
            // Keep the game active while allowing the click to reach the WPF control.
            handled = true;
            return MaNoActivate;
        }

        return nint.Zero;
    }

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
        RememberCurrentDisplaySize();
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

    private void ScaleDownButton_Click(object sender, RoutedEventArgs e) => StepScale(-1);

    private void ScaleUpButton_Click(object sender, RoutedEventArgs e) => StepScale(1);

    private void StepScale(int direction)
    {
        int currentIndex = 0;
        double smallestDifference = double.MaxValue;
        for (int i = 0; i < SupportedScales.Length; i++)
        {
            double difference = Math.Abs(SupportedScales[i] - _currentScale);
            if (difference < smallestDifference)
            {
                smallestDifference = difference;
                currentIndex = i;
            }
        }

        int newIndex = Math.Clamp(currentIndex + direction, 0, SupportedScales.Length - 1);
        if (newIndex != currentIndex)
        {
            ApplyWindowScale(SupportedScales[newIndex], save: true);
        }
    }

    private void ApplyWindowScale(double scale, bool save)
    {
        if (ScaledRoot is null)
        {
            return;
        }

        double oldScale = _currentScale <= 0 ? 1.0 : _currentScale;
        double currentWidth = ActualWidth > 0 ? ActualWidth : Width;
        double currentHeight = ActualHeight > 0 ? ActualHeight : Height;
        double logicalWidth = currentWidth / oldScale;
        double logicalHeight = currentHeight / oldScale;
        double anchorLeft = Left;
        double anchorTop = Top;

        _currentScale = scale;
        ScaleValueText.Text = $"{scale * 100:0}%";
        ScaledRoot.LayoutTransform = new ScaleTransform(scale, scale);
        UpdateMinimumSize();
        SetSizeFromTopLeft(
            Math.Max(MinWidth, logicalWidth * scale),
            Math.Max(MinHeight, logicalHeight * scale),
            anchorLeft,
            anchorTop);
        if (save && IsLoaded)
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

    private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (LightThemeRadio is null)
        {
            return;
        }

        ApplyTheme(LightThemeRadio.IsChecked == true);
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

        double savedScale = SupportedScales[0];
        double smallestDifference = double.MaxValue;
        foreach (double supportedScale in SupportedScales)
        {
            double difference = Math.Abs(supportedScale - _settings.WindowScale);
            if (difference < smallestDifference)
            {
                smallestDifference = difference;
                savedScale = supportedScale;
            }
        }
        ApplyWindowScale(savedScale, save: false);

        bool useLightTheme = string.Equals(_settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
        LightThemeRadio.IsChecked = useLightTheme;
        DarkThemeRadio.IsChecked = !useLightTheme;
        ApplyTheme(useLightTheme);

        ApplyInputSelection();
        TimeSpanValue.Text = $"{(int)TimeSpanSlider.Value} seconds";
        PedalGraph.TimeSpanSeconds = TimeSpanSlider.Value;
    }

    private void RestoreWindowPosition()
    {
        if (_settings.WindowLeft is not double savedLeft ||
            _settings.WindowTop is not double savedTop ||
            !double.IsFinite(savedLeft) || !double.IsFinite(savedTop))
        {
            return;
        }

        // Keep enough of the overlay visible to drag it back after a display change.
        const double visibleWidth = 64;
        const double visibleHeight = 32;
        double screenLeft = SystemParameters.VirtualScreenLeft;
        double screenTop = SystemParameters.VirtualScreenTop;
        double screenRight = screenLeft + SystemParameters.VirtualScreenWidth;
        double screenBottom = screenTop + SystemParameters.VirtualScreenHeight;
        if (!double.IsFinite(screenLeft) || !double.IsFinite(screenTop) ||
            !double.IsFinite(screenRight) || !double.IsFinite(screenBottom))
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = Math.Clamp(savedLeft, screenLeft - Width + visibleWidth, screenRight - visibleWidth);
        Top = Math.Clamp(savedTop, screenTop, screenBottom - visibleHeight);
    }

    private void RestoreWindowSizes()
    {
        _liveLogicalWidth = RestoredSize(_settings.LiveWindowWidth, _liveLogicalWidth, LogicalMinWidth);
        _liveLogicalHeight = RestoredSize(_settings.LiveWindowHeight, _liveLogicalHeight, LogicalMinTelemetryHeight);
        _statusLogicalWidth = RestoredSize(_settings.StatusWindowWidth, _statusLogicalWidth, LogicalStatusWidth);
        _statusLogicalHeight = RestoredSize(_settings.StatusWindowHeight, _statusLogicalHeight, LogicalStatusHeight);
    }

    private static double RestoredSize(double? saved, double fallback, double minimum) =>
        saved is double value && double.IsFinite(value) && value >= minimum && value <= 10000
            ? value
            : fallback;

    private void RememberCurrentDisplaySize()
    {
        if (!IsLoaded || _currentScale <= 0)
        {
            return;
        }

        double width = (double.IsFinite(Width) && Width > 0 ? Width : ActualWidth) / _currentScale;
        double height = (double.IsFinite(Height) && Height > 0 ? Height : ActualHeight) / _currentScale - GetVisibleSettingsHeight();
        if (!double.IsFinite(width) || !double.IsFinite(height))
        {
            return;
        }

        if (_isLiveDisplay)
        {
            _liveLogicalWidth = Math.Max(LogicalMinWidth, width);
            _liveLogicalHeight = Math.Max(LogicalMinTelemetryHeight, height);
        }
        else
        {
            if (SettingsPanel.Visibility != Visibility.Visible)
            {
                _statusLogicalWidth = Math.Max(LogicalStatusWidth, width);
            }
            _statusLogicalHeight = Math.Max(LogicalStatusHeight, height);
        }
    }

    private void SaveSettings()
    {
        RememberCurrentDisplaySize();
        _settings.ShowThrottle = ThrottleCheck.IsChecked == true;
        _settings.ShowBrake = BrakeCheck.IsChecked == true;
        _settings.ShowClutch = ClutchCheck.IsChecked == true;
        _settings.GraphTimeSpanSeconds = (int)Math.Round(TimeSpanSlider.Value);
        _settings.WindowScale = _currentScale;
        _settings.LiveWindowWidth = _liveLogicalWidth;
        _settings.LiveWindowHeight = _liveLogicalHeight;
        _settings.StatusWindowWidth = _statusLogicalWidth;
        _settings.StatusWindowHeight = _statusLogicalHeight;
        if (double.IsFinite(Left) && double.IsFinite(Top))
        {
            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
        }
        _settings.Theme = LightThemeRadio.IsChecked == true ? "Light" : "Dark";
        _settings.Save();
    }

    private static readonly nint HwndTopmost = new(-1);
    private static readonly nint WsExNoActivate = new(0x08000000);
    private static readonly nint MaNoActivate = new(3);
    private const int GwlExStyle = -20;
    private const int WmMouseActivate = 0x0021;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoOwnerZOrder = 0x0200;

    private static nint GetWindowLongPtrCompat(nint hWnd, int index) =>
        Environment.Is64BitProcess
            ? GetWindowLongPtr64(hWnd, index)
            : new nint(GetWindowLong32(hWnd, index));

    private static nint SetWindowLongPtrCompat(nint hWnd, int index, nint value) =>
        Environment.Is64BitProcess
            ? SetWindowLongPtr64(hWnd, index, value)
            : new nint(SetWindowLong32(hWnd, index, value.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(nint hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint hWnd, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hWnd, int index, nint value);

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
