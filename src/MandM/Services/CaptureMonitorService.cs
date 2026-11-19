using System.Collections.ObjectModel;
using System.Drawing;
using System.Windows;
using MandM.Models;

namespace MandM.Services;

/// <summary>
/// Periodically OCR's valid capture regions, skipping unchanged frames and reusing buffers.
/// </summary>
public sealed class CaptureMonitorService : IDisposable
{
    private readonly ScreenCaptureService _capture = new();
    private readonly OcrService _ocr;
    private readonly BuffTrackerService _tracker;
    private readonly DamageTrackerService _damage;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private AppSettings _settings;
    private readonly object _baselineGate = new();
    private bool _needsBaseline = true;
    private readonly Dictionary<string, RegionSlot> _slots = new(StringComparer.Ordinal);
    private string _lastStatus = string.Empty;
    private int _idleStreak;
    private long _totalCaptures;
    private long _totalOcr;
    private long _totalSkipped;

    public string Status { get; private set; } = "Idle";
    public ObservableCollection<RegionOcrSnapshot> RegionSnapshots { get; } = [];
    public event Action<string>? StatusChanged;
    public event Action<string>? CombatPreviewChanged;
    public event Action<string>? AbilitiesPreviewChanged;

    public CaptureMonitorService(
        AppSettings settings,
        OcrService ocr,
        BuffTrackerService tracker,
        DamageTrackerService damage)
    {
        _settings = settings;
        _ocr = ocr;
        _tracker = tracker;
        _damage = damage;
        SyncRegionSnapshots();
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        SyncRegionSnapshots();
        RequestBaseline();
    }

    public void Start()
    {
        Stop();
        SyncRegionSnapshots();
        RequestBaseline();
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        _cts?.Dispose();
        _cts = null;
        _loop = null;
        DisposeSlots();
        SetStatus("Stopped");
    }

    private void RequestBaseline()
    {
        lock (_baselineGate)
            _needsBaseline = true;
        foreach (var slot in _slots.Values)
            slot.ResetHash();
        _idleStreak = 0;
        _tracker.InvalidateMatchBaseline();
        _damage.InvalidateMatchBaseline();
    }

    private bool ConsumeBaselineRequest()
    {
        lock (_baselineGate)
        {
            if (!_needsBaseline)
                return false;
            _needsBaseline = false;
            return true;
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        if (!_ocr.IsAvailable)
        {
            SetStatus(_ocr.UnavailableReason ?? "OCR unavailable");
            return;
        }

        SetStatus("Monitoring…");
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var settings = _settings;
                var interval = Math.Clamp(settings.OcrIntervalMs, 400, 5000);
                var live = GetLiveRegions(settings);

                if (live.Count == 0)
                {
                    // No valid/referenced regions — don't wake capture or OCR.
                    SetStatus("Set capture regions in Options");
                    await Task.Delay(Math.Max(interval, 1500), ct);
                    continue;
                }

                if (ConsumeBaselineRequest())
                {
                    await CaptureBaselineAsync(settings, live, ct);
                    SetStatus($"Baseline captured — monitoring {live.Count} live region(s)");
                    await Task.Delay(interval, ct);
                    continue;
                }

                var ocrThisPass = 0;
                var skippedThisPass = 0;
                var capturedThisPass = 0;

                foreach (var named in live)
                {
                    var slot = GetOrCreateSlot(named.Id);
                    var bmp = _capture.CaptureInto(named.Region, slot.Bitmap);
                    if (bmp is null)
                        continue;

                    slot.Bitmap = bmp;
                    capturedThisPass++;
                    _totalCaptures++;

                    var hash = ScreenCaptureService.ComputeContentHash(bmp);
                    if (slot.HasHash && slot.LastHash == hash)
                    {
                        skippedThisPass++;
                        _totalSkipped++;
                        continue;
                    }

                    slot.LastHash = hash;
                    slot.HasHash = true;

                    var lines = await _ocr.RecognizeLinesAsync(bmp, ct);
                    ocrThisPass++;
                    _totalOcr++;
                    _tracker.ProcessRegionLines(named.Id, lines);
                    _damage.ProcessRegionLines(named.Id, lines);
                    PublishPreview(named, settings, lines, slot);
                }

                if (ocrThisPass == 0 && skippedThisPass > 0)
                    _idleStreak++;
                else
                    _idleStreak = 0;

                // Mild backoff while every live region is unchanged (chat idle).
                var delay = _idleStreak >= 3
                    ? Math.Min(interval * 2, Math.Max(interval, 1200))
                    : interval;

                SetStatus("Live");

                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                SetStatus($"OCR error: {ex.Message}");
                try { await Task.Delay(1500, ct); } catch { break; }
            }
        }
    }

    private async Task CaptureBaselineAsync(
        AppSettings settings,
        List<NamedCaptureRegion> regions,
        CancellationToken ct)
    {
        SetStatus("Capturing OCR baseline (ignoring on-screen text)…");
        _tracker.InvalidateMatchBaseline();
        _damage.InvalidateMatchBaseline();

        foreach (var named in regions)
        {
            var slot = GetOrCreateSlot(named.Id);
            var bmp = _capture.CaptureInto(named.Region, slot.Bitmap);
            if (bmp is null)
                continue;

            slot.Bitmap = bmp;
            slot.LastHash = ScreenCaptureService.ComputeContentHash(bmp);
            slot.HasHash = true;

            var lines = await _ocr.RecognizeLinesAsync(bmp, ct);
            _tracker.PrimeRegionBaseline(named.Id, lines);
            _damage.PrimeRegionBaseline(named.Id, lines);
            PublishPreview(named, settings, lines, slot);
        }

        bool anotherPending;
        lock (_baselineGate)
            anotherPending = _needsBaseline;
        if (!anotherPending)
        {
            _tracker.MarkBaselineReady();
            _damage.MarkBaselineReady();
        }
    }

    /// <summary>All named regions with a valid rectangle — feeds buffs and the OCR tab.</summary>
    private static List<NamedCaptureRegion> GetLiveRegions(AppSettings settings) =>
        settings.CaptureRegions?.Where(r => r.IsValid).ToList() ?? [];

    public void SyncRegionSnapshots()
    {
        void Apply()
        {
            var regions = _settings.CaptureRegions ?? [];
            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (var region in regions)
            {
                keep.Add(region.Id);
                var snap = RegionSnapshots.FirstOrDefault(s => s.Id == region.Id);
                if (snap is null)
                {
                    snap = new RegionOcrSnapshot { Id = region.Id };
                    RegionSnapshots.Add(snap);
                }

                snap.Name = region.Name;
                snap.BoundsDisplay = region.BoundsDisplay;
                snap.IsValid = region.IsValid;
                if (!region.IsValid)
                    snap.Text = string.Empty;
            }

            for (var i = RegionSnapshots.Count - 1; i >= 0; i--)
            {
                if (!keep.Contains(RegionSnapshots[i].Id))
                    RegionSnapshots.RemoveAt(i);
            }
        }

        if (Application.Current?.Dispatcher.CheckAccess() == true)
            Apply();
        else
            Application.Current?.Dispatcher.Invoke(Apply);
    }

    private RegionSlot GetOrCreateSlot(string id)
    {
        if (_slots.TryGetValue(id, out var slot))
            return slot;
        slot = new RegionSlot();
        _slots[id] = slot;
        return slot;
    }

    private void PublishPreview(
        NamedCaptureRegion named,
        AppSettings settings,
        IReadOnlyList<string> lines,
        RegionSlot slot)
    {
        string text;
        if (IsAbilitiesRegion(named))
        {
            var cleaned = new List<string>(lines.Count);
            foreach (var line in lines)
            {
                var c = CleanAbilityLine(line);
                if (!string.IsNullOrWhiteSpace(c))
                    cleaned.Add(c);
            }

            text = string.Join(Environment.NewLine, cleaned);
            if (!string.Equals(text, slot.LastPreview, StringComparison.Ordinal))
            {
                slot.LastPreview = text;
                AbilitiesPreviewChanged?.Invoke(text);
            }

            _tracker.SetLoadedAbilities(cleaned);
        }
        else
        {
            text = string.Join(Environment.NewLine, lines);
            if (!string.Equals(text, slot.LastPreview, StringComparison.Ordinal))
            {
                slot.LastPreview = text;
                if (IsCombatPreviewRegion(named, settings))
                    CombatPreviewChanged?.Invoke(text);
            }
        }

        UpdateSnapshotText(named.Id, text);
    }

    private void UpdateSnapshotText(string regionId, string text)
    {
        void Apply()
        {
            var snap = RegionSnapshots.FirstOrDefault(s => s.Id == regionId);
            if (snap is null) return;
            snap.Text = text;
        }

        if (Application.Current?.Dispatcher.CheckAccess() == true)
            Apply();
        else
            Application.Current?.Dispatcher.Invoke(Apply);
    }

    private static bool IsAbilitiesRegion(NamedCaptureRegion named) =>
        string.Equals(named.Name, "Abilities", StringComparison.OrdinalIgnoreCase);

    private static bool IsCombatPreviewRegion(NamedCaptureRegion named, AppSettings settings) =>
        string.Equals(named.Name, "Combat Text", StringComparison.OrdinalIgnoreCase);

    private static string? CleanAbilityLine(string line)
    {
        var t = line.Trim();
        if (t.Length <= 2 && t.All(char.IsDigit)) return null;
        if (t.Length > 2 && char.IsDigit(t[0]) && (t[1] == ' ' || t[1] == '.' || t[1] == ')'))
            t = t[2..].Trim();
        return t;
    }

    private void SetStatus(string status)
    {
        if (string.Equals(status, _lastStatus, StringComparison.Ordinal))
            return;
        _lastStatus = status;
        Status = status;
        StatusChanged?.Invoke(status);
    }

    private void DisposeSlots()
    {
        foreach (var slot in _slots.Values)
            slot.Dispose();
        _slots.Clear();
    }

    public void Dispose() => Stop();

    private sealed class RegionSlot : IDisposable
    {
        public Bitmap? Bitmap;
        public ulong LastHash;
        public bool HasHash;
        public string LastPreview = string.Empty;

        public void ResetHash()
        {
            HasHash = false;
            LastHash = 0;
            LastPreview = string.Empty;
        }

        public void Dispose()
        {
            Bitmap?.Dispose();
            Bitmap = null;
        }
    }
}
