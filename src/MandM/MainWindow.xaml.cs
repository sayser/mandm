using System.Windows;
using MandM.Models;
using MandM.Services;

namespace MandM;

public partial class MainWindow : Window
{
    private OverlayWindow? _overlay;

    public MainWindow()
    {
        InitializeComponent();
        TxtVersion.Text = $"v{UpdateCheckService.GetLocalVersion()}";
        ListActive.ItemsSource = App.State.Tracker.ActiveBuffs;
        ListAbilities.ItemsSource = App.State.Tracker.LoadedAbilities;
        RefreshBuffList();
        UpdateOverlayButton();
        UpdateActiveEmptyState();

        App.State.Monitor.StatusChanged += s => Dispatcher.Invoke(() => TxtStatus.Text = s);
        App.State.Monitor.CombatPreviewChanged += s => Dispatcher.Invoke(() => TxtCombatPreview.Text = s);
        App.State.Tracker.Changed += () => Dispatcher.Invoke(() =>
        {
            RefreshBuffList();
            UpdateActiveEmptyState();
        });
        App.State.Tracker.ActiveBuffs.CollectionChanged += (_, _) =>
            Dispatcher.Invoke(UpdateActiveEmptyState);

        TxtStatus.Text = App.State.Ocr.IsAvailable
            ? App.State.Monitor.Status
            : App.State.Ocr.UnavailableReason ?? "OCR unavailable";

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
    }

    private void UpdateActiveEmptyState() =>
        TxtNoActive.Visibility = App.State.Tracker.ActiveBuffs.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

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
        dlg.ShowDialog();
        RefreshBuffList();
        TxtStatus.Text = App.State.Monitor.Status;
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
