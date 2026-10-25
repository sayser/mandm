using System.Windows;
using MandM.Models;

namespace MandM;

public partial class OptionsWindow : Window
{
    private CaptureRegion _abilities;
    private CaptureRegion _combat;

    public OptionsWindow()
    {
        InitializeComponent();
        var s = App.State.Settings;
        _abilities = s.AbilitiesRegion.Clone();
        _combat = s.CombatTextRegion.Clone();
        TxtAbilities.Text = _abilities.ToString();
        TxtCombat.Text = _combat.ToString();
        TxtInterval.Text = s.OcrIntervalMs.ToString();
        TxtSettingsPath.Text = App.State.SettingsService.SettingsPath;
    }

    private void SetAbilities_Click(object sender, RoutedEventArgs e)
    {
        var region = RegionPickerWindow.Pick(this, "Select Abilities region");
        if (region is null) return;
        _abilities = region;
        TxtAbilities.Text = _abilities.ToString();
    }

    private void SetCombat_Click(object sender, RoutedEventArgs e)
    {
        var region = RegionPickerWindow.Pick(this, "Select Combat Text region");
        if (region is null) return;
        _combat = region;
        TxtCombat.Text = _combat.ToString();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtInterval.Text.Trim(), out var interval) || interval < 250 || interval > 10000)
        {
            MessageBox.Show(this, "OCR interval must be between 250 and 10000 ms.", "MandM",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        App.State.Settings.AbilitiesRegion = _abilities;
        App.State.Settings.CombatTextRegion = _combat;
        App.State.Settings.OcrIntervalMs = interval;
        App.State.SaveSettings();
        App.State.Monitor.Start();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
