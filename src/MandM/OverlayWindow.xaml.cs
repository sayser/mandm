using System.Collections.Specialized;
using System.Windows;
using MandM.Services;

namespace MandM;

public partial class OverlayWindow : Window
{
    private readonly BuffTrackerService _tracker;
    private readonly OverlayDragHelper _drag;
    private bool _suppressBoundsEvent;

    public event Action? BoundsChanged;

    public OverlayWindow(BuffTrackerService tracker)
    {
        InitializeComponent();
        _tracker = tracker;
        _drag = new OverlayDragHelper(this, DragHandle);
        ListActive.ItemsSource = tracker.ActiveBuffs;
        tracker.ActiveBuffs.CollectionChanged += ActiveBuffsOnCollectionChanged;
        UpdateEmptyState();
        Closed += (_, _) => tracker.ActiveBuffs.CollectionChanged -= ActiveBuffsOnCollectionChanged;
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

    private void ActiveBuffsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.Invoke(UpdateEmptyState);

    private void UpdateEmptyState() =>
        TxtEmpty.Visibility = _tracker.ActiveBuffs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void Overlay_BoundsChanged(object sender, EventArgs e)
    {
        if (_suppressBoundsEvent) return;
        BoundsChanged?.Invoke();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
