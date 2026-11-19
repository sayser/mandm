using System.Windows;
using System.Windows.Controls;
using MandM.Models;
using MandM.Services;

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
        TxtVoice.Text = def.VoiceText;

        CmbAlertMode.ItemsSource = new[]
        {
            new { Mode = AlertMode.Sound, Label = "Sound (wav / system)" },
            new { Mode = AlertMode.Voice, Label = "Voice (spoken text)" }
        };
        CmbAlertMode.DisplayMemberPath = "Label";
        CmbAlertMode.SelectedValuePath = "Mode";
        CmbAlertMode.SelectedValue = AlertModeOptions.Normalize(def.AlertMode);

        CmbSound.ItemsSource = AlarmService.BuiltInSounds;
        CmbSound.Text = string.IsNullOrWhiteSpace(def.AlarmSound) ? "Exclamation" : def.AlarmSound;
        RefreshAlertPanels();
    }

    private AlertMode SelectedAlertMode() =>
        CmbAlertMode.SelectedValue is AlertMode mode
            ? AlertModeOptions.Normalize(mode)
            : AlertMode.Sound;

    private void AlertMode_Changed(object sender, SelectionChangedEventArgs e) => RefreshAlertPanels();

    private void RefreshAlertPanels()
    {
        if (PanelSound is null || PanelVoice is null) return;
        var voice = SelectedAlertMode() == AlertMode.Voice;
        PanelSound.Visibility = voice ? Visibility.Collapsed : Visibility.Visible;
        PanelVoice.Visibility = voice ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TestAlert_Click(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(TxtName.Text) ? "Timer" : TxtName.Text.Trim();
        App.State.Alarms.PlayAlert(
            SelectedAlertMode(),
            CmbSound.Text,
            TxtVoice.Text,
            $"{name} finished");
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
        Result.AlertMode = SelectedAlertMode();
        Result.AlarmSound = string.IsNullOrWhiteSpace(CmbSound.Text) ? "Exclamation" : CmbSound.Text.Trim();
        Result.VoiceText = TxtVoice.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
