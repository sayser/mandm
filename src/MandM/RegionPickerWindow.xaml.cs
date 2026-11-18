using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MandM.Models;

namespace MandM;

public partial class RegionPickerWindow : Window
{
    private static readonly Brush IdleStroke = Freeze(Color.FromArgb(0xE6, 0x2E, 0xE6, 0xC8));
    private static readonly Brush IdleOuter = Freeze(Color.FromArgb(0x66, 0x2E, 0xE6, 0xC8));
    private static readonly Brush DragStroke = Freeze(Color.FromArgb(0xF0, 0xA9, 0x8B, 0xFF));
    private static readonly Brush DragOuter = Freeze(Color.FromArgb(0x70, 0xA9, 0x8B, 0xFF));

    private Point _start;
    private bool _dragging;
    private bool _coverVirtualScreen = true;

    public CaptureRegion? Result { get; private set; }

    public RegionPickerWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            if (_coverVirtualScreen)
                CoverVirtualScreen();
        };
        Loaded += (_, _) =>
        {
            if (_coverVirtualScreen)
                CoverVirtualScreen();
            Keyboard.Focus(Root);
            PlaceCursor(new Point(ActualWidth / 2, ActualHeight / 2));
        };
    }

    /// <summary>Non-interactive preview used by --screenshot (fixed window, not full virtual desktop).</summary>
    public void ShowPreviewDemo(Point cursor, Rect selection)
    {
        _coverVirtualScreen = false;
        Cursor = Cursors.Arrow;
        Hint.Text = "Select Combat Text region — drag to select, Esc to cancel";
        Selection.Visibility = Visibility.Visible;
        CanvasSet(selection.X, selection.Y, selection.Width, selection.Height);
        SetDraggingLook(true);
        PlaceCursor(cursor);
    }

    public static CaptureRegion? Pick(Window owner, string hint)
    {
        // Do not Owner-maximize onto one monitor — span the full virtual desktop.
        var picker = new RegionPickerWindow
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.Manual
        };
        picker.Hint.Text = $"{hint} — drag on any monitor, Esc to cancel";
        return picker.ShowDialog() == true ? picker.Result : null;
    }

    /// <summary>
    /// Cover every monitor (virtual desktop). Maximized only covers the owner's screen.
    /// </summary>
    private void CoverVirtualScreen()
    {
        WindowState = WindowState.Normal;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        // Keep the picker above the owner and on the correct Z-order across monitors.
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _start = e.GetPosition(Root);
        Selection.Visibility = Visibility.Visible;
        CanvasSet(_start.X, _start.Y, 0, 0);
        SetDraggingLook(true);
        PlaceCursor(_start);
        Root.CaptureMouse();
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(Root);
        PlaceCursor(pos);

        if (!_dragging) return;
        var x = Math.Min(_start.X, pos.X);
        var y = Math.Min(_start.Y, pos.Y);
        var w = Math.Abs(pos.X - _start.X);
        var h = Math.Abs(pos.Y - _start.Y);
        CanvasSet(x, y, w, h);
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Root.ReleaseMouseCapture();
        SetDraggingLook(false);

        var pos = e.GetPosition(Root);
        var x = Math.Min(_start.X, pos.X);
        var y = Math.Min(_start.Y, pos.Y);
        var w = Math.Abs(pos.X - _start.X);
        var h = Math.Abs(pos.Y - _start.Y);

        if (w < 8 || h < 8)
        {
            DialogResult = false;
            Close();
            return;
        }

        // Virtual-desktop device pixels for GDI CopyFromScreen (works across monitors).
        Result = ToVirtualScreenRegion(x, y, w, h);
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// Convert picker-local DIPs to virtual-screen pixels used by Graphics.CopyFromScreen.
    /// </summary>
    private CaptureRegion ToVirtualScreenRegion(double x, double y, double w, double h)
    {
        // PointToScreen returns device pixels in the virtual desktop coordinate space.
        var topLeft = PointToScreen(new Point(x, y));
        var bottomRight = PointToScreen(new Point(x + w, y + h));

        var left = (int)Math.Round(Math.Min(topLeft.X, bottomRight.X));
        var top = (int)Math.Round(Math.Min(topLeft.Y, bottomRight.Y));
        var width = Math.Max(1, (int)Math.Round(Math.Abs(bottomRight.X - topLeft.X)));
        var height = Math.Max(1, (int)Math.Round(Math.Abs(bottomRight.Y - topLeft.Y)));

        return new CaptureRegion
        {
            X = left,
            Y = top,
            Width = width,
            Height = height
        };
    }

    private void Root_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    private void PlaceCursor(Point pos)
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
        Selection.Width = w;
        Selection.Height = h;
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
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
