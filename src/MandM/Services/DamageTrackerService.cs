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

    public bool HasFight { get { lock (_gate) return _fight.HasFight; } }
    public bool Tracking { get { lock (_gate) return _fight.Tracking; } }
    public int DamageDealt { get { lock (_gate) return _fight.DamageDealt; } }
    public string SlainTarget { get { lock (_gate) return _fight.SlainTarget; } }

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
            lock (_gate)
            {
                if (!_fight.HasFight)
                    return "0:00";
                var span = TimeSpan.FromSeconds(_fight.SecondsAt(DateTime.Now));
                return span.TotalHours >= 1
                    ? span.ToString(@"h\:mm\:ss")
                    : span.ToString(@"m\:ss");
            }
        }
    }

    public string DamageDealtDisplay { get { lock (_gate) return _fight.DamageDealt.ToString("N0"); } }

    public string DpsDisplay { get { lock (_gate) return _fight.HasFight ? _fight.DpsAt(DateTime.Now).ToString("0.0") : "—"; } }

    public string StatusDisplay
    {
        get
        {
            lock (_gate)
            {
                if (_fight.Tracking)
                    return "In fight";
                if (_fight.HasFight
                    && _fight.SlainBy.Equals("You", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(_fight.SlainTarget))
                    return "Fight ended — you have slain " + _fight.SlainTarget;
                if (_fight.HasFight
                    && !string.IsNullOrWhiteSpace(_fight.SlainBy)
                    && !string.IsNullOrWhiteSpace(_fight.SlainTarget))
                    return "Fight ended — " + _fight.SlainBy + " has slain " + _fight.SlainTarget;
                if (_fight.HasFight)
                    return "Fight ended";
                return "Waiting for a hit";
            }
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

        var visible = lines as IReadOnlyList<string> ?? lines.ToList();
        List<AbilityDamageRow> rows;
        bool tracking;
        lock (_gate)
        {
            var fresh = _buffer.Observe(visible);
            if (fresh.Count == 0)
                return;
            if (!_fight.ApplyFrame(fresh, visible, DateTime.Now))
                return;
            rows = SnapshotAbilities();
            tracking = _fight.Tracking;
        }

        PostUi(() =>
        {
            Abilities.Clear();
            foreach (var row in rows)
                Abilities.Add(row);
            SyncClock(tracking);
            Changed?.Invoke();
        });
    }

    public void ClearFight()
    {
        lock (_gate)
            _fight.Reset();
        PostUi(() =>
        {
            Abilities.Clear();
            SyncClock(false);
            Changed?.Invoke();
        });
    }

    private List<AbilityDamageRow> SnapshotAbilities()
    {
        var dealt = _fight.DamageDealt;
        var rows = new List<AbilityDamageRow>();
        var index = 0;
        foreach (var (name, damage) in _fight.Abilities)
        {
            rows.Add(new AbilityDamageRow
            {
                Name = name,
                Damage = damage,
                Share = dealt <= 0 ? 0 : damage * 100.0 / dealt,
                Color = AbilityDamageRow.ColorAt(index++)
            });
        }

        return rows;
    }

    private void SyncClock(bool tracking)
    {
        if (tracking)
        {
            if (!_clock.IsEnabled)
                _clock.Start();
        }
        else if (_clock.IsEnabled)
        {
            _clock.Stop();
        }
    }

    private static void PostUi(Action apply)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            apply();
        else
            dispatcher.BeginInvoke(apply);
    }

    private bool IsSelected(string regionId) =>
        !string.IsNullOrWhiteSpace(_regionId)
        && string.Equals(_regionId, regionId, StringComparison.Ordinal);
}
