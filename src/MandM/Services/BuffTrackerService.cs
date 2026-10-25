using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using MandM.Models;

namespace MandM.Services;

public sealed class BuffTrackerService : IDisposable
{
    private readonly AlarmService _alarms;
    private readonly DispatcherTimer _tick;
    private readonly object _gate = new();
    private readonly HashSet<string> _recentLines = new(StringComparer.OrdinalIgnoreCase);
    private AppSettings _settings;

    public ObservableCollection<ActiveBuff> ActiveBuffs { get; } = [];
    public ObservableCollection<string> LoadedAbilities { get; } = [];
    public event Action? Changed;

    public BuffTrackerService(AppSettings settings, AlarmService alarms)
    {
        _settings = settings;
        _alarms = alarms;
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _tick.Tick += (_, _) => Tick();
        _tick.Start();
    }

    public void UpdateSettings(AppSettings settings) => _settings = settings;

    public void ProcessCombatLines(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Deduplicate identical OCR lines briefly so we don't re-land every poll.
            var key = TextMatch.Normalize(line);
            lock (_gate)
            {
                if (!_recentLines.Add(key))
                    continue;
            }

            foreach (var def in _settings.Buffs.Where(b => b.Enabled))
            {
                if (!string.IsNullOrWhiteSpace(def.FadeText) && TextMatch.ContainsLoose(line, def.FadeText))
                {
                    ClearBuff(def.Id);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(def.LandText) && TextMatch.ContainsLoose(line, def.LandText))
                {
                    LandBuff(def);
                }
            }
        }

        // Cap recent-line memory
        lock (_gate)
        {
            if (_recentLines.Count > 200)
                _recentLines.Clear();
        }
    }

    public void SetLoadedAbilities(IEnumerable<string> names)
    {
        var list = names
            .Select(n => n.Trim())
            .Where(n => n.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        void Apply()
        {
            LoadedAbilities.Clear();
            foreach (var n in list)
                LoadedAbilities.Add(n);
            Changed?.Invoke();
        }

        if (Application.Current?.Dispatcher.CheckAccess() == true)
            Apply();
        else
            Application.Current?.Dispatcher.Invoke(Apply);
    }

    public void LandBuff(BuffDefinition def)
    {
        void Apply()
        {
            var existing = ActiveBuffs.FirstOrDefault(b => b.Definition.Id == def.Id);
            if (existing is not null)
                ActiveBuffs.Remove(existing);

            ActiveBuffs.Add(new ActiveBuff
            {
                Definition = def,
                LandedAtUtc = DateTime.UtcNow,
                Remaining = TimeSpan.FromSeconds(def.DurationSeconds)
            });
            Changed?.Invoke();
        }

        if (Application.Current?.Dispatcher.CheckAccess() == true)
            Apply();
        else
            Application.Current?.Dispatcher.Invoke(Apply);
    }

    public void ClearBuff(string buffId)
    {
        void Apply()
        {
            var existing = ActiveBuffs.FirstOrDefault(b => b.Definition.Id == buffId);
            if (existing is null) return;
            ActiveBuffs.Remove(existing);
            Changed?.Invoke();
        }

        if (Application.Current?.Dispatcher.CheckAccess() == true)
            Apply();
        else
            Application.Current?.Dispatcher.Invoke(Apply);
    }

    /// <summary>Manual test helper — lands a buff without OCR.</summary>
    public void TestLand(BuffDefinition def) => LandBuff(def);

    private void Tick()
    {
        var now = DateTime.UtcNow;
        ActiveBuff expired = null!;
        foreach (var buff in ActiveBuffs.ToList())
        {
            var elapsed = now - buff.LandedAtUtc;
            var remaining = TimeSpan.FromSeconds(buff.Definition.DurationSeconds) - elapsed;
            buff.Remaining = remaining;

            if (remaining <= TimeSpan.Zero)
            {
                expired = buff;
                continue;
            }

            if (!buff.Warned && remaining.TotalSeconds <= buff.Definition.WarnSeconds)
            {
                buff.Warned = true;
                _alarms.Play(buff.Definition.AlarmSound);
            }
        }

        if (expired is not null)
        {
            ActiveBuffs.Remove(expired);
            Changed?.Invoke();
        }

        // Remove any others that expired this tick
        foreach (var buff in ActiveBuffs.Where(b => b.Remaining <= TimeSpan.Zero).ToList())
            ActiveBuffs.Remove(buff);
    }

    public void Dispose() => _tick.Stop();
}
