using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MandM.Models;

namespace MandM;

public partial class RegionPickerWindow : Window
{
    private static readonly Brush IdleStroke = Freeze(Color.FromArgb(0xE6, 0x2E, 0xE6, 0xC8));
    private static readonly Brush IdleOuter = Freeze(Color.FromArgb(0x66, 0x2E, 0xE6, 0xC8));
    private static readonly Brush DragStroke = Freeze(Color.FromArgb(0xF0, 0xA9, 0x8B, 0xFF));
    private static readonly Brush DragOuter = Freeze(Color.FromArgb(0x70, 0xA9, 0x8B, 0xFF));

    private readonly DispatcherTimer _pollTimer;
    private POINT _startScreen;
    private bool _dragging;
    private bool _buttonWasDown;
    private bool _coverVirtualScreen = true;
    private bool _finished;
    private bool _inputArmed; // wait for Set… click to release before starting a drag

    public CaptureRegion? Result { get; private set; }

    public RegionPickerWindow()
    {
        InitializeComponent();

        // Poll Win32 cursor/button state — WPF mouse routing often fails for
        // full-virtual-desktop transparent overlays (and loses to click-through Topmost siblings).
        _pollTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(8)
        };
        _pollTimer.Tick += (_, _) => PollInput();

        SourceInitialized += (_, _) =>
        {
            if (_coverVirtualScreen)
                CoverVirtualScreen();
        };
        Loaded += (_, _) =>
        {
            if (_coverVirtualScreen)
                CoverVirtualScreen();
            Activate();
            Keyboard.Focus(Root);
            if (_coverVirtualScreen)
                _pollTimer.Start();
            FollowCursorFromWin32(force: true);
        };
        Closed += (_, _) =>
        {
            _pollTimer.Stop();
        };
    }

    /// <summary>Non-interactive preview used by --screenshot (fixed window, not full virtual desktop).</summary>
    public void ShowPreviewDemo(Point cursor, Rect selection)
    {
        _coverVirtualScreen = false;
        _pollTimer.Stop();
        Cursor = Cursors.Arrow;
        Hint.Text = "Select Combat Text region — drag to select, Esc to cancel";
        Selection.Visibility = Visibility.Visible;
        CanvasSet(selection.X, selection.Y, selection.Width, selection.Height);
        SetDraggingLook(true);
        PlaceCursorLocal(cursor);
    }

    public static CaptureRegion? Pick(Window owner, string hint)
    {
        // Hide the always-on mouse-highlight overlay: it is Topmost + click-through and
        // steals Z-order so the picker never sees moves/drags.
        var main = FindMainWindow(owner);
        main?.SuspendMouseHighlight(true);
        try
        {
            var picker = new RegionPickerWindow
            {
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            picker.Hint.Text = $"{hint} — drag on any monitor, Esc to cancel";
            return picker.ShowDialog() == true ? picker.Result : null;
        }
        finally
        {
            main?.SuspendMouseHighlight(false);
        }
    }

    private static MainWindow? FindMainWindow(Window owner)
    {
        if (owner is MainWindow main) return main;
        if (owner.Owner is MainWindow owned) return owned;
        return Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
    }

    private void CoverVirtualScreen()
    {
        WindowState = WindowState.Normal;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
    }

    private void PollInput()
    {
        if (_finished || !_coverVirtualScreen) return;

        FollowCursorFromWin32(force: false);

        if ((GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0)
        {
            Finish(cancel: true);
            return;
        }

        var down = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;

        // Ignore the mouse-down that opened Set… until it is released.
        if (!_inputArmed)
        {
            if (!down)
                _inputArmed = true;
            _buttonWasDown = down;
            return;
        }

        if (down && !_buttonWasDown)
            BeginDrag();
        else if (down && _dragging)
            UpdateDrag();
        else if (!down && _buttonWasDown && _dragging)
            EndDrag();

        _buttonWasDown = down;
    }

    private void FollowCursorFromWin32(bool force)
    {
        if (!GetCursorPos(out var pt)) return;
        var local = ScreenToLocal(pt);
        PlaceCursorLocal(local);
        if (_dragging)
            UpdateDragSelection(local);
    }

    private void BeginDrag()
    {
        if (!GetCursorPos(out _startScreen)) return;
        _dragging = true;
        Selection.Visibility = Visibility.Visible;
        SetDraggingLook(true);
        var local = ScreenToLocal(_startScreen);
        CanvasSet(local.X, local.Y, 0, 0);
        PlaceCursorLocal(local);
    }

    private void UpdateDrag()
    {
        if (!GetCursorPos(out var pt)) return;
        UpdateDragSelection(ScreenToLocal(pt));
    }

    private void UpdateDragSelection(Point localEnd)
    {
        var start = ScreenToLocal(_startScreen);
        var x = Math.Min(start.X, localEnd.X);
        var y = Math.Min(start.Y, localEnd.Y);
        var w = Math.Abs(localEnd.X - start.X);
        var h = Math.Abs(localEnd.Y - start.Y);
        CanvasSet(x, y, w, h);
    }

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        SetDraggingLook(false);

        if (!GetCursorPos(out var end))
        {
            Finish(cancel: true);
            return;
        }

        var x = Math.Min(_startScreen.X, end.X);
        var y = Math.Min(_startScreen.Y, end.Y);
        var w = Math.Abs(end.X - _startScreen.X);
        var h = Math.Abs(end.Y - _startScreen.Y);

        if (w < 8 || h < 8)
        {
            Finish(cancel: true);
            return;
        }

        // Virtual-desktop device pixels — same space Graphics.CopyFromScreen uses.
        Result = new CaptureRegion
        {
            X = x,
            Y = y,
            Width = Math.Max(1, w),
            Height = Math.Max(1, h)
        };
        Finish(cancel: false);
    }

    private void Finish(bool cancel)
    {
        if (_finished) return;
        _finished = true;
        _pollTimer.Stop();
        try
        {
            DialogResult = !cancel;
        }
        catch
        {
            // already closing
        }

        Close();
    }

    private Point ScreenToLocal(POINT screen)
    {
        // Device pixels → DIPs relative to this window.
        var device = new Point(screen.X, screen.Y);
        if (PresentationSource.FromVisual(this) is { CompositionTarget: { } target })
            device = target.TransformFromDevice.Transform(device);
        return new Point(device.X - Left, device.Y - Top);
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Prefer Win32 poll path; keep WPF as a secondary trigger if it fires.
        if (!_dragging)
            BeginDrag();
        e.Handled = true;
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        // Visual follow is handled by the poll timer; keep handler for focus/compatibility.
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging)
            EndDrag();
        e.Handled = true;
    }

    private void Root_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Finish(cancel: true);
    }

    private void PlaceCursorLocal(Point pos)
    {
        InnerTx.X = pos.X - CursorInner.Width / 2;
        InnerTx.Y = pos.Y - CursorInner.Height / 2;
        OuterTx.X = pos.X - CursorOuter.Width / 2;
        OuterTx.Y = pos.Y - CursorOuter.Height / 2;
        DotTx.X = pos.X - CursorDot.Width / 2;
        DotTx.Y = pos.Y - CursorDot.Height / 2;
    }

    private void SetDraggingLook(bool dragging)
    {
        CursorInner.Stroke = dragging ? DragStroke : IdleStroke;
        CursorOuter.Stroke = dragging ? DragOuter : IdleOuter;
        Selection.Stroke = dragging ? DragStroke : IdleStroke;
    }

    private void CanvasSet(double x, double y, double w, double h)
    {
        Selection.Width = Math.Max(0, w);
        Selection.Height = Math.Max(0, h);
        Selection.Margin = new Thickness(x, y, 0, 0);
        Selection.HorizontalAlignment = HorizontalAlignment.Left;
        Selection.VerticalAlignment = VerticalAlignment.Top;
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int VK_LBUTTON = 0x01;
    private const int VK_ESCAPE = 0x1B;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
