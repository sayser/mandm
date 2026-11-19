using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using MandM.Models;

namespace MandM.Services;

public sealed class UserTimerService : IDisposable
{
    private readonly DispatcherTimer _tick;
    private readonly AlarmService _alarms;
    private AppSettings _settings;

    public ObservableCollection<ActiveUserTimer> Timers { get; } = [];
    public event Action? Changed;

    public UserTimerService(AppSettings settings, AlarmService alarms)
    {
        _settings = settings;
        _alarms = alarms;
        RebuildFromSettings();
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _tick.Tick += (_, _) => Tick();
        _tick.Start();
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        var runningIds = Timers.Where(t => t.IsRunning).Select(t => t.Definition.Id).ToHashSet(StringComparer.Ordinal);

        Timers.Clear();
        foreach (var def in _settings.UserTimers ?? [])
        {
            var timer = CreateTimer(def);
            timer.Reset();
            if (runningIds.Contains(def.Id))
                timer.Reset();
            Timers.Add(timer);
        }

        Changed?.Invoke();
    }

    public void RebuildFromSettings()
    {
        Timers.Clear();
        foreach (var def in _settings.UserTimers ?? [])
            Timers.Add(CreateTimer(def));
        Changed?.Invoke();
    }

    private ActiveUserTimer CreateTimer(UserTimerDefinition def)
    {
        var timer = new ActiveUserTimer { Definition = def };
        timer.Reset();
        timer.Expired += OnTimerExpired;
        return timer;
    }

    private void OnTimerExpired(ActiveUserTimer timer)
    {
        var def = timer.Definition;
        _alarms.PlayAlert(
            def.AlertMode,
            def.AlarmSound,
            def.VoiceText,
            $"{def.Name} finished");
        Changed?.Invoke();
    }

    public ActiveUserTimer AddTimer(UserTimerDefinition def)
    {
        _settings.UserTimers ??= [];
        _settings.UserTimers.Add(def);
        var timer = CreateTimer(def);

        void Apply()
        {
            Timers.Add(timer);
            Changed?.Invoke();
        }

        if (Application.Current?.Dispatcher.CheckAccess() == true)
            Apply();
        else
            Application.Current?.Dispatcher.Invoke(Apply);

        return timer;
    }

    public void RemoveTimer(string id)
    {
        _settings.UserTimers?.RemoveAll(t => t.Id == id);

        void Apply()
        {
            var existing = Timers.FirstOrDefault(t => t.Definition.Id == id);
            if (existing is not null)
            {
                existing.Expired -= OnTimerExpired;
                Timers.Remove(existing);
            }

            Changed?.Invoke();
        }

        if (Application.Current?.Dispatcher.CheckAccess() == true)
            Apply();
        else
            Application.Current?.Dispatcher.Invoke(Apply);
    }

    private void Tick()
    {
        foreach (var timer in Timers.ToList())
            timer.Tick();
    }

    public void Dispose() => _tick.Stop();
}
