using MandM.Models;

namespace MandM.Services;

/// <summary>
/// Periodically OCR's Combat Text and Abilities capture regions.
/// </summary>
public sealed class CaptureMonitorService : IDisposable
{
    private readonly ScreenCaptureService _capture = new();
    private readonly OcrService _ocr;
    private readonly BuffTrackerService _tracker;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private AppSettings _settings;

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

    public void UpdateSettings(AppSettings settings) => _settings = settings;

    public void Start()
    {
        Stop();
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
                var interval = Math.Clamp(settings.OcrIntervalMs, 250, 5000);

                if (settings.CombatTextRegion.IsValid)
                {
                    using var bmp = _capture.Capture(settings.CombatTextRegion);
                    if (bmp is not null)
                    {
                        var lines = await _ocr.RecognizeLinesAsync(bmp, ct);
                        CombatPreviewChanged?.Invoke(string.Join(Environment.NewLine, lines));
                        _tracker.ProcessCombatLines(lines);
                    }
                }

                if (settings.AbilitiesRegion.IsValid)
                {
                    using var bmp = _capture.Capture(settings.AbilitiesRegion);
                    if (bmp is not null)
                    {
                        var lines = await _ocr.RecognizeLinesAsync(bmp, ct);
                        // Ability bar: each slot tends to be one OCR line with the spell name.
                        var cleaned = lines
                            .Select(CleanAbilityLine)
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .Cast<string>()
                            .ToList();
                        AbilitiesPreviewChanged?.Invoke(string.Join(Environment.NewLine, cleaned));
                        _tracker.SetLoadedAbilities(cleaned);
                    }
                }

                var regionsOk = settings.CombatTextRegion.IsValid || settings.AbilitiesRegion.IsValid;
                SetStatus(regionsOk
                    ? $"Monitoring (every {interval} ms)"
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

    private static string? CleanAbilityLine(string line)
    {
        var t = line.Trim();
        // Drop solitary hotkey digits OCR sometimes picks up.
        if (t.Length <= 2 && t.All(char.IsDigit)) return null;
        // Strip leading "1 " / "7 " style prefixes if present.
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
