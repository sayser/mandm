using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MandM.Models;

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

    private CaptureRegion _abilities;
    private CaptureRegion _combat;
    private bool _suppressUi;

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
        App.State.Settings.MouseHighlight = ReadMouseHighlight();
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
