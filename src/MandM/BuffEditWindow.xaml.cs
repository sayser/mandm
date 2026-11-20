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
        if (def.IsDebuff)
            KindDebuff.IsChecked = true;
        else
            KindBuff.IsChecked = true;
        ApplyKindUi();
        TxtName.Text = def.Name;
        TxtDurationMin.Text = (def.DurationSeconds / 60.0).ToString("0.##");
        TxtWarnSec.Text = def.WarnSeconds.ToString();
        TxtCast.Text = def.CastText;
        TxtLand.Text = def.LandText;
        TxtFade.Text = def.FadeText;
        TxtWarn.Text = def.WarnText;
        TxtWarnVoice.Text = def.WarnVoiceText;
        TxtFadeVoice.Text = def.FadeVoiceText;
        TxtFadeVoiceDebuff.Text = def.FadeVoiceText;

        var alertChoices = new[]
        {
            new { Mode = AlertMode.Sound, Label = "Sound (wav / system)" },
            new { Mode = AlertMode.Voice, Label = "Voice (spoken text)" }
        };
        CmbAlertMode.ItemsSource = alertChoices;
        CmbAlertMode.DisplayMemberPath = "Label";
        CmbAlertMode.SelectedValuePath = "Mode";
        CmbAlertMode.SelectedValue = AlertModeOptions.Normalize(def.AlertMode);

        CmbFadeAlertMode.ItemsSource = new[]
        {
            new { Mode = AlertMode.Sound, Label = "Sound (wav / system)" },
            new { Mode = AlertMode.Voice, Label = "Voice (spoken text)" }
        };
        CmbFadeAlertMode.DisplayMemberPath = "Label";
        CmbFadeAlertMode.SelectedValuePath = "Mode";
        CmbFadeAlertMode.SelectedValue = AlertModeOptions.Normalize(def.ResolvedFadeAlertMode);

        CmbSound.ItemsSource = AlarmService.BuiltInSounds;
        CmbSound.Text = def.AlarmSound;
        CmbFadeSound.ItemsSource = AlarmService.BuiltInSounds;
        CmbFadeSound.Text = string.IsNullOrWhiteSpace(def.FadeAlarmSound)
            ? def.AlarmSound
            : def.FadeAlarmSound;
        RefreshAlertPanels();
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

    private AlertMode SelectedAlertMode() =>
        CmbAlertMode.SelectedValue is AlertMode mode
            ? AlertModeOptions.Normalize(mode)
            : AlertMode.Sound;

    private void AlertMode_Changed(object sender, SelectionChangedEventArgs e) => RefreshAlertPanels();

    private void FadeAlertMode_Changed(object sender, SelectionChangedEventArgs e) => RefreshFadeAlertPanels();

    private AlertMode SelectedFadeAlertMode() =>
        CmbFadeAlertMode?.SelectedValue is AlertMode mode
            ? AlertModeOptions.Normalize(mode)
            : AlertMode.Sound;

    private void Kind_Changed(object sender, RoutedEventArgs e) => ApplyKindUi();

    private bool IsDebuffKind => KindDebuff?.IsChecked == true;

    private void ApplyKindUi()
    {
        if (PanelTiming is null || PanelCast is null || PanelWarn is null || LblCast is null)
            return;

        var debuff = IsDebuffKind;
        PanelTiming.Visibility = debuff ? Visibility.Collapsed : Visibility.Visible;
        PanelCast.Visibility = Visibility.Visible;
        PanelWarn.Visibility = debuff ? Visibility.Collapsed : Visibility.Visible;
        LblCast.Text = debuff ? "CAST TEXT (OPTIONAL — NO ALERT)" : "CAST TEXT (OPTIONAL)";
        LblLand.Text = debuff ? "LAND PHRASE (TEXT BEFORE IT IS THE MONSTER)" : "LAND TEXT (STARTS TIMER)";
        LblFade.Text = debuff ? "FADE PHRASE (OPTIONAL)" : "FADE TEXT (CLEARS TIMER)";
        if (LblAlert is not null)
            LblAlert.Text = debuff ? "LAND ALERT" : "ALERT OUTPUT";
        if (LblSound is not null)
            LblSound.Text = debuff ? "LAND SOUND" : "ALARM SOUND";
        if (BtnTestFade is not null)
            BtnTestFade.Visibility = debuff ? Visibility.Visible : Visibility.Collapsed;
        LblSubtitle.Text = debuff
            ? "Land starts tracking and plays the land alert. Fade is optional and has its own alert. Empty fade text stays until a slain line."
            : "Land starts or resets the timer. Fade clears it. Warn text alarms immediately. Each line picks a region.";
        LblMatchHint.Text = debuff
            ? "Land text is a phrase, not the whole line. “looks weak” matches “a bloated plague rat looks weak.” The words in front are the monster. Leave fade text empty for no fade alert. A fade phrase uses the fade alert, not the land voice. Cast text does not play a sound."
            : "Leave warn text empty to keep the time-based warn above. Each line only watches its region dropdown. Phrases match on word boundaries.";
        LblWarnVoice.Text = debuff ? "LAND VOICE TEXT" : "WARN VOICE TEXT";
        LblVoiceHint.Text = debuff
            ? "Voice is chosen in Options → Spoken alerts. This line is only the land alert."
            : "Voice is chosen in Options → Spoken alerts. Test uses the warn line.";
        RefreshAlertPanels();
    }

    private void RefreshAlertPanels()
    {
        if (PanelSound is null || PanelVoice is null) return;
        var voice = SelectedAlertMode() == AlertMode.Voice;
        PanelSound.Visibility = voice ? Visibility.Collapsed : Visibility.Visible;
        PanelVoice.Visibility = voice ? Visibility.Visible : Visibility.Collapsed;
        if (PanelBuffFadeVoice is not null)
            PanelBuffFadeVoice.Visibility = IsDebuffKind ? Visibility.Collapsed : Visibility.Visible;
        if (PanelFadeAlert is not null)
            PanelFadeAlert.Visibility = IsDebuffKind ? Visibility.Visible : Visibility.Collapsed;
        RefreshFadeAlertPanels();
    }

    private void RefreshFadeAlertPanels()
    {
        if (PanelFadeSound is null || PanelFadeVoice is null) return;
        var voice = SelectedFadeAlertMode() == AlertMode.Voice;
        PanelFadeSound.Visibility = voice ? Visibility.Collapsed : Visibility.Visible;
        PanelFadeVoice.Visibility = voice ? Visibility.Visible : Visibility.Collapsed;
    }

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

    private void BrowseFadeSound_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Wave audio (*.wav)|*.wav|All files (*.*)|*.*",
            Title = "Select fade sound"
        };
        if (dlg.ShowDialog(this) == true)
            CmbFadeSound.Text = dlg.FileName;
    }

    private void TestSound_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAlertMode() == AlertMode.Voice)
        {
            var name = string.IsNullOrWhiteSpace(TxtName.Text) ? "Spell" : TxtName.Text.Trim();
            var fallback = IsDebuffKind ? name : $"{name} warning";
            App.State.Alarms.PlayAlert(
                AlertMode.Voice,
                CmbSound.Text,
                TxtWarnVoice.Text,
                fallback);
        }
        else
        {
            App.State.Alarms.Play(CmbSound.Text);
        }
    }

    private void TestFade_Click(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(TxtName.Text) ? "Spell" : TxtName.Text.Trim();
        if (SelectedFadeAlertMode() == AlertMode.Voice)
        {
            App.State.Alarms.PlayAlert(
                AlertMode.Voice,
                CmbFadeSound.Text,
                TxtFadeVoiceDebuff.Text,
                $"{name} faded");
        }
        else
        {
            App.State.Alarms.Play(CmbFadeSound.Text);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            MessageBox.Show(this, "Name is required.", "MandM", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var debuff = IsDebuffKind;
        double minutes = 0;
        var warn = 0;
        if (!debuff)
        {
            if (!double.TryParse(TxtDurationMin.Text.Trim(), out minutes) || minutes <= 0)
            {
                MessageBox.Show(this, "Duration must be a positive number of minutes.", "MandM",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(TxtWarnSec.Text.Trim(), out warn) || warn < 0)
            {
                MessageBox.Show(this, "Warn time must be a non-negative number of seconds.", "MandM",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(TxtLand.Text))
        {
            MessageBox.Show(this, debuff
                    ? "Land text is required (starts the debuff and plays the alert)."
                    : "Land text is required (starts the timer).",
                "MandM", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (App.State.Settings.CaptureRegions.Count == 0)
        {
            MessageBox.Show(this, "Add a capture region in Options first.", "MandM",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result.Enabled = ChkEnabled.IsChecked == true;
        Result.Kind = debuff ? SpellKind.Debuff : SpellKind.Buff;
        Result.Name = TxtName.Text.Trim();
        if (!debuff)
        {
            Result.DurationSeconds = (int)Math.Round(minutes * 60);
            Result.WarnSeconds = warn;
        }
        Result.CastText = TxtCast.Text.Trim();
        Result.LandText = TxtLand.Text.Trim();
        Result.FadeText = TxtFade.Text.Trim();
        Result.WarnText = TxtWarn.Text.Trim();
        Result.CastRegionId = SelectedRegionId(CmbCastRegion);
        Result.LandRegionId = SelectedRegionId(CmbLandRegion);
        Result.FadeRegionId = SelectedRegionId(CmbFadeRegion);
        Result.WarnRegionId = SelectedRegionId(CmbWarnRegion);
        Result.AlertMode = SelectedAlertMode();
        Result.AlarmSound = string.IsNullOrWhiteSpace(CmbSound.Text) ? "Exclamation" : CmbSound.Text.Trim();
        Result.WarnVoiceText = TxtWarnVoice.Text.Trim();
        if (debuff)
        {
            Result.FadeAlertMode = SelectedFadeAlertMode();
            Result.FadeAlarmSound = string.IsNullOrWhiteSpace(CmbFadeSound.Text)
                ? "Exclamation"
                : CmbFadeSound.Text.Trim();
            Result.FadeVoiceText = TxtFadeVoiceDebuff.Text.Trim();
        }
        else
        {
            Result.FadeAlertMode = null;
            Result.FadeAlarmSound = string.Empty;
            Result.FadeVoiceText = TxtFadeVoice.Text.Trim();
        }
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
