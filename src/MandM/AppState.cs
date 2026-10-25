using MandM.Models;
using MandM.Services;

namespace MandM;

public sealed class AppState : IDisposable
{
    public SettingsService SettingsService { get; } = new();
    public AlarmService Alarms { get; } = new();
    public OcrService Ocr { get; } = new();
    public UpdateCheckService Updates { get; } = new();

    public AppSettings Settings { get; private set; }
    public BuffTrackerService Tracker { get; }
    public CaptureMonitorService Monitor { get; }

    public AppState()
    {
        Settings = SettingsService.Load();
        Tracker = new BuffTrackerService(Settings, Alarms);
        Monitor = new CaptureMonitorService(Settings, Ocr, Tracker);
    }

    public void SaveSettings()
    {
        SettingsService.Save(Settings);
        Tracker.UpdateSettings(Settings);
        Monitor.UpdateSettings(Settings);
    }

    public void ReloadSettings()
    {
        Settings = SettingsService.Load();
        Tracker.UpdateSettings(Settings);
        Monitor.UpdateSettings(Settings);
    }

    public void Dispose()
    {
        Monitor.Dispose();
        Tracker.Dispose();
    }
}
