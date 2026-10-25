using System.Windows;
using System.Windows.Input;
using MandM.Services;

namespace MandM;

public partial class OverlayWindow : Window
{
    public OverlayWindow(BuffTrackerService tracker)
    {
        InitializeComponent();
        ListActive.ItemsSource = tracker.ActiveBuffs;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
