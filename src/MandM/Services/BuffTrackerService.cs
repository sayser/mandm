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
    /// <summary>OCR line match counts from the previous poll (per buff id).</summary>
    private Dictionary<string, int> _previousLandCounts = new(StringComparer.Ordinal);
    private Dictionary<string, int> _previousFadeCounts = new(StringComparer.Ordinal);
    private Dictionary<string, int> _previousCastCounts = new(StringComparer.Ordinal);
    private Dictionary<string, int> _previousWarnCounts = new(StringComparer.Ordinal);
    /// <summary>Buff ids that saw a new cast and are waiting for the next land sighting to restart.</summary>
    private readonly HashSet<string> _armedFromCast = new(StringComparer.Ordinal);
    private AppSettings _settings;
    /// <summary>False until an OCR baseline pass finishes — blocks land/warn/fade/cast actions.</summary>
    private bool _acceptMatchEvents;

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

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        InvalidateMatchBaseline();
    }

    /// <summary>
    /// Clear match counters so the next OCR pass must re-baseline.
    /// Prevents mid-session settings saves from treating on-screen text as brand new.
    /// </summary>
    public void InvalidateMatchBaseline()
    {
        lock (_gate)
        {
            _acceptMatchEvents = false;
            _previousLandCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            _previousFadeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            _previousCastCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            _previousWarnCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            _armedFromCast.Clear();
        }
    }

    public void MarkBaselineReady()
    {
        lock (_gate)
            _acceptMatchEvents = true;
    }

    /// <summary>
    /// Snapshot current OCR for a region into match counters without starting/resetting/warning/fading.
    /// Used at monitor start so leftover chat lines do not look like new events.
    /// </summary>
    public void PrimeRegionBaseline(string regionId, IEnumerable<string> lines) =>
        ApplyRegionLines(regionId, lines, fireEvents: false);

    /// <summary>OCR lines from one named capture region (live monitoring).</summary>
    public void ProcessRegionLines(string regionId, IEnumerable<string> lines)
    {
        // If a baseline is pending (startup / settings change), only seed counters.
        bool accept;
        lock (_gate)
            accept = _acceptMatchEvents;
        ApplyRegionLines(regionId, lines, fireEvents: accept);
    }

    private void ApplyRegionLines(string regionId, IEnumerable<string> lines, bool fireEvents)
    {
        if (string.IsNullOrWhiteSpace(regionId))
            return;

        var entries = lines
            .Select(raw => (raw ?? string.Empty).Trim())
            .Where(raw => raw.Length > 0)
            .ToList();
        var blob = string.Join("\n", entries);

        foreach (var def in _settings.Buffs.Where(b => b.Enabled).ToList())
        {
            if (RegionEquals(def.FadeRegionId, regionId))
            {
                var fadeNow = CountMatches(entries, blob, def.FadeText);
                int fadeWas;
                bool allow;
                lock (_gate)
                {
                    fadeWas = _previousFadeCounts.GetValueOrDefault(def.Id);
                    _previousFadeCounts[def.Id] = fadeNow;
                    allow = fireEvents && _acceptMatchEvents;
                }

                if (allow && fadeNow > fadeWas)
                    ClearBuff(def.Id);
            }

            if (RegionEquals(def.CastRegionId, regionId))
            {
                var castNow = CountMatches(entries, blob, def.CastText);
                int castWas;
                bool allow;
                lock (_gate)
                {
                    castWas = _previousCastCounts.GetValueOrDefault(def.Id);
                    _previousCastCounts[def.Id] = castNow;
                    allow = fireEvents && _acceptMatchEvents;
                    if (allow && castNow > castWas)
                        _armedFromCast.Add(def.Id);
                }
            }

            if (RegionEquals(def.LandRegionId, regionId))
            {
                var landNow = CountMatches(entries, blob, def.LandText);
                int landWas;
                bool newLandOccurrence;
                bool castArmedLand;
                lock (_gate)
                {
                    landWas = _previousLandCounts.GetValueOrDefault(def.Id);
                    _previousLandCounts[def.Id] = landNow;
                    newLandOccurrence = fireEvents && _acceptMatchEvents && landNow > landWas;
                    castArmedLand = fireEvents && _acceptMatchEvents
                                    && landNow > 0
                                    && _armedFromCast.Remove(def.Id);
                }

                if (newLandOccurrence || castArmedLand)
                    LandBuff(def);
            }

            if (RegionEquals(def.WarnRegionId, regionId) && !string.IsNullOrWhiteSpace(def.WarnText))
            {
                var warnNow = CountMatches(entries, blob, def.WarnText);
                int warnWas;
                bool allow;
                lock (_gate)
                {
                    warnWas = _previousWarnCounts.GetValueOrDefault(def.Id);
                    _previousWarnCounts[def.Id] = warnNow;
                    allow = fireEvents && _acceptMatchEvents;
                }

                if (allow && warnNow > warnWas)
                    TriggerWarnFromText(def);
            }
        }
    }

    private void TriggerWarnFromText(BuffDefinition def)
    {
        void Apply()
        {
            var active = ActiveBuffs.FirstOrDefault(b => b.Definition.Id == def.Id);
            if (active is null)
                return;
            if (active.Warned)
                return;

            active.Warned = true;
            _alarms.Play(def.AlarmSound);
            Changed?.Invoke();
        }

        if (Application.Current?.Dispatcher.CheckAccess() == true)
            Apply();
        else
            Application.Current?.Dispatcher.Invoke(Apply);
    }

    private static bool RegionEquals(string? configured, string regionId) =>
        !string.IsNullOrWhiteSpace(configured)
        && string.Equals(configured, regionId, StringComparison.Ordinal);

    private static int CountMatches(List<string> entries, string blob, string? needle)
    {
        if (string.IsNullOrWhiteSpace(needle))
            return 0;

        var count = entries.Count(line => TextMatch.ContainsLoose(line, needle));
        if (count == 0 && TextMatch.ContainsLoose(blob, needle))
            return 1;
        return count;
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
                Remaining = TimeSpan.FromSeconds(Math.Max(1, def.DurationSeconds)),
                Warned = false
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
        foreach (var buff in ActiveBuffs.ToList())
        {
            var elapsed = now - buff.LandedAtUtc;
            var remaining = TimeSpan.FromSeconds(buff.Definition.DurationSeconds) - elapsed;
            buff.Remaining = remaining;

            if (remaining <= TimeSpan.Zero)
                continue;

            // Time-based warn only when no warn OCR phrase is configured.
            if (string.IsNullOrWhiteSpace(buff.Definition.WarnText)
                && !buff.Warned
                && remaining.TotalSeconds <= buff.Definition.WarnSeconds)
            {
                buff.Warned = true;
                _alarms.Play(buff.Definition.AlarmSound);
            }
        }

        var expired = ActiveBuffs.Where(b => b.Remaining <= TimeSpan.Zero).ToList();
        if (expired.Count == 0) return;
        foreach (var buff in expired)
            ActiveBuffs.Remove(buff);
        Changed?.Invoke();
    }

    public void Dispose() => _tick.Stop();
}
