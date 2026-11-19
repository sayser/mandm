using System.Windows;
using MandM.Models;

namespace MandM;

public partial class TimerEditWindow : Window
{
    public UserTimerDefinition Result { get; private set; }

    public TimerEditWindow(UserTimerDefinition def)
    {
        InitializeComponent();
        Result = def;
        TxtName.Text = def.Name;
        var total = Math.Max(0, def.DurationSeconds);
        TxtDurationMin.Text = (total / 60).ToString();
        TxtDurationSec.Text = (total % 60).ToString();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            MessageBox.Show(this, "Name is required.", "MandM", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(TxtDurationMin.Text.Trim(), out var minutes) || minutes < 0)
        {
            MessageBox.Show(this, "Minutes must be zero or a positive whole number.", "MandM",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(TxtDurationSec.Text.Trim(), out var seconds) || seconds < 0 || seconds > 59)
        {
            MessageBox.Show(this, "Seconds must be a whole number from 0 to 59.", "MandM",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var totalSeconds = minutes * 60 + seconds;
        if (totalSeconds <= 0)
        {
            MessageBox.Show(this, "Duration must be at least 1 second.", "MandM",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result.Name = TxtName.Text.Trim();
        Result.DurationSeconds = totalSeconds;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
