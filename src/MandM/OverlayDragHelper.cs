using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MandM;

/// <summary>
/// Manual window drag for borderless / AllowsTransparency overlays.
/// WPF <see cref="Window.DragMove"/> often no-ops on those windows.
/// </summary>
internal sealed class OverlayDragHelper
{
    private readonly Window _window;
    private readonly UIElement _handle;
    private Point _mouseStartDip;
    private Point _windowStart;
    private bool _dragging;

    public OverlayDragHelper(Window window, UIElement handle)
    {
        _window = window;
        _handle = handle;
        handle.MouseLeftButtonDown += OnDown;
        handle.MouseMove += OnMove;
        handle.MouseLeftButtonUp += OnUp;
        handle.LostMouseCapture += (_, _) => _dragging = false;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed)
            return;
        if (FindAncestor<System.Windows.Controls.Button>(e.OriginalSource as DependencyObject) is not null)
            return;

        _dragging = true;
        _mouseStartDip = ToDip(_window.PointToScreen(e.GetPosition(_window)));
        _windowStart = new Point(_window.Left, _window.Top);
        _handle.CaptureMouse();
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || !_handle.IsMouseCaptured)
            return;

        var nowDip = ToDip(_window.PointToScreen(e.GetPosition(_window)));
        _window.Left = _windowStart.X + (nowDip.X - _mouseStartDip.X);
        _window.Top = _windowStart.Y + (nowDip.Y - _mouseStartDip.Y);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
            return;

        _dragging = false;
        if (_handle.IsMouseCaptured)
            _handle.ReleaseMouseCapture();
        e.Handled = true;
    }

    private Point ToDip(Point devicePixels)
    {
        var source = PresentationSource.FromVisual(_window);
        if (source?.CompositionTarget is null)
            return devicePixels;
        return source.CompositionTarget.TransformFromDevice.Transform(devicePixels);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }
}
