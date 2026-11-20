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
    /// <summary>Canonical debuff land lines still on screen, per spell id.</summary>
    private readonly Dictionary<string, HashSet<string>> _seenDebuffLand = new(StringComparer.Ordinal);
    /// <summary>Canonical debuff fade lines still on screen, per spell id.</summary>
    private readonly Dictionary<string, HashSet<string>> _seenDebuffFade = new(StringComparer.Ordinal);
    /// <summary>Canonical slain lines still on screen, per capture region.</summary>
    private readonly Dictionary<string, HashSet<string>> _seenSlain = new(StringComparer.Ordinal);
    /// <summary>Newly appeared slain lines this OCR pass. Cleared after every region is applied.</summary>
    private readonly List<string> _newSlainThisPass = [];
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
        DropUntrackedDebuffs();
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
            _seenDebuffLand.Clear();
            _seenDebuffFade.Clear();
            _seenSlain.Clear();
            _newSlainThisPass.Clear();
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
            if (def.IsDebuff)
            {
                ApplyDebuffLines(regionId, entries, blob, def, fireEvents);
                continue;
            }

            // Order: cast → land → warn → fade.
            // Warn before fade so a warn line that also loosely resembles fade still alarms
            // before any clear, and fade uses specificity against warn/land/cast.

            if (RegionEquals(def.CastRegionId, regionId))
            {
                var castNow = CountPhraseMatches(entries, blob, def, def.CastText);
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
                var landNow = CountPhraseMatches(entries, blob, def, def.LandText);
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
                var warnNow = CountPhraseMatches(entries, blob, def, def.WarnText);
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

            if (RegionEquals(def.FadeRegionId, regionId))
            {
                var fadeNow = CountPhraseMatches(entries, blob, def, def.FadeText);
                int fadeWas;
                bool allow;
                lock (_gate)
                {
                    fadeWas = _previousFadeCounts.GetValueOrDefault(def.Id);
                    _previousFadeCounts[def.Id] = fadeNow;
                    allow = fireEvents && _acceptMatchEvents;
                }

                if (allow && fadeNow > fadeWas)
                    FadeBuff(def);
            }
        }

        NoteSlainLines(regionId, entries, fireEvents);
    }

    /// <summary>
    /// Debuffs have no countdown and no warn. Cast text is stored and does not alert.
    /// A new land line starts that monster and plays the alert. A new fade line plays
    /// once and clears the named monster, or every copy when the fade line has no name.
    /// </summary>
    private void ApplyDebuffLines(
        string regionId,
        List<string> entries,
        string blob,
        BuffDefinition def,
        bool fireEvents)
    {
        _ = blob;

        if (RegionEquals(def.LandRegionId, regionId))
        {
            var matched = MatchingDebuffLines(entries, def.LandText, PeerPhrases(def, def.LandText));
            var fresh = TakeFresh(def.Id, matched, _seenDebuffLand, fireEvents);
            foreach (var line in fresh)
            {
                if (DebuffPhrase.TryRead(line, def.LandText, PeerPhrases(def, def.LandText), out var monster))
                    LandDebuff(def, monster);
            }
        }

        if (!string.IsNullOrWhiteSpace(def.FadeText) && RegionEquals(def.FadeRegionId, regionId))
        {
            var matched = MatchingDebuffLines(entries, def.FadeText, PeerPhrases(def, def.FadeText));
            var fresh = TakeFresh(def.Id, matched, _seenDebuffFade, fireEvents);
            var monsters = new List<string>();
            foreach (var line in fresh)
            {
                if (DebuffPhrase.TryRead(line, def.FadeText, PeerPhrases(def, def.FadeText), out var monster))
                    monsters.Add(monster);
            }

            if (monsters.Count == 0)
                return;

            if (monsters.Any(string.IsNullOrWhiteSpace))
            {
                FadeDebuffCopies(def, all: true, monster: string.Empty);
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var monster in monsters)
            {
                if (seen.Add(DebuffPhrase.Key(monster)))
                    FadeDebuffCopies(def, all: false, monster: monster);
            }
        }
    }

    private static List<string> MatchingDebuffLines(
        List<string> entries,
        string? phrase,
        IEnumerable<string> peers)
    {
        var matched = new List<string>();
        if (string.IsNullOrWhiteSpace(phrase))
            return matched;

        foreach (var line in entries)
        {
            if (DebuffPhrase.TryRead(line, phrase, peers, out _))
                matched.Add(line);
        }

        return matched;
    }

    private static IEnumerable<string> PeerPhrases(BuffDefinition def, string? phrase)
    {
        if (!string.IsNullOrWhiteSpace(def.LandText)
            && !string.Equals(def.LandText, phrase, StringComparison.OrdinalIgnoreCase))
            yield return def.LandText;
        if (!string.IsNullOrWhiteSpace(def.FadeText)
            && !string.Equals(def.FadeText, phrase, StringComparison.OrdinalIgnoreCase))
            yield return def.FadeText;
    }

    private List<string> TakeFresh(
        string spellId,
        List<string> matched,
        Dictionary<string, HashSet<string>> store,
        bool fireEvents)
    {
        lock (_gate)
        {
            if (!store.TryGetValue(spellId, out var seen))
            {
                seen = new HashSet<string>(StringComparer.Ordinal);
                store[spellId] = seen;
            }

            var fresh = DebuffPhrase.FreshLines(seen, matched);
            if (!fireEvents || !_acceptMatchEvents)
                return [];
            return fresh;
        }
    }

    /// <summary>
    /// Same slain rule as the fight tracker, including a party member's kill.
    /// Only a line that was not already on screen counts. The clear waits until the
    /// pass finishes so a land in a later region of the same pass does not stick.
    /// </summary>
    private void NoteSlainLines(string regionId, List<string> entries, bool fireEvents)
    {
        var joined = DamageLineParser.JoinWrapped(entries);
        var slain = joined.Where(DamageLineParser.IsSlainLine).ToList();
        lock (_gate)
        {
            if (!_seenSlain.TryGetValue(regionId, out var seen))
            {
                seen = new HashSet<string>(StringComparer.Ordinal);
                _seenSlain[regionId] = seen;
            }

            var fresh = DebuffPhrase.FreshLines(seen, slain);
            if (fireEvents && _acceptMatchEvents)
                _newSlainThisPass.AddRange(fresh);
        }
    }

    /// <summary>Call after every capture region in one OCR pass has been applied.</summary>
    public void FinishPass()
    {
        List<string> slain;
        lock (_gate)
        {
            slain = _newSlainThisPass.ToList();
            _newSlainThisPass.Clear();
        }

        if (slain.Count > 0)
            ClearDebuffsForSlain(slain);
    }

    /// <summary>
    /// Count lines that match <paramref name="needle"/> for this buff, but let a longer
    /// configured phrase that also matches "own" the line (warn over short fade, etc.).
    /// </summary>
    private static int CountPhraseMatches(
        List<string> entries,
        string blob,
        BuffDefinition def,
        string? needle)
    {
        if (string.IsNullOrWhiteSpace(needle))
            return 0;

        var count = entries.Count(line => LineOwnsPhrase(line, def, needle));
        if (count == 0 && LineOwnsPhrase(blob, def, needle))
            return 1;
        return count;
    }

    private static bool LineOwnsPhrase(string line, BuffDefinition def, string needle)
    {
        if (!TextMatch.ContainsLoose(line, needle))
            return false;

        // Longer same-buff phrases that also match steal the line from shorter ones.
        // Example: fade "You appear" must not claim warn "You begin to feel yourself appearing".
        foreach (var other in PhrasesOf(def))
        {
            if (string.Equals(other, needle, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!TextMatch.ContainsLoose(line, other))
                continue;

            var otherNorm = TextMatch.Normalize(other);
            var needleNorm = TextMatch.Normalize(needle);
            if (otherNorm.Length > needleNorm.Length)
                return false;

            // Same length rare; if the other phrase contains this needle, prefer other.
            if (otherNorm.Length == needleNorm.Length)
                continue;
            if (TextMatch.ContainsLoose(other, needle))
                return false;
        }

        return true;
    }

    private static IEnumerable<string> PhrasesOf(BuffDefinition def)
    {
        if (!def.IsDebuff && !string.IsNullOrWhiteSpace(def.CastText)) yield return def.CastText;
        if (!string.IsNullOrWhiteSpace(def.LandText)) yield return def.LandText;
        if (!string.IsNullOrWhiteSpace(def.FadeText)) yield return def.FadeText;
        if (!def.IsDebuff && !string.IsNullOrWhiteSpace(def.WarnText)) yield return def.WarnText;
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
            _alarms.PlayAlert(
                def.AlertMode,
                def.AlarmSound,
                def.WarnVoiceText,
                $"{def.Name} warning");
            Changed?.Invoke();
        }

        PostUi(Apply);
    }

    private void FadeBuff(BuffDefinition def)
    {
        void Apply()
        {
            var existing = ActiveBuffs.FirstOrDefault(b => b.Definition.Id == def.Id);
            if (existing is null) return;
            ActiveBuffs.Remove(existing);
            _alarms.PlayAlert(
                def.AlertMode,
                def.AlarmSound,
                def.FadeVoiceText,
                $"{def.Name} faded");
            Changed?.Invoke();
        }

        PostUi(Apply);
    }

    private void ClearDebuffsForSlain(IReadOnlyList<string> slainLines)
    {
        void Apply()
        {
            var tracked = ActiveBuffs.Where(b => b.IsDebuff).Select(b => b.TargetName).ToList();
            if (tracked.Count == 0)
                return;

            var changed = false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in slainLines)
            {
                var key = DamageLineParser.Canonical(line);
                if (key.Length == 0 || !seen.Add(key))
                    continue;

                DamageLineParser.TryParseSlain(line, out _, out var target);
                if (tracked.Any(name => DebuffPhrase.SameMonster(name, target)))
                {
                    var named = ActiveBuffs
                        .Where(b => b.IsDebuff && DebuffPhrase.SameMonster(b.TargetName, target))
                        .ToList();
                    foreach (var buff in named)
                        ActiveBuffs.Remove(buff);
                    changed |= named.Count > 0;
                    continue;
                }

                var all = ActiveBuffs.Where(b => b.IsDebuff).ToList();
                foreach (var buff in all)
                    ActiveBuffs.Remove(buff);
                changed |= all.Count > 0;
            }

            if (changed)
                Changed?.Invoke();
        }

        PostUi(Apply);
    }

    private static bool RegionEquals(string? configured, string regionId) =>
        !string.IsNullOrWhiteSpace(configured)
        && string.Equals(configured, regionId, StringComparison.Ordinal);

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

        PostUi(Apply);
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

        PostUi(Apply);
    }

    /// <summary>
    /// Play the land alert. A debuff with no fade text is not shown.
    /// A debuff with fade text is tracked, with the monster name, until fade or slain.
    /// </summary>
    private void LandDebuff(BuffDefinition def, string monster)
    {
        void Apply()
        {
            if (string.IsNullOrWhiteSpace(def.FadeText))
            {
                _alarms.PlayAlert(
                    def.AlertMode,
                    def.AlarmSound,
                    def.WarnVoiceText,
                    def.Name);
                return;
            }

            var existing = ActiveBuffs.FirstOrDefault(b =>
                b.Definition.Id == def.Id && DebuffPhrase.SameSlot(b.TargetName, monster));
            if (existing is not null)
                ActiveBuffs.Remove(existing);

            ActiveBuffs.Add(new ActiveBuff
            {
                Definition = def,
                TargetName = monster.Trim(),
                LandedAtUtc = DateTime.UtcNow,
                Remaining = TimeSpan.MaxValue,
                Warned = false
            });
            _alarms.PlayAlert(
                def.AlertMode,
                def.AlarmSound,
                def.WarnVoiceText,
                def.Name);
            Changed?.Invoke();
        }

        PostUi(Apply);
    }

    /// <summary>Empty fade text is an alert only, so those rows do not stay on the list or overlay.</summary>
    private void DropUntrackedDebuffs()
    {
        void Apply()
        {
            var drop = ActiveBuffs.Where(b =>
            {
                if (!b.IsDebuff)
                    return false;
                var current = _settings.Buffs.FirstOrDefault(d => d.Id == b.Definition.Id);
                return current is null || string.IsNullOrWhiteSpace(current.FadeText);
            }).ToList();
            if (drop.Count == 0)
                return;
            foreach (var buff in drop)
                ActiveBuffs.Remove(buff);
            Changed?.Invoke();
        }

        PostUi(Apply);
    }

    /// <summary>
    /// Fade one monster, or every copy of this debuff when the fade line names nobody.
    /// The alert plays once.
    /// </summary>
    private void FadeDebuffCopies(BuffDefinition def, bool all, string monster)
    {
        void Apply()
        {
            var doomed = ActiveBuffs.Where(b =>
                b.IsDebuff
                && b.Definition.Id == def.Id
                && (all || DebuffPhrase.SameMonster(b.TargetName, monster))).ToList();
            if (doomed.Count == 0)
                return;

            foreach (var buff in doomed)
                ActiveBuffs.Remove(buff);
            _alarms.PlayAlert(
                def.ResolvedFadeAlertMode,
                def.ResolvedFadeAlarmSound,
                def.FadeVoiceText,
                $"{def.Name} faded");
            Changed?.Invoke();
        }

        PostUi(Apply);
    }

    public void ClearBuff(string buffId)
    {
        void Apply()
        {
            var existing = ActiveBuffs.Where(b => b.Definition.Id == buffId).ToList();
            if (existing.Count == 0) return;
            foreach (var buff in existing)
                ActiveBuffs.Remove(buff);
            Changed?.Invoke();
        }

        PostUi(Apply);
    }

    /// <summary>Manual test helper — lands a spell without OCR.</summary>
    public void TestLand(BuffDefinition def)
    {
        if (def.IsDebuff)
            LandDebuff(def, string.Empty);
        else
            LandBuff(def);
    }

    private static void PostUi(Action apply)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            apply();
        else
            dispatcher.BeginInvoke(apply);
    }

    private void Tick()
    {
        var now = DateTime.UtcNow;
        foreach (var buff in ActiveBuffs.ToList())
        {
            if (buff.IsDebuff)
                continue;

            var elapsed = now - buff.LandedAtUtc;
            var remaining = TimeSpan.FromSeconds(buff.Definition.DurationSeconds) - elapsed;
            buff.Remaining = remaining;

            if (remaining <= TimeSpan.Zero)
                continue;

            if (string.IsNullOrWhiteSpace(buff.Definition.WarnText)
                && !buff.Warned
                && remaining.TotalSeconds <= buff.Definition.WarnSeconds)
            {
                buff.Warned = true;
                _alarms.PlayAlert(
                    buff.Definition.AlertMode,
                    buff.Definition.AlarmSound,
                    buff.Definition.WarnVoiceText,
                    $"{buff.Definition.Name} warning");
            }
        }

        var expired = ActiveBuffs.Where(b => !b.IsDebuff && b.Remaining <= TimeSpan.Zero).ToList();
        if (expired.Count == 0) return;
        foreach (var buff in expired)
            ActiveBuffs.Remove(buff);
        Changed?.Invoke();
    }

    public void Dispose() => _tick.Stop();
}
