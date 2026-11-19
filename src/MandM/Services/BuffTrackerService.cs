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
    /// <summary>Buff ids that saw a new cast and are waiting for the next land sighting to restart.</summary>
    private readonly HashSet<string> _armedFromCast = new(StringComparer.Ordinal);
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

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        lock (_gate)
        {
            _previousLandCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            _previousFadeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            _previousCastCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            _armedFromCast.Clear();
        }
    }

    public void ProcessCombatLines(IEnumerable<string> lines)
    {
        // Preserve duplicate rows — a recast often OCR's the same land string again.
        var entries = lines
            .Select(raw => (raw ?? string.Empty).Trim())
            .Where(raw => raw.Length > 0)
            .ToList();

        var blob = string.Join("\n", entries);
        var landCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var fadeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var castCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var def in _settings.Buffs.Where(b => b.Enabled).ToList())
        {
            landCounts[def.Id] = CountMatches(entries, blob, def.LandText);
            fadeCounts[def.Id] = CountMatches(entries, blob, def.FadeText);
            castCounts[def.Id] = CountMatches(entries, blob, def.CastText);
        }

        Dictionary<string, int> prevLand;
        Dictionary<string, int> prevFade;
        Dictionary<string, int> prevCast;
        lock (_gate)
        {
            prevLand = _previousLandCounts;
            prevFade = _previousFadeCounts;
            prevCast = _previousCastCounts;
            _previousLandCounts = landCounts;
            _previousFadeCounts = fadeCounts;
            _previousCastCounts = castCounts;
        }

        foreach (var def in _settings.Buffs.Where(b => b.Enabled).ToList())
        {
            var fadeNow = fadeCounts.GetValueOrDefault(def.Id);
            var fadeWas = prevFade.GetValueOrDefault(def.Id);
            if (fadeNow > fadeWas)
                ClearBuff(def.Id);

            var castNow = castCounts.GetValueOrDefault(def.Id);
            var castWas = prevCast.GetValueOrDefault(def.Id);
            if (castNow > castWas)
            {
                lock (_gate)
                    _armedFromCast.Add(def.Id);
            }

            var landNow = landCounts.GetValueOrDefault(def.Id);
            var landWas = prevLand.GetValueOrDefault(def.Id);

            // 1) New land line appearance (count went up) — including recast while active.
            var newLandOccurrence = landNow > landWas;
            // 2) Cast was seen, then land is visible (handles sticky identical land OCR that
            //    never increases count when the old land line is still on screen).
            bool castArmedLand;
            lock (_gate)
                castArmedLand = landNow > 0 && _armedFromCast.Remove(def.Id);

            if (newLandOccurrence || castArmedLand)
                LandBuff(def);
        }
    }

    private static int CountMatches(List<string> entries, string blob, string? needle)
    {
        if (string.IsNullOrWhiteSpace(needle))
            return 0;

        var count = entries.Count(line => TextMatch.ContainsLoose(line, needle));
        // Phrase split across OCR lines: one presence if the joined frame matches.
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

            // Always restart from full duration — even when already active.
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

            if (!buff.Warned && remaining.TotalSeconds <= buff.Definition.WarnSeconds)
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
