using System.Windows;
using System.Windows.Input;
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
    public CaptureRegion? Result { get; private set; }

    public RegionPickerWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Keyboard.Focus(Root);
            PlaceCursor(new Point(ActualWidth / 2, ActualHeight / 2));
        };
    }

    /// <summary>Non-interactive preview used by --screenshot.</summary>
    public void ShowPreviewDemo(Point cursor, Rect selection)
    {
        Cursor = Cursors.Arrow;
        Hint.Text = "Select Combat Text region — drag to select, Esc to cancel";
        Selection.Visibility = Visibility.Visible;
        CanvasSet(selection.X, selection.Y, selection.Width, selection.Height);
        SetDraggingLook(true);
        PlaceCursor(cursor);
    }

    public static CaptureRegion? Pick(Window owner, string hint)
    {
        var picker = new RegionPickerWindow { Owner = owner };
        picker.Hint.Text = $"{hint} — drag to select, Esc to cancel";
        return picker.ShowDialog() == true ? picker.Result : null;
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

        // Convert WPF DIPs to physical screen pixels for GDI CopyFromScreen.
        var topLeft = PointToScreen(new Point(x, y));
        var bottomRight = PointToScreen(new Point(x + w, y + h));
        Result = new CaptureRegion
        {
            X = (int)Math.Round(topLeft.X),
            Y = (int)Math.Round(topLeft.Y),
            Width = Math.Max(1, (int)Math.Round(bottomRight.X - topLeft.X)),
            Height = Math.Max(1, (int)Math.Round(bottomRight.Y - topLeft.Y))
        };
        DialogResult = true;
        Close();
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
}
