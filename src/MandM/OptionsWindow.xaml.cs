using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MandM.Models;
using MandM.Services;

namespace MandM;

public partial class OptionsWindow : Window
{
    private static readonly (string Hex, string Name)[] ColorPresets =
    [
        ("#FF5522", "Orange"),
        ("#FFE14A", "Yellow"),
        ("#2FD8C7", "Teal"),
        ("#7C5CFC", "Purple"),
        ("#A98BFF", "Lavender"),
        ("#FF6C91", "Pink"),
        ("#7FC6FF", "Sky"),
    ];

    private readonly ObservableCollection<NamedCaptureRegion> _regions = [];
    private bool _suppressUi;

    public OptionsWindow()
    {
        InitializeComponent();
        var s = App.State.Settings;
        Services.SettingsService.MigrateAndNormalize(s);

        foreach (var region in s.CaptureRegions)
            _regions.Add(region.Clone());
        ListRegions.ItemsSource = _regions;

        TxtInterval.Text = s.OcrIntervalMs.ToString();
        TxtSettingsPath.Text = App.State.SettingsService.SettingsPath;

        var voices = WindowsTts.ListVoices();
        CmbVoice.ItemsSource = voices;
        var voiceId = s.Voice?.WindowsVoiceId ?? string.Empty;
        CmbVoice.SelectedItem = voices.FirstOrDefault(v =>
                                    string.Equals(v.Id, voiceId, StringComparison.OrdinalIgnoreCase))
                                ?? voices.FirstOrDefault();
        SldVoiceVolume.Value = Math.Clamp(s.Voice?.VolumePercent is > 0 and var vol ? vol : 100, 0, 100);
        SldVoiceVolume.ValueChanged += VoiceVolume_Changed;
        RefreshVoiceVolumeLabel();

        SldDiameter.ValueChanged += Slider_ValueChanged;
        SldThickness.ValueChanged += Slider_ValueChanged;
        SldBlinkHz.ValueChanged += Slider_ValueChanged;
        SldSecondDiameter.ValueChanged += Slider_ValueChanged;
        ChkBlink.Checked += Blink_Changed;
        ChkBlink.Unchecked += Blink_Changed;
        ChkSecondRing.Checked += SecondRing_Changed;
        ChkSecondRing.Unchecked += SecondRing_Changed;
        TxtColorHex.TextChanged += ColorHex_TextChanged;

        BuildColorSwatches();
        LoadMouseHighlight(s.MouseHighlight ?? new MouseHighlightSettings());
    }

    private void BuildColorSwatches()
    {
        ColorSwatches.Children.Clear();
        foreach (var (hex, name) in ColorPresets)
        {
            var brush = new SolidColorBrush(ParseColor(hex));
            brush.Freeze();
            var btn = new Button
            {
                Style = (Style)FindResource("ColorSwatchButton"),
                Tag = brush,
                ToolTip = name,
                DataContext = hex
            };
            btn.Click += (_, _) =>
            {
                TxtColorHex.Text = hex;
                RefreshColorPreview();
            };
            ColorSwatches.Children.Add(btn);
        }
    }

    private void LoadMouseHighlight(MouseHighlightSettings mh)
    {
        _suppressUi = true;
        ChkMouseEnabled.IsChecked = mh.Enabled;
        TxtColorHex.Text = NormalizeHex(mh.ColorHex);
        SldDiameter.Value = Math.Clamp(mh.Diameter <= 0 ? 48 : mh.Diameter, 20, 120);
        SldThickness.Value = Math.Clamp(mh.Thickness <= 0 ? 3 : mh.Thickness, 1, 10);
        ChkBlink.IsChecked = mh.Blink;
        SldBlinkHz.Value = Math.Clamp(mh.BlinkHz <= 0 ? 2 : mh.BlinkHz, 0.5, 6);
        ChkSecondRing.IsChecked = mh.SecondRing;
        SldSecondDiameter.Value = Math.Clamp(mh.SecondDiameter <= 0 ? 84 : mh.SecondDiameter, 40, 200);
        _suppressUi = false;
        RefreshSliderLabels();
        RefreshColorPreview();
        Blink_Changed(this, new RoutedEventArgs());
        SecondRing_Changed(this, new RoutedEventArgs());
    }

    private MouseHighlightSettings ReadMouseHighlight() => new()
    {
        Enabled = ChkMouseEnabled.IsChecked == true,
        ColorHex = NormalizeHex(TxtColorHex.Text),
        Diameter = SldDiameter.Value,
        Thickness = SldThickness.Value,
        Opacity = 0.85,
        Blink = ChkBlink.IsChecked == true,
        BlinkHz = SldBlinkHz.Value,
        SecondRing = ChkSecondRing.IsChecked == true,
        SecondDiameter = SldSecondDiameter.Value
    };

    private void RefreshSliderLabels()
    {
        TxtDiameter.Text = $"{SldDiameter.Value:0} px";
        TxtThickness.Text = $"{SldThickness.Value:0} px";
        TxtBlinkHz.Text = $"{SldBlinkHz.Value:0.0}/s";
        TxtSecondDiameter.Text = $"{SldSecondDiameter.Value:0} px";
    }

    private void RefreshColorPreview()
    {
        var hex = NormalizeHex(TxtColorHex.Text);
        var color = ParseColor(hex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        SwatchPreview.Background = brush;
        var match = ColorPresets.FirstOrDefault(p =>
            string.Equals(p.Hex, hex, StringComparison.OrdinalIgnoreCase));
        TxtColorName.Text = match.Hex is null ? "Custom" : match.Name;
    }

    private void ColorHex_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressUi) return;
        RefreshColorPreview();
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressUi) return;
        RefreshSliderLabels();
    }

    private void Blink_Changed(object sender, RoutedEventArgs e)
    {
        if (SldBlinkHz is null) return;
        var on = ChkBlink.IsChecked == true;
        SldBlinkHz.IsEnabled = on;
        BlinkSpeedPanel.Opacity = on ? 1 : 0.45;
    }

    private void SecondRing_Changed(object sender, RoutedEventArgs e)
    {
        if (SldSecondDiameter is null) return;
        var on = ChkSecondRing.IsChecked == true;
        SldSecondDiameter.IsEnabled = on;
        SecondSizePanel.Opacity = on ? 1 : 0.45;
    }

    private void RefreshRegionList()
    {
        var selected = ListRegions.SelectedItem;
        ListRegions.Items.Refresh();
        ListRegions.SelectedItem = selected;
    }

    private void VoiceVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        RefreshVoiceVolumeLabel();

    private void RefreshVoiceVolumeLabel()
    {
        if (TxtVoiceVolume is null) return;
        TxtVoiceVolume.Text = $"{SldVoiceVolume.Value:0}%";
    }

    private void PreviewVoice_Click(object sender, RoutedEventArgs e)
    {
        var previous = App.State.Alarms.CurrentVoiceSettings;
        App.State.Alarms.UpdateVoiceSettings(ReadVoiceSettings());
        App.State.Alarms.PreviewVoice();
        App.State.Alarms.UpdateVoiceSettings(previous);
    }

    private VoiceSettings ReadVoiceSettings() => new()
    {
        WindowsVoiceId = (CmbVoice.SelectedItem as VoiceOption)?.Id ?? string.Empty,
        VolumePercent = (int)Math.Round(SldVoiceVolume.Value)
    };

    private void AddRegion_Click(object sender, RoutedEventArgs e)
    {
        var name = NamePromptWindow.Prompt(this, "Add capture region", "Region name", "New Region");
        if (name is null) return;

        var region = new NamedCaptureRegion { Name = name };
        _regions.Add(region);
        ListRegions.SelectedItem = region;
    }

    private void RenameRegion_Click(object sender, RoutedEventArgs e)
    {
        if (ListRegions.SelectedItem is not NamedCaptureRegion selected)
        {
            MessageBox.Show(this, "Select a region to rename.", "MandM",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var name = NamePromptWindow.Prompt(this, "Rename region", "Region name", selected.Name);
        if (name is null) return;
        selected.Name = name;
        RefreshRegionList();
    }

    private void SetRegion_Click(object sender, RoutedEventArgs e)
    {
        if (ListRegions.SelectedItem is not NamedCaptureRegion selected)
        {
            MessageBox.Show(this, "Select a region, then click Set… to drag its rectangle.", "MandM",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var picked = RegionPickerWindow.Pick(this, $"Select {selected.Name} region");
        if (picked is null) return;
        selected.Region = picked;
        RefreshRegionList();
    }

    private void RemoveRegion_Click(object sender, RoutedEventArgs e)
    {
        if (ListRegions.SelectedItem is not NamedCaptureRegion selected)
        {
            MessageBox.Show(this, "Select a region to remove.", "MandM",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_regions.Count <= 1)
        {
            MessageBox.Show(this, "Keep at least one capture region.", "MandM",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(this,
            $"Remove region “{selected.Name}”?",
            "MandM", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        _regions.Remove(selected);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtInterval.Text.Trim(), out var interval) || interval < 250 || interval > 10000)
        {
            MessageBox.Show(this, "OCR interval must be between 250 and 10000 ms.", "MandM",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_regions.Count == 0)
        {
            MessageBox.Show(this, "Add at least one capture region.", "MandM",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        App.State.Settings.CaptureRegions = _regions.Select(r => r.Clone()).ToList();
        App.State.Settings.OcrIntervalMs = interval;
        App.State.Settings.Voice = ReadVoiceSettings();
        App.State.Settings.MouseHighlight = ReadMouseHighlight();

        // Mirror legacy fields from named list.
        var abilities = App.State.Settings.FindRegionByName("Abilities");
        App.State.Settings.AbilitiesRegion = abilities?.Region.Clone() ?? new CaptureRegion();
        var combat = App.State.Settings.FindRegionByName("Combat Text");
        App.State.Settings.CombatTextRegion = combat?.Region.Clone() ?? new CaptureRegion();

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

    private static string NormalizeHex(string? value)
    {
        var hex = (value ?? "#FF5522").Trim();
        if (!hex.StartsWith('#')) hex = "#" + hex;
        return hex.Length == 7 ? hex.ToUpperInvariant() : "#FF5522";
    }

    private static Color ParseColor(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length == 6 &&
            byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
            return Color.FromRgb(r, g, b);
        return Color.FromRgb(255, 85, 34);
    }
}
