using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MandM.Models;
using MandM.Services;

namespace MandM;

public partial class MainWindow : Window
{
    private static readonly Brush OcrOnlineBrush = Freeze(Color.FromRgb(0x39, 0xE0, 0xCC));
    private static readonly Brush OcrOfflineBrush = Freeze(Color.FromRgb(0xFF, 0x5E, 0x70));
    private static readonly Brush MutedPillBrush = Freeze(Color.FromRgb(0x89, 0xA9, 0xC1));

    private OverlayWindow? _overlay;
    private TimerOverlayWindow? _timerOverlay;
    private MouseHighlightOverlayWindow? _mouseHighlight;
    private bool _mouseHighlightSuspended;
    private bool _damageRegionSync;

    public MainWindow()
    {
        InitializeComponent();
        TxtVersion.Text = $"v{UpdateCheckService.GetLocalVersion()}";
        ListActive.ItemsSource = App.State.Tracker.ActiveBuffs;
        ListTimers.ItemsSource = App.State.UserTimers.Timers;
        ListRegionOcr.ItemsSource = App.State.Monitor.RegionSnapshots;
        ListAbilities.ItemsSource = App.State.Damage.Abilities;
        AbilityDonut.ItemsSource = App.State.Damage.Abilities;
        RefreshBuffList();
        RefreshDamageRegionList();
        RefreshDamageTotals();
        UpdateOverlayButton();
        UpdateTimerOverlayButton();
        UpdateActiveEmptyState();
        ApplyMouseHighlight();
        RefreshStatusPills();

        App.State.Monitor.StatusChanged += s => Dispatcher.Invoke(() =>
        {
            TxtStatus.Text = s;
            RefreshStatusPills();
        });
        App.State.Tracker.Changed += () => Dispatcher.Invoke(() =>
        {
            RefreshBuffList();
            UpdateActiveEmptyState();
            RefreshStatusPills();
        });
        App.State.Tracker.ActiveBuffs.CollectionChanged += (_, _) =>
            Dispatcher.Invoke(() =>
            {
                UpdateActiveEmptyState();
                RefreshStatusPills();
            });
        App.State.Damage.Changed += () => Dispatcher.Invoke(RefreshDamageTotals);

        TxtStatus.Text = App.State.Ocr.IsAvailable
            ? App.State.Monitor.Status
            : App.State.Ocr.UnavailableReason ?? "OCR unavailable";
        RefreshStatusPills();

        if (App.State.Settings.OverlayEnabled)
            ShowBuffOverlay(true);
        if (App.State.Settings.TimerOverlayEnabled)
            ShowTimerOverlay(true);

        Closing += (_, _) =>
        {
            if (_overlay is not null)
            {
                PersistBuffOverlayBounds();
                _overlay.Close();
                _overlay = null;
            }

            if (_timerOverlay is not null)
            {
                PersistTimerOverlayBounds();
                _timerOverlay.Close();
                _timerOverlay = null;
            }

            _mouseHighlight?.Close();
            _mouseHighlight = null;

            App.State.SaveSettings();
        };
    }

    private void RefreshBuffList()
    {
        var selectedId = (ListBuffs.SelectedItem as BuffDefinition)?.Id;
        ListBuffs.ItemsSource = null;
        ListBuffs.ItemsSource = App.State.Settings.Buffs.ToList();
        if (selectedId is not null)
            ListBuffs.SelectedItem = App.State.Settings.Buffs.FirstOrDefault(b => b.Id == selectedId);
    }

    private void UpdateOverlayButton()
    {
        BtnOverlay.Content = App.State.Settings.OverlayEnabled ? "Overlay: On" : "Overlay: Off";
        RefreshStatusPills();
    }

    private void UpdateTimerOverlayButton()
    {
        if (BtnTimerOverlay is null) return;
        BtnTimerOverlay.Content = App.State.Settings.TimerOverlayEnabled ? "Overlay: On" : "Overlay: Off";
    }

    private void UpdateActiveEmptyState() =>
        TxtNoActive.Visibility = App.State.Tracker.ActiveBuffs.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void RefreshStatusPills()
    {
        var ocrOk = App.State.Ocr.IsAvailable;
        OcrDot.Fill = ocrOk ? OcrOnlineBrush : OcrOfflineBrush;
        TxtOcrPill.Text = ocrOk ? "Live" : "Unavailable";
        TxtOverlayPill.Text = App.State.Settings.OverlayEnabled ? "On" : "Off";
        TxtOverlayPill.Foreground = App.State.Settings.OverlayEnabled ? OcrOnlineBrush : MutedPillBrush;

        var mouseOn = App.State.Settings.MouseHighlight?.Enabled == true;
        TxtMousePill.Text = mouseOn ? "On" : "Off";
        TxtMousePill.Foreground = mouseOn ? OcrOnlineBrush : MutedPillBrush;

        TxtActivePill.Text = App.State.Tracker.ActiveBuffs.Count.ToString();
    }

    private void ApplyMouseHighlight()
    {
        try
        {
            var settings = App.State.Settings.MouseHighlight ?? new MouseHighlightSettings();
            if (_mouseHighlightSuspended)
            {
                settings = settings.Clone();
                settings.Enabled = false;
            }

            if (settings.Enabled)
            {
                _mouseHighlight ??= new MouseHighlightOverlayWindow();
                _mouseHighlight.ApplyOptions(settings);
            }
            else
            {
                _mouseHighlight?.ApplyOptions(settings);
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Mouse highlight unavailable: {ex.Message}";
        }

        RefreshStatusPills();
    }

    public void SuspendMouseHighlight(bool suspend)
    {
        _mouseHighlightSuspended = suspend;
        ApplyMouseHighlight();
    }

    private void ModuleTab_Checked(object sender, RoutedEventArgs e)
    {
        if (PanelBuffs is null || PanelDamage is null || PanelTimers is null || PanelOcr is null) return;
        PanelBuffs.Visibility = TabBuffs.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelTimers.Visibility = TabTimers.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelOcr.Visibility = TabOcr.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelDamage.Visibility = TabDamage.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void Overlay_Click(object sender, RoutedEventArgs e)
    {
        var enable = !App.State.Settings.OverlayEnabled;
        ShowBuffOverlay(enable);
        App.State.Settings.OverlayEnabled = enable;
        App.State.SaveSettings();
        UpdateOverlayButton();
    }

    private void ShowBuffOverlay(bool show)
    {
        if (show)
        {
            if (_overlay is null)
            {
                _overlay = new OverlayWindow(App.State.Tracker);
                _overlay.BoundsChanged += () =>
                {
                    PersistBuffOverlayBounds();
                    App.State.SaveSettings();
                };
                _overlay.Closed += (_, _) =>
                {
                    PersistBuffOverlayBounds();
                    _overlay = null;
                    App.State.Settings.OverlayEnabled = false;
                    App.State.SaveSettings();
                    UpdateOverlayButton();
                };
            }

            var s = App.State.Settings;
            _overlay.ApplyBounds(s.OverlayLeft, s.OverlayTop, s.OverlayWidth, s.OverlayHeight);
            _overlay.Show();
        }
        else if (_overlay is not null)
        {
            PersistBuffOverlayBounds();
            _overlay.Close();
            _overlay = null;
        }
    }

    private void PersistBuffOverlayBounds()
    {
        if (_overlay is null) return;
        App.State.Settings.OverlayLeft = _overlay.Left;
        App.State.Settings.OverlayTop = _overlay.Top;
        App.State.Settings.OverlayWidth = _overlay.Width;
        App.State.Settings.OverlayHeight = _overlay.Height;
    }

    private void TimerOverlay_Click(object sender, RoutedEventArgs e)
    {
        var enable = !App.State.Settings.TimerOverlayEnabled;
        ShowTimerOverlay(enable);
        App.State.Settings.TimerOverlayEnabled = enable;
        App.State.SaveSettings();
        UpdateTimerOverlayButton();
    }

    private void ShowTimerOverlay(bool show)
    {
        if (show)
        {
            if (_timerOverlay is null)
            {
                _timerOverlay = new TimerOverlayWindow(App.State.UserTimers);
                _timerOverlay.BoundsChanged += () =>
                {
                    PersistTimerOverlayBounds();
                    App.State.SaveSettings();
                };
                _timerOverlay.Closed += (_, _) =>
                {
                    PersistTimerOverlayBounds();
                    _timerOverlay = null;
                    App.State.Settings.TimerOverlayEnabled = false;
                    App.State.SaveSettings();
                    UpdateTimerOverlayButton();
                };
            }

            var s = App.State.Settings;
            _timerOverlay.ApplyBounds(s.TimerOverlayLeft, s.TimerOverlayTop, s.TimerOverlayWidth, s.TimerOverlayHeight);
            _timerOverlay.Show();
        }
        else if (_timerOverlay is not null)
        {
            PersistTimerOverlayBounds();
            _timerOverlay.Close();
            _timerOverlay = null;
        }
    }

    private void PersistTimerOverlayBounds()
    {
        if (_timerOverlay is null) return;
        App.State.Settings.TimerOverlayLeft = _timerOverlay.Left;
        App.State.Settings.TimerOverlayTop = _timerOverlay.Top;
        App.State.Settings.TimerOverlayWidth = _timerOverlay.Width;
        App.State.Settings.TimerOverlayHeight = _timerOverlay.Height;
    }

    private void Options_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OptionsWindow { Owner = this };
        if (dlg.ShowDialog() == true)
            ApplyMouseHighlight();
        RefreshBuffList();
        RefreshDamageRegionList();
        TxtStatus.Text = App.State.Monitor.Status;
        RefreshStatusPills();
    }

    private void AddBuff_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.MigrateAndNormalize(App.State.Settings);
        var regionId = App.State.Settings.DefaultBuffRegionId() ?? string.Empty;
        var def = new BuffDefinition
        {
            Name = "New Buff",
            DurationSeconds = 60 * 10,
            WarnSeconds = 60,
            CastRegionId = regionId,
            LandRegionId = regionId,
            FadeRegionId = regionId,
            WarnRegionId = regionId
        };
        var dlg = new BuffEditWindow(def) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            App.State.Settings.Buffs.Add(dlg.Result);
            App.State.SaveSettings();
            RefreshBuffList();
        }
    }

    private void EditBuff_Click(object sender, RoutedEventArgs e)
    {
        if (ListBuffs.SelectedItem is not BuffDefinition selected) return;
        var dlg = new BuffEditWindow(selected.Clone()) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            var idx = App.State.Settings.Buffs.FindIndex(b => b.Id == selected.Id);
            if (idx >= 0)
            {
                var updated = dlg.Result;
                updated.Id = selected.Id;
                App.State.Settings.Buffs[idx] = updated;
                App.State.SaveSettings();
                RefreshBuffList();
            }
        }
    }

    private void DeleteBuff_Click(object sender, RoutedEventArgs e)
    {
        if (ListBuffs.SelectedItem is not BuffDefinition selected) return;
        if (MessageBox.Show(this, $"Delete buff \"{selected.Name}\"?", "MandM",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        App.State.Settings.Buffs.RemoveAll(b => b.Id == selected.Id);
        App.State.Tracker.ClearBuff(selected.Id);
        App.State.SaveSettings();
        RefreshBuffList();
    }

    private void AddTimer_Click(object sender, RoutedEventArgs e)
    {
        var def = new UserTimerDefinition { Name = "New Timer", DurationSeconds = 5 * 60 };
        var dlg = new TimerEditWindow(def) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        App.State.UserTimers.AddTimer(dlg.Result);
        App.State.SaveSettings();
    }

    private void EditTimer_Click(object sender, RoutedEventArgs e)
    {
        if (ListTimers.SelectedItem is not ActiveUserTimer selected) return;
        var dlg = new TimerEditWindow(selected.Definition.Clone()) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        dlg.Result.Id = selected.Definition.Id;
        var list = App.State.Settings.UserTimers;
        var idx = list.FindIndex(t => t.Id == selected.Definition.Id);
        if (idx >= 0)
            list[idx] = dlg.Result;
        App.State.UserTimers.UpdateSettings(App.State.Settings);
        selected.Reset();
        App.State.SaveSettings();
    }

    private void DeleteTimer_Click(object sender, RoutedEventArgs e)
    {
        if (ListTimers.SelectedItem is not ActiveUserTimer selected) return;
        if (MessageBox.Show(this, $"Delete timer \"{selected.Name}\"?", "MandM",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        App.State.UserTimers.RemoveTimer(selected.Definition.Id);
        App.State.SaveSettings();
    }

    private static ActiveUserTimer? TimerFromSender(object sender) =>
        (sender as FrameworkElement)?.Tag as ActiveUserTimer
        ?? ((sender as FrameworkElement)?.DataContext as ActiveUserTimer);

    private void TimerStart_Click(object sender, RoutedEventArgs e)
    {
        TimerFromSender(sender)?.Start();
        // Force list refresh of bound status
        ListTimers.Items.Refresh();
    }

    private void TimerStop_Click(object sender, RoutedEventArgs e)
    {
        TimerFromSender(sender)?.Stop();
        ListTimers.Items.Refresh();
    }

    private void TimerReset_Click(object sender, RoutedEventArgs e)
    {
        TimerFromSender(sender)?.Reset();
        ListTimers.Items.Refresh();
    }

    private void RefreshDamageRegionList()
    {
        if (CmbDamageRegion is null) return;
        _damageRegionSync = true;
        try
        {
            var regions = App.State.Settings.CaptureRegions.ToList();
            CmbDamageRegion.ItemsSource = regions;
            var id = App.State.Settings.DamageRegionId ?? string.Empty;
            CmbDamageRegion.SelectedItem = regions.FirstOrDefault(r =>
                string.Equals(r.Id, id, StringComparison.Ordinal));
        }
        finally
        {
            _damageRegionSync = false;
        }

        UpdateDamageHint();
    }

    private void DamageRegion_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_damageRegionSync || CmbDamageRegion is null) return;
        var selected = CmbDamageRegion.SelectedItem as NamedCaptureRegion;
        var id = selected?.Id ?? string.Empty;
        if (string.Equals(App.State.Settings.DamageRegionId ?? string.Empty, id, StringComparison.Ordinal))
            return;

        App.State.Settings.DamageRegionId = id;
        App.State.SaveSettings();
        UpdateDamageHint();
    }

    private void UpdateDamageHint()
    {
        if (TxtDamageHint is null) return;
        var selected = CmbDamageRegion.SelectedItem as NamedCaptureRegion;
        if (selected is null)
        {
            TxtDamageHint.Text =
                "Pick the named capture region for your combat window. Lines already on screen are ignored. A slain line ends the fight.";
            return;
        }

        TxtDamageHint.Text = selected.IsValid
            ? $"Reading “{selected.Name}”. New hits start a fight. Any slain line, including a party member’s kill, ends it until the next hit."
            : $"“{selected.Name}” has no rectangle yet. Set it in Options, then choose it here.";
    }

    private void RefreshDamageTotals()
    {
        if (TxtDamageDealt is null) return;
        var damage = App.State.Damage;
        TxtFightDuration.Text = damage.DurationDisplay;
        TxtDamageDealt.Text = damage.DamageDealtDisplay;
        TxtFightDps.Text = damage.DpsDisplay;
        TxtFightStatus.Text = damage.StatusDisplay;
        TxtNoDamage.Visibility = damage.Abilities.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (damage.Abilities.Count == 0 && damage.HasFight)
            TxtNoDamage.Text = "Fight started. Ability totals appear as you land hits.";
        else if (damage.Abilities.Count == 0)
            TxtNoDamage.Text = "No fight yet. The next new hit starts one. Lines already on screen are ignored.";
    }

    private void ResetDamage_Click(object sender, RoutedEventArgs e) =>
        App.State.Damage.ClearFight();

    private async void Updates_Click(object sender, RoutedEventArgs e)
    {
        TxtStatus.Text = "Checking for updates…";
        var result = await App.State.Updates.CheckAsync(App.State.Settings.UpdateRepo);
        TxtStatus.Text = result.Message;
        if (result.UpdateAvailable && !string.IsNullOrWhiteSpace(result.ReleaseUrl))
        {
            var open = MessageBox.Show(this,
                $"{result.Message}\n\nOpen release page?",
                "MandM Updates",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (open == MessageBoxResult.Yes)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = result.ReleaseUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "MandM", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        else
        {
            MessageBox.Show(this, result.Message, "MandM Updates", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
