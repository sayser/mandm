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
    /// <summary>Normalized OCR lines from the previous poll — used to detect newly appeared lines.</summary>
    private HashSet<string> _previousLineKeys = new(StringComparer.OrdinalIgnoreCase);
    private string _previousBlob = string.Empty;
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
        // Buff list / land text may have changed — re-scan the next OCR frame as all-new
        // so a line still on screen (e.g. "You go berserk.") can land a newly added buff.
        lock (_gate)
        {
            _previousLineKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _previousBlob = string.Empty;
        }
    }

    public void ProcessCombatLines(IEnumerable<string> lines)
    {
        var entries = lines
            .Select(raw => (raw ?? string.Empty).Trim())
            .Where(raw => raw.Length > 0)
            .Select(raw => (Raw: raw, Key: TextMatch.Normalize(raw)))
            .Where(t => t.Key.Length > 0)
            .GroupBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var currentKeys = entries.Select(e => e.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var blob = string.Join("\n", entries.Select(e => e.Raw));

        HashSet<string> previousKeys;
        string previousBlob;
        lock (_gate)
        {
            previousKeys = _previousLineKeys;
            previousBlob = _previousBlob;
            _previousLineKeys = currentKeys;
            _previousBlob = blob;
        }

        var newEntries = entries.Where(e => !previousKeys.Contains(e.Key)).ToList();

        foreach (var (raw, _) in newEntries)
            ApplyLine(raw);

        // Land phrases split across OCR lines ("You go" + "berserk") or only visible as a group:
        // fire when the joined frame newly contains the land/fade text.
        foreach (var def in _settings.Buffs.Where(b => b.Enabled).ToList())
        {
            if (!string.IsNullOrWhiteSpace(def.FadeText) &&
                TextMatch.ContainsLoose(blob, def.FadeText) &&
                !TextMatch.ContainsLoose(previousBlob, def.FadeText))
            {
                ClearBuff(def.Id);
            }

            if (!string.IsNullOrWhiteSpace(def.LandText) &&
                TextMatch.ContainsLoose(blob, def.LandText) &&
                !TextMatch.ContainsLoose(previousBlob, def.LandText))
            {
                // Avoid double-land if a new single line already matched this buff above.
                if (!newEntries.Any(e => TextMatch.ContainsLoose(e.Raw, def.LandText)))
                    LandBuff(def);
            }
        }
    }

    private void ApplyLine(string line)
    {
        foreach (var def in _settings.Buffs.Where(b => b.Enabled).ToList())
        {
            if (!string.IsNullOrWhiteSpace(def.FadeText) && TextMatch.ContainsLoose(line, def.FadeText))
            {
                ClearBuff(def.Id);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(def.LandText) && TextMatch.ContainsLoose(line, def.LandText))
                LandBuff(def);
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
                Remaining = TimeSpan.FromSeconds(Math.Max(1, def.DurationSeconds))
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
