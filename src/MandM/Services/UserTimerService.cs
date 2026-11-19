using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using MandM.Models;

namespace MandM.Services;

public sealed class UserTimerService : IDisposable
{
    private readonly DispatcherTimer _tick;
    private AppSettings _settings;

    public ObservableCollection<ActiveUserTimer> Timers { get; } = [];
    public event Action? Changed;

    public UserTimerService(AppSettings settings)
    {
        _settings = settings;
        RebuildFromSettings();
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _tick.Tick += (_, _) => Tick();
        _tick.Start();
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        var runningIds = Timers.Where(t => t.IsRunning).Select(t => t.Definition.Id).ToHashSet(StringComparer.Ordinal);
        var remainingById = Timers.ToDictionary(t => t.Definition.Id, t => t.Remaining);

        Timers.Clear();
        foreach (var def in _settings.UserTimers ?? [])
        {
            var timer = new ActiveUserTimer { Definition = def };
            timer.Reset();
            if (runningIds.Contains(def.Id) && remainingById.TryGetValue(def.Id, out var left) && left > TimeSpan.Zero)
            {
                // Resume with previous remaining after definition refresh.
                // ActiveUserTimer doesn't expose set-remaining-while-running easily; reset then start with snap:
                // Simplest UX after edit: leave stopped at full duration (caller may Reset).
                timer.Reset();
            }

            Timers.Add(timer);
        }

        Changed?.Invoke();
    }

    public void RebuildFromSettings()
    {
        Timers.Clear();
        foreach (var def in _settings.UserTimers ?? [])
        {
            var timer = new ActiveUserTimer { Definition = def };
            timer.Reset();
            Timers.Add(timer);
        }

        Changed?.Invoke();
    }

    public ActiveUserTimer AddTimer(UserTimerDefinition def)
    {
        _settings.UserTimers ??= [];
        _settings.UserTimers.Add(def);
        var timer = new ActiveUserTimer { Definition = def };
        timer.Reset();

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
                Timers.Remove(existing);
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
