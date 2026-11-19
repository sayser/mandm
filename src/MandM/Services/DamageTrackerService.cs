using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using MandM.Models;

namespace MandM.Services;

/// <summary>
/// Current fight for the selected capture region: damage dealt, duration, DPS, and abilities.
/// A slain line stops the fight. The next hit starts another. Startup lines stay ignored.
/// </summary>
public sealed class DamageTrackerService
{
    private readonly object _gate = new();
    private readonly DamageLineBuffer _buffer = new();
    private readonly DamageFight _fight = new();
    private readonly DispatcherTimer _clock;
    private string _regionId;

    public ObservableCollection<AbilityDamageRow> Abilities { get; } = [];
    public event Action? Changed;

    public bool HasFight => _fight.HasFight;
    public bool Tracking => _fight.Tracking;
    public int DamageDealt => _fight.DamageDealt;
    public string SlainTarget => _fight.SlainTarget;

    public DamageTrackerService(AppSettings settings)
    {
        _regionId = settings.DamageRegionId ?? string.Empty;
        _clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _clock.Tick += (_, _) => Changed?.Invoke();
    }

    public string DurationDisplay
    {
        get
        {
            if (!_fight.HasFight)
                return "0:00";
            var span = TimeSpan.FromSeconds(_fight.SecondsAt(DateTime.Now));
            return span.TotalHours >= 1
                ? span.ToString(@"h\:mm\:ss")
                : span.ToString(@"m\:ss");
        }
    }

    public string DamageDealtDisplay => _fight.DamageDealt.ToString("N0");

    public string DpsDisplay => _fight.HasFight ? _fight.DpsAt(DateTime.Now).ToString("0.0") : "—";

    public string StatusDisplay
    {
        get
        {
            if (_fight.Tracking)
                return "In fight";
            if (_fight.HasFight && !string.IsNullOrWhiteSpace(_fight.SlainTarget))
                return "Fight ended — you have slain " + _fight.SlainTarget;
            if (_fight.HasFight)
                return "Fight ended";
            return "Waiting for a hit";
        }
    }

    public void UpdateSettings(AppSettings settings)
    {
        var next = settings.DamageRegionId ?? string.Empty;
        var regionChanged = !string.Equals(_regionId, next, StringComparison.Ordinal);
        _regionId = next;
        InvalidateMatchBaseline();
        if (regionChanged)
            ClearFight();
    }

    public void InvalidateMatchBaseline()
    {
        lock (_gate)
            _buffer.Invalidate();
    }

    public void MarkBaselineReady()
    {
        lock (_gate)
            _buffer.MarkReady();
    }

    public void PrimeRegionBaseline(string regionId, IEnumerable<string> lines)
    {
        if (!IsSelected(regionId))
            return;
        lock (_gate)
            _buffer.Prime(lines);
    }

    public void ProcessRegionLines(string regionId, IEnumerable<string> lines)
    {
        if (!IsSelected(regionId))
            return;

        List<string> fresh;
        lock (_gate)
            fresh = _buffer.Observe(lines);
        if (fresh.Count == 0)
            return;

        var actions = new List<Action<DateTime>>();
        foreach (var line in fresh)
        {
            if (DamageLineParser.TryParseSlain(line, out var target))
            {
                var slain = target;
                actions.Add(when => _fight.OnSlain(slain, when));
                continue;
            }

            if (!DamageLineParser.TryParse(line, out var parsed))
                continue;
            var hit = parsed;
            actions.Add(when => _fight.OnDamage(hit, when));
        }

        if (actions.Count == 0)
            return;

        void Apply()
        {
            var when = DateTime.Now;
            foreach (var action in actions)
                action(when);
            PublishAbilities();
            SyncClock();
            Changed?.Invoke();
        }

        if (Application.Current?.Dispatcher.CheckAccess() == true)
            Apply();
        else if (Application.Current is not null)
            Application.Current.Dispatcher.Invoke(Apply);
        else
            Apply();
    }

    public void ClearFight()
    {
        void Apply()
        {
            _fight.Reset();
            PublishAbilities();
            SyncClock();
            Changed?.Invoke();
        }

        if (Application.Current?.Dispatcher.CheckAccess() == true)
            Apply();
        else if (Application.Current is not null)
            Application.Current.Dispatcher.Invoke(Apply);
        else
            Apply();
    }

    private void PublishAbilities()
    {
        Abilities.Clear();
        var dealt = _fight.DamageDealt;
        foreach (var (name, damage) in _fight.Abilities)
        {
            Abilities.Add(new AbilityDamageRow
            {
                Name = name,
                Damage = damage,
                Share = dealt <= 0 ? 0 : damage * 100.0 / dealt
            });
        }
    }

    private void SyncClock()
    {
        if (_fight.Tracking)
        {
            if (!_clock.IsEnabled)
                _clock.Start();
        }
        else if (_clock.IsEnabled)
        {
            _clock.Stop();
        }
    }

    private bool IsSelected(string regionId) =>
        !string.IsNullOrWhiteSpace(_regionId)
        && string.Equals(_regionId, regionId, StringComparison.Ordinal);
}
