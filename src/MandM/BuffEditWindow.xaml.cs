using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MandM.Models;
using MandM.Services;

namespace MandM;

public partial class BuffEditWindow : Window
{
    public BuffDefinition Result { get; private set; }

    public BuffEditWindow(BuffDefinition def)
    {
        InitializeComponent();
        Result = def;
        SettingsService.MigrateAndNormalize(App.State.Settings);

        var regions = App.State.Settings.CaptureRegions.ToList();
        BindRegionCombo(CmbCastRegion, regions, def.CastRegionId);
        BindRegionCombo(CmbLandRegion, regions, def.LandRegionId);
        BindRegionCombo(CmbFadeRegion, regions, def.FadeRegionId);
        BindRegionCombo(CmbWarnRegion, regions, def.WarnRegionId);

        ChkEnabled.IsChecked = def.Enabled;
        TxtName.Text = def.Name;
        TxtDurationMin.Text = (def.DurationSeconds / 60.0).ToString("0.##");
        TxtWarnSec.Text = def.WarnSeconds.ToString();
        TxtCast.Text = def.CastText;
        TxtLand.Text = def.LandText;
        TxtFade.Text = def.FadeText;
        TxtWarn.Text = def.WarnText;
        CmbSound.ItemsSource = AlarmService.BuiltInSounds;
        CmbSound.Text = def.AlarmSound;
    }

    private static void BindRegionCombo(ComboBox combo, List<NamedCaptureRegion> regions, string? selectedId)
    {
        combo.ItemsSource = regions;
        var fallback = regions.FirstOrDefault(r =>
                            string.Equals(r.Name, "Combat Text", StringComparison.OrdinalIgnoreCase))
                       ?? regions.FirstOrDefault();
        var match = regions.FirstOrDefault(r => string.Equals(r.Id, selectedId, StringComparison.Ordinal))
                    ?? fallback;
        combo.SelectedItem = match;
    }

    private static string SelectedRegionId(ComboBox combo) =>
        (combo.SelectedItem as NamedCaptureRegion)?.Id
        ?? combo.SelectedValue as string
        ?? string.Empty;

    private void BrowseSound_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Wave audio (*.wav)|*.wav|All files (*.*)|*.*",
            Title = "Select alarm sound"
        };
        if (dlg.ShowDialog(this) == true)
            CmbSound.Text = dlg.FileName;
    }

    private void TestSound_Click(object sender, RoutedEventArgs e) =>
        App.State.Alarms.Play(CmbSound.Text);

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

        if (!int.TryParse(TxtWarnSec.Text.Trim(), out var warn) || warn < 0)
        {
            MessageBox.Show(this, "Warn time must be a non-negative number of seconds.", "MandM",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(TxtLand.Text))
        {
            MessageBox.Show(this, "Land text is required (starts the timer).", "MandM",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (App.State.Settings.CaptureRegions.Count == 0)
        {
            MessageBox.Show(this, "Add a capture region in Options first.", "MandM",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result.Enabled = ChkEnabled.IsChecked == true;
        Result.Name = TxtName.Text.Trim();
        Result.DurationSeconds = (int)Math.Round(minutes * 60);
        Result.WarnSeconds = warn;
        Result.CastText = TxtCast.Text.Trim();
        Result.LandText = TxtLand.Text.Trim();
        Result.FadeText = TxtFade.Text.Trim();
        Result.WarnText = TxtWarn.Text.Trim();
        Result.CastRegionId = SelectedRegionId(CmbCastRegion);
        Result.LandRegionId = SelectedRegionId(CmbLandRegion);
        Result.FadeRegionId = SelectedRegionId(CmbFadeRegion);
        Result.WarnRegionId = SelectedRegionId(CmbWarnRegion);
        Result.AlarmSound = string.IsNullOrWhiteSpace(CmbSound.Text) ? "Exclamation" : CmbSound.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
