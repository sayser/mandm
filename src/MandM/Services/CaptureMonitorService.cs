using System.Drawing;
using MandM.Models;

namespace MandM.Services;

/// <summary>
/// Periodically OCR's live capture regions, skipping unchanged frames and reusing buffers.
/// </summary>
public sealed class CaptureMonitorService : IDisposable
{
    private readonly ScreenCaptureService _capture = new();
    private readonly OcrService _ocr;
    private readonly BuffTrackerService _tracker;
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
    public event Action<string>? StatusChanged;
    public event Action<string>? CombatPreviewChanged;
    public event Action<string>? AbilitiesPreviewChanged;

    public CaptureMonitorService(AppSettings settings, OcrService ocr, BuffTrackerService tracker)
    {
        _settings = settings;
        _ocr = ocr;
        _tracker = tracker;
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        RequestBaseline();
    }

    public void Start()
    {
        Stop();
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

                SetStatus(
                    $"Live {live.Count} · OCR {ocrThisPass}/{capturedThisPass} · skip {_totalSkipped} · every {delay} ms");

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
            PublishPreview(named, settings, lines, slot);
        }

        bool anotherPending;
        lock (_baselineGate)
            anotherPending = _needsBaseline;
        if (!anotherPending)
            _tracker.MarkBaselineReady();
    }

    /// <summary>
    /// Only regions that are valid and either Abilities or referenced by an enabled buff.
    /// Unset / unused rectangles never wake OCR.
    /// </summary>
    private static List<NamedCaptureRegion> GetLiveRegions(AppSettings settings)
    {
        var all = settings.CaptureRegions;
        if (all is null || all.Count == 0)
            return [];

        var needed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var buff in settings.Buffs.Where(b => b.Enabled))
        {
            AddId(needed, buff.CastRegionId);
            AddId(needed, buff.LandRegionId);
            AddId(needed, buff.FadeRegionId);
            AddId(needed, buff.WarnRegionId);
        }

        var live = new List<NamedCaptureRegion>(all.Count);
        foreach (var region in all)
        {
            if (!region.IsValid)
                continue;
            if (IsAbilitiesRegion(region) || needed.Contains(region.Id))
                live.Add(region);
        }

        return live;
    }

    private static void AddId(HashSet<string> set, string? id)
    {
        if (!string.IsNullOrWhiteSpace(id))
            set.Add(id);
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
        if (IsAbilitiesRegion(named))
        {
            var cleaned = new List<string>(lines.Count);
            foreach (var line in lines)
            {
                var c = CleanAbilityLine(line);
                if (!string.IsNullOrWhiteSpace(c))
                    cleaned.Add(c);
            }

            var text = string.Join(Environment.NewLine, cleaned);
            if (!string.Equals(text, slot.LastPreview, StringComparison.Ordinal))
            {
                slot.LastPreview = text;
                AbilitiesPreviewChanged?.Invoke(text);
            }

            _tracker.SetLoadedAbilities(cleaned);
        }
        else if (IsCombatPreviewRegion(named, settings))
        {
            var text = string.Join(Environment.NewLine, lines);
            if (!string.Equals(text, slot.LastPreview, StringComparison.Ordinal))
            {
                slot.LastPreview = text;
                CombatPreviewChanged?.Invoke(text);
            }
        }
    }

    private static bool IsAbilitiesRegion(NamedCaptureRegion named) =>
        string.Equals(named.Name, "Abilities", StringComparison.OrdinalIgnoreCase);

    private static bool IsCombatPreviewRegion(NamedCaptureRegion named, AppSettings settings)
    {
        if (string.Equals(named.Name, "Combat Text", StringComparison.OrdinalIgnoreCase))
            return true;

        var first = settings.CaptureRegions
            .FirstOrDefault(r => r.IsValid && !IsAbilitiesRegion(r) && IsLiveForPreview(r, settings));
        return first is not null && string.Equals(first.Id, named.Id, StringComparison.Ordinal);
    }

    private static bool IsLiveForPreview(NamedCaptureRegion region, AppSettings settings)
    {
        foreach (var buff in settings.Buffs.Where(b => b.Enabled))
        {
            if (IdEquals(buff.CastRegionId, region.Id)
                || IdEquals(buff.LandRegionId, region.Id)
                || IdEquals(buff.FadeRegionId, region.Id)
                || IdEquals(buff.WarnRegionId, region.Id))
                return true;
        }

        return false;
    }

    private static bool IdEquals(string? a, string b) =>
        !string.IsNullOrWhiteSpace(a) && string.Equals(a, b, StringComparison.Ordinal);

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
