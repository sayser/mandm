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
    public UserTimerService UserTimers { get; }
    public DamageTrackerService Damage { get; }
    public CaptureMonitorService Monitor { get; }

    public AppState()
    {
        Settings = SettingsService.Load();
        Alarms.UpdateVoiceSettings(Settings.Voice ?? new VoiceSettings());
        Tracker = new BuffTrackerService(Settings, Alarms);
        UserTimers = new UserTimerService(Settings, Alarms);
        Damage = new DamageTrackerService(Settings);
        Monitor = new CaptureMonitorService(Settings, Ocr, Tracker, Damage);
    }

    public void SaveSettings()
    {
        SettingsService.Save(Settings);
        Alarms.UpdateVoiceSettings(Settings.Voice ?? new VoiceSettings());
        Tracker.UpdateSettings(Settings);
        UserTimers.UpdateSettings(Settings);
        Damage.UpdateSettings(Settings);
        Monitor.UpdateSettings(Settings);
    }

    public void ReloadSettings()
    {
        Settings = SettingsService.Load();
        Alarms.UpdateVoiceSettings(Settings.Voice ?? new VoiceSettings());
        Tracker.UpdateSettings(Settings);
        UserTimers.UpdateSettings(Settings);
        Damage.UpdateSettings(Settings);
        Monitor.UpdateSettings(Settings);
    }

    public void Dispose()
    {
        Monitor.Dispose();
        Tracker.Dispose();
        UserTimers.Dispose();
    }
}
