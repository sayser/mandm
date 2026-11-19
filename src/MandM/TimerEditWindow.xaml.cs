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
        TxtDurationMin.Text = (def.DurationSeconds / 60.0).ToString("0.##");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            MessageBox.Show(this, "Name is required.", "MandM", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!double.TryParse(TxtDurationMin.Text.Trim(), out var minutes) || minutes <= 0)
        {
            MessageBox.Show(this, "Duration must be a positive number of minutes.", "MandM",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result.Name = TxtName.Text.Trim();
        Result.DurationSeconds = Math.Max(1, (int)Math.Round(minutes * 60));
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
