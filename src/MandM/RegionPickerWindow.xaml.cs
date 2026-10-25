using System.Windows;
using System.Windows.Input;
using MandM.Models;

namespace MandM;

public partial class RegionPickerWindow : Window
{
    private Point _start;
    private bool _dragging;
    public CaptureRegion? Result { get; private set; }

    public RegionPickerWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Keyboard.Focus(Root);
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
        Root.CaptureMouse();
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var pos = e.GetPosition(Root);
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

    private void CanvasSet(double x, double y, double w, double h)
    {
        Selection.Width = w;
        Selection.Height = h;
        Selection.Margin = new Thickness(x, y, 0, 0);
        Selection.HorizontalAlignment = HorizontalAlignment.Left;
        Selection.VerticalAlignment = VerticalAlignment.Top;
    }
}
