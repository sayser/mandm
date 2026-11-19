using MandM.Models;

namespace MandM.Services;

/// <summary>
/// Periodically OCR's every named capture region, skipping unchanged frames.
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
    private readonly Dictionary<string, ulong> _frameHashes = new(StringComparer.Ordinal);
    private int _skippedFrames;
    private int _ocrFrames;

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
        SetStatus("Stopped");
    }

    private void RequestBaseline()
    {
        lock (_baselineGate)
            _needsBaseline = true;
        _frameHashes.Clear();
        _skippedFrames = 0;
        _ocrFrames = 0;
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
                // Reasonable poll: clamp a bit higher floor so we don't thrash GPU/CPU.
                var interval = Math.Clamp(settings.OcrIntervalMs, 400, 5000);
                var regions = settings.CaptureRegions?.Where(r => r.IsValid).ToList() ?? [];

                if (ConsumeBaselineRequest())
                {
                    await CaptureBaselineAsync(settings, regions, ct);
                    SetStatus(regions.Count > 0
                        ? $"Baseline captured — monitoring {regions.Count} region(s)"
                        : "Set capture regions in Options");
                    await Task.Delay(interval, ct);
                    continue;
                }

                var ocrThisPass = 0;
                var skippedThisPass = 0;
                foreach (var named in regions)
                {
                    using var bmp = _capture.Capture(named.Region);
                    if (bmp is null)
                        continue;

                    var hash = ScreenCaptureService.ComputeContentHash(bmp);
                    if (_frameHashes.TryGetValue(named.Id, out var prior) && prior == hash)
                    {
                        skippedThisPass++;
                        _skippedFrames++;
                        continue;
                    }

                    _frameHashes[named.Id] = hash;
                    var lines = await _ocr.RecognizeLinesAsync(bmp, ct);
                    ocrThisPass++;
                    _ocrFrames++;
                    _tracker.ProcessRegionLines(named.Id, lines);
                    PublishPreview(named, settings, lines);
                }

                SetStatus(regions.Count > 0
                    ? $"Monitoring {regions.Count} region(s) · OCR {ocrThisPass}/{regions.Count} this tick (skip unchanged) · every {interval} ms"
                    : "Set capture regions in Options");

                await Task.Delay(interval, ct);
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

    /// <summary>
    /// First OCR pass after start/settings change: seed match counters only.
    /// On-screen land/warn/fade/cast lines must not trigger buff actions.
    /// </summary>
    private async Task CaptureBaselineAsync(
        AppSettings settings,
        List<NamedCaptureRegion> regions,
        CancellationToken ct)
    {
        SetStatus("Capturing OCR baseline (ignoring on-screen text)…");
        _tracker.InvalidateMatchBaseline();
        _frameHashes.Clear();

        foreach (var named in regions)
        {
            using var bmp = _capture.Capture(named.Region);
            if (bmp is null)
                continue;

            _frameHashes[named.Id] = ScreenCaptureService.ComputeContentHash(bmp);
            var lines = await _ocr.RecognizeLinesAsync(bmp, ct);
            _tracker.PrimeRegionBaseline(named.Id, lines);
            PublishPreview(named, settings, lines);
        }

        bool anotherPending;
        lock (_baselineGate)
            anotherPending = _needsBaseline;
        if (!anotherPending)
            _tracker.MarkBaselineReady();
    }

    private void PublishPreview(NamedCaptureRegion named, AppSettings settings, IReadOnlyList<string> lines)
    {
        if (IsAbilitiesRegion(named))
        {
            var cleaned = lines
                .Select(CleanAbilityLine)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Cast<string>()
                .ToList();
            AbilitiesPreviewChanged?.Invoke(string.Join(Environment.NewLine, cleaned));
            _tracker.SetLoadedAbilities(cleaned);
        }
        else if (IsCombatPreviewRegion(named, settings))
        {
            CombatPreviewChanged?.Invoke(string.Join(Environment.NewLine, lines));
        }
    }

    private static bool IsAbilitiesRegion(NamedCaptureRegion named) =>
        string.Equals(named.Name, "Abilities", StringComparison.OrdinalIgnoreCase);

    private static bool IsCombatPreviewRegion(NamedCaptureRegion named, AppSettings settings)
    {
        if (string.Equals(named.Name, "Combat Text", StringComparison.OrdinalIgnoreCase))
            return true;

        var first = settings.CaptureRegions
            .FirstOrDefault(r => r.IsValid && !IsAbilitiesRegion(r));
        return first is not null && string.Equals(first.Id, named.Id, StringComparison.Ordinal);
    }

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
        Status = status;
        StatusChanged?.Invoke(status);
    }

    public void Dispose() => Stop();
}
