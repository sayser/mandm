using System.Windows;
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
    private MouseHighlightOverlayWindow? _mouseHighlight;

    public MainWindow()
    {
        InitializeComponent();
        TxtVersion.Text = $"v{UpdateCheckService.GetLocalVersion()}";
        ListActive.ItemsSource = App.State.Tracker.ActiveBuffs;
        ListAbilities.ItemsSource = App.State.Tracker.LoadedAbilities;
        RefreshBuffList();
        UpdateOverlayButton();
        UpdateActiveEmptyState();
        ApplyMouseHighlight();
        RefreshStatusPills();

        App.State.Monitor.StatusChanged += s => Dispatcher.Invoke(() =>
        {
            TxtStatus.Text = s;
            RefreshStatusPills();
        });
        App.State.Monitor.CombatPreviewChanged += s => Dispatcher.Invoke(() => TxtCombatPreview.Text = s);
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

        TxtStatus.Text = App.State.Ocr.IsAvailable
            ? App.State.Monitor.Status
            : App.State.Ocr.UnavailableReason ?? "OCR unavailable";
        RefreshStatusPills();

        if (App.State.Settings.OverlayEnabled)
            ShowOverlay(true);

        Closing += (_, _) =>
        {
            if (_overlay is not null)
            {
                PersistOverlayBounds();
                _overlay.Close();
                _overlay = null;
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
        {
            ListBuffs.SelectedItem = App.State.Settings.Buffs.FirstOrDefault(b => b.Id == selectedId);
        }
    }

    private void UpdateOverlayButton()
    {
        var on = App.State.Settings.OverlayEnabled;
        BtnOverlay.Content = on ? "Overlay: On" : "Overlay: Off";
        RefreshStatusPills();
    }

    private void UpdateActiveEmptyState() =>
        TxtNoActive.Visibility = App.State.Tracker.ActiveBuffs.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void RefreshStatusPills()
    {
        var ocrOk = App.State.Ocr.IsAvailable;
        OcrDot.Fill = ocrOk ? OcrOnlineBrush : OcrOfflineBrush;
        TxtOcrPill.Text = ocrOk
            ? (string.IsNullOrWhiteSpace(App.State.Monitor.Status) ? "Live" : Truncate(App.State.Monitor.Status, 28))
            : "Unavailable";
        TxtOverlayPill.Text = App.State.Settings.OverlayEnabled ? "On" : "Off";
        TxtOverlayPill.Foreground = App.State.Settings.OverlayEnabled ? OcrOnlineBrush : MutedPillBrush;

        var mouseOn = App.State.Settings.MouseHighlight?.Enabled == true;
        TxtMousePill.Text = mouseOn ? "On" : "Off";
        TxtMousePill.Foreground = mouseOn ? OcrOnlineBrush : MutedPillBrush;

        TxtActivePill.Text = App.State.Tracker.ActiveBuffs.Count.ToString();
    }

    private void ApplyMouseHighlight()
    {
        var settings = App.State.Settings.MouseHighlight ?? new MouseHighlightSettings();
        if (settings.Enabled)
        {
            _mouseHighlight ??= new MouseHighlightOverlayWindow();
            _mouseHighlight.ApplyOptions(settings);
        }
        else
        {
            _mouseHighlight?.ApplyOptions(settings);
        }

        RefreshStatusPills();
    }

    private void ModuleTab_Checked(object sender, RoutedEventArgs e)
    {
        if (PanelBuffs is null || PanelDamage is null) return;
        var showBuffs = TabBuffs.IsChecked == true;
        PanelBuffs.Visibility = showBuffs ? Visibility.Visible : Visibility.Collapsed;
        PanelDamage.Visibility = showBuffs ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void Overlay_Click(object sender, RoutedEventArgs e)
    {
        var enable = !App.State.Settings.OverlayEnabled;
        ShowOverlay(enable);
        App.State.Settings.OverlayEnabled = enable;
        App.State.SaveSettings();
        UpdateOverlayButton();
    }

    private void ShowOverlay(bool show)
    {
        if (show)
        {
            if (_overlay is null)
            {
                _overlay = new OverlayWindow(App.State.Tracker);
                _overlay.Closed += (_, _) =>
                {
                    PersistOverlayBounds();
                    _overlay = null;
                    App.State.Settings.OverlayEnabled = false;
                    App.State.SaveSettings();
                    UpdateOverlayButton();
                };
            }

            var s = App.State.Settings;
            _overlay.Left = s.OverlayLeft;
            _overlay.Top = s.OverlayTop;
            _overlay.Width = s.OverlayWidth;
            _overlay.Height = s.OverlayHeight;
            _overlay.Show();
        }
        else if (_overlay is not null)
        {
            PersistOverlayBounds();
            _overlay.Close();
            _overlay = null;
        }
    }

    private void PersistOverlayBounds()
    {
        if (_overlay is null) return;
        App.State.Settings.OverlayLeft = _overlay.Left;
        App.State.Settings.OverlayTop = _overlay.Top;
        App.State.Settings.OverlayWidth = _overlay.Width;
        App.State.Settings.OverlayHeight = _overlay.Height;
    }

    private void Options_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OptionsWindow { Owner = this };
        if (dlg.ShowDialog() == true)
            ApplyMouseHighlight();
        RefreshBuffList();
        TxtStatus.Text = App.State.Monitor.Status;
        RefreshStatusPills();
    }

    private void AddBuff_Click(object sender, RoutedEventArgs e)
    {
        var def = new BuffDefinition
        {
            Name = "New Buff",
            DurationSeconds = 60 * 10,
            WarnSeconds = 60
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

    private void TestLand_Click(object sender, RoutedEventArgs e)
    {
        if (ListBuffs.SelectedItem is not BuffDefinition selected) return;
        App.State.Tracker.TestLand(selected);
    }

    private async void Updates_Click(object sender, RoutedEventArgs e)
    {
        BtnOverlay.IsEnabled = true;
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
