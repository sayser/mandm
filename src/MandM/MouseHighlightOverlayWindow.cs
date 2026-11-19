using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MandM.Models;

namespace MandM;

/// <summary>
/// Click-through ring that follows the cursor. Position comes from Win32
/// GetCursorPos on a dedicated thread and SetWindowPos, so a busy UI thread
/// cannot freeze the ring.
/// </summary>
public sealed class MouseHighlightOverlayWindow : Window
{
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    private readonly Canvas _canvas;
    private readonly Ellipse _inner;
    private readonly Ellipse _outer;
    private readonly DispatcherTimer _blinkTimer;

    private Color _color = Color.FromRgb(255, 85, 34);
    private double _diameter = 48;
    private double _secondDiameter = 84;
    private double _thickness = 3;
    private double _baseOpacity = 0.85;
    private bool _blink;
    private double _blinkHz = 2.0;
    private bool _secondRing;
    private volatile bool _enabled;
    private volatile bool _follow;
    private volatile IntPtr _hwnd;
    private Thread? _followThread;
    private double _lastBlinkOpacity = -1;

    public MouseHighlightOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _inner = CreateRing();
        _outer = CreateRing();
        _outer.Visibility = Visibility.Collapsed;

        _canvas = new Canvas { IsHitTestVisible = false };
        _canvas.Children.Add(_outer);
        _canvas.Children.Add(_inner);
        Content = _canvas;

        _blinkTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _blinkTimer.Tick += (_, _) => UpdateBlink();

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyClickThrough();
        };
        Loaded += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyClickThrough();
            PlaceRings();
        };
        Closed += (_, _) => StopFollow();
    }

    public void ApplyOptions(MouseHighlightSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _enabled = settings.Enabled;
        _color = settings.ToColor();
        _diameter = Math.Clamp(settings.Diameter, 16, 200);
        _secondDiameter = Math.Clamp(settings.SecondDiameter, 20, 260);
        _thickness = Math.Clamp(settings.Thickness, 1, 16);
        _baseOpacity = Math.Clamp(settings.Opacity <= 0 ? 0.85 : settings.Opacity, 0.15, 1.0);
        _blink = settings.Blink;
        _blinkHz = Math.Clamp(settings.BlinkHz <= 0 ? 2.0 : settings.BlinkHz, 0.5, 8.0);
        _secondRing = settings.SecondRing;
        ApplyAppearance();
        PlaceRings();

        if (_enabled)
        {
            if (!IsVisible) Show();
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyClickThrough();
            StartFollow();
            if (_blink) _blinkTimer.Start();
            else
            {
                _blinkTimer.Stop();
                ApplyOpacity(_baseOpacity);
            }
        }
        else
        {
            _blinkTimer.Stop();
            StopFollow();
            if (IsVisible) Hide();
        }
    }

    private static Ellipse CreateRing() => new()
    {
        Fill = Brushes.Transparent,
        IsHitTestVisible = false,
        SnapsToDevicePixels = false
    };

    private void ApplyAppearance()
    {
        var brush = new SolidColorBrush(_color);
        brush.Freeze();
        ConfigureRing(_inner, _diameter, brush);
        ConfigureRing(_outer, _secondDiameter, brush);
        _outer.Visibility = _secondRing ? Visibility.Visible : Visibility.Collapsed;
        ApplyOpacity(_baseOpacity);
    }

    private void ConfigureRing(Ellipse ring, double diameter, Brush brush)
    {
        ring.Width = diameter;
        ring.Height = diameter;
        ring.StrokeThickness = _thickness;
        ring.Stroke = brush;
    }

    private void PlaceRings()
    {
        var outer = _secondRing ? _secondDiameter : _diameter;
        var size = Math.Max(_diameter, outer) + _thickness * 2 + 4;
        Width = size;
        Height = size;
        Canvas.SetLeft(_inner, (size - _diameter) / 2.0);
        Canvas.SetTop(_inner, (size - _diameter) / 2.0);
        Canvas.SetLeft(_outer, (size - _secondDiameter) / 2.0);
        Canvas.SetTop(_outer, (size - _secondDiameter) / 2.0);
    }

    private void ApplyOpacity(double opacity)
    {
        _inner.Opacity = opacity;
        _outer.Opacity = opacity * 0.75;
        _lastBlinkOpacity = opacity;
    }

    private void UpdateBlink()
    {
        if (!_blink || !_enabled) return;
        var phase = (Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency) * _blinkHz;
        var wave = 0.5 + 0.5 * Math.Sin(phase * Math.PI * 2.0);
        var opacity = _baseOpacity * (0.15 + 0.85 * wave);
        if (Math.Abs(opacity - _lastBlinkOpacity) < 0.01) return;
        ApplyOpacity(opacity);
    }

    private void StartFollow()
    {
        if (_followThread is { IsAlive: true })
            return;
        _follow = true;
        _followThread = new Thread(FollowLoop)
        {
            IsBackground = true,
            Name = "MandM cursor",
            Priority = ThreadPriority.Normal
        };
        _followThread.Start();
    }

    private void StopFollow()
    {
        _follow = false;
        _followThread = null;
    }

    private void FollowLoop()
    {
        var lastX = int.MinValue;
        var lastY = int.MinValue;
        while (_follow)
        {
            var hwnd = _hwnd;
            if (_enabled && hwnd != IntPtr.Zero && GetCursorPos(out var pt) && (pt.X != lastX || pt.Y != lastY))
            {
                lastX = pt.X;
                lastY = pt.Y;
                if (GetWindowRect(hwnd, out var rect))
                {
                    var width = rect.Right - rect.Left;
                    var height = rect.Bottom - rect.Top;
                    if (width > 0 && height > 0)
                    {
                        SetWindowPos(
                            hwnd,
                            IntPtr.Zero,
                            pt.X - width / 2,
                            pt.Y - height / 2,
                            0,
                            0,
                            SwpNoSize | SwpNoZOrder | SwpNoActivate);
                    }
                }
            }

            Thread.Sleep(8);
        }
    }

    private void ApplyClickThrough()
    {
        var hwnd = _hwnd != IntPtr.Zero ? _hwnd : new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        _hwnd = hwnd;
        const int gwlExStyle = -20;
        const int wsExTransparent = 0x00000020;
        const int wsExLayered = 0x00080000;
        const int wsExToolWindow = 0x00000080;
        const int wsExNoActivate = 0x08000000;
        var style = GetWindowLong(hwnd, gwlExStyle);
        SetWindowLong(hwnd, gwlExStyle, style | wsExTransparent | wsExLayered | wsExToolWindow | wsExNoActivate);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
