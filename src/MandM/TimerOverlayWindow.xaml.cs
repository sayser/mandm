using System.Collections.Specialized;
using System.Windows;
using MandM.Services;

namespace MandM;

public partial class TimerOverlayWindow : Window
{
    private readonly UserTimerService _timers;
    private readonly OverlayDragHelper _drag;
    private bool _suppressBoundsEvent;

    public event Action? BoundsChanged;

    public TimerOverlayWindow(UserTimerService timers)
    {
        InitializeComponent();
        _timers = timers;
        _drag = new OverlayDragHelper(this, DragHandle);
        ListTimers.ItemsSource = timers.Timers;
        timers.Timers.CollectionChanged += TimersOnCollectionChanged;
        UpdateEmptyState();
        Closed += (_, _) => timers.Timers.CollectionChanged -= TimersOnCollectionChanged;
    }

    public void ApplyBounds(double left, double top, double width, double height)
    {
        _suppressBoundsEvent = true;
        try
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }
        finally
        {
            _suppressBoundsEvent = false;
        }
    }

    private void TimersOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.Invoke(UpdateEmptyState);

    private void UpdateEmptyState() =>
        TxtEmpty.Visibility = _timers.Timers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void Overlay_BoundsChanged(object sender, EventArgs e)
    {
        if (_suppressBoundsEvent) return;
        BoundsChanged?.Invoke();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
