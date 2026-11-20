namespace MandM.Services;

/// <summary>
/// One personal fight. Damage dealt and ability totals run until a slain line.
/// The next outgoing or incoming hit starts a new fight. No party attribution.
/// </summary>
public sealed class DamageFight
{
    private readonly Dictionary<string, int> _abilities = new(StringComparer.OrdinalIgnoreCase);

    public bool Tracking { get; private set; }
    public bool HasFight { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? EndedAt { get; private set; }
    public string SlainBy { get; private set; } = string.Empty;
    public string SlainTarget { get; private set; } = string.Empty;
    public int DamageDealt { get; private set; }

    public IReadOnlyList<(string Name, int Damage)> Abilities =>
        _abilities
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => (pair.Key, pair.Value))
            .ToList();

    /// <summary>
    /// Apply only lines that newly appeared. A new outgoing or incoming hit starts or
    /// continues the fight. A new slain line (you or a party member) ends it.
    /// Lines still sitting on screen are not in <paramref name="fresh"/> and are ignored.
    /// </summary>
    public bool ApplyFrame(IReadOnlyList<string> fresh, IReadOnlyList<string> visible, DateTime when)
    {
        _ = visible;
        var hits = new List<ParsedDamage>();
        string? slainLine = null;
        foreach (var line in fresh)
        {
            if (DamageLineParser.IsSlainLine(line))
            {
                slainLine = line;
                continue;
            }

            if (DamageLineParser.TryParse(line, out var hit))
                hits.Add(hit);
        }

        var changed = false;
        if (hits.Count > 0)
        {
            if (!Tracking)
                Begin(when);
            foreach (var hit in hits)
                AddHit(hit);
            changed = true;
        }

        if (slainLine is not null && Tracking)
        {
            DamageLineParser.TryParseSlain(slainLine, out var slayer, out var target);
            FinishSlain(slayer, target, when);
            changed = true;
        }

        return changed;
    }

    public void OnDamage(ParsedDamage hit, DateTime when)
    {
        if (!Tracking)
            Begin(when);
        AddHit(hit);
    }

    private void AddHit(ParsedDamage hit)
    {
        if (!hit.Outgoing)
            return;
        DamageDealt += hit.Amount;
        var name = AbilityLabel(hit.Detail);
        _abilities[name] = _abilities.GetValueOrDefault(name) + hit.Amount;
    }

    public void OnSlain(string target, DateTime when) => FinishSlain("You", target, when);

    private void FinishSlain(string slayer, string target, DateTime when)
    {
        if (!Tracking)
            return;
        Tracking = false;
        EndedAt = when;
        SlainBy = string.IsNullOrWhiteSpace(slayer) ? string.Empty : slayer.Trim();
        SlainTarget = string.IsNullOrWhiteSpace(target) ? string.Empty : target.Trim();
    }

    /// <summary>
    /// Stop the clock without a slain line and without clearing the totals.
    /// The next damage line starts a new fight. A slain line while not tracking does nothing.
    /// </summary>
    public void Hold(DateTime when)
    {
        if (!Tracking)
            return;
        Tracking = false;
        EndedAt = when;
    }

    /// <summary>Drop a span out of the running clock, such as time spent paused.</summary>
    public void Exclude(TimeSpan span)
    {
        if (span <= TimeSpan.Zero || StartedAt is null)
            return;
        StartedAt = StartedAt.Value + span;
    }

    public void Reset()
    {
        Tracking = false;
        HasFight = false;
        StartedAt = null;
        EndedAt = null;
        SlainBy = string.Empty;
        SlainTarget = string.Empty;
        DamageDealt = 0;
        _abilities.Clear();
    }

    public double SecondsAt(DateTime now)
    {
        if (StartedAt is null)
            return 0;
        var end = Tracking ? now : EndedAt ?? now;
        if (end < StartedAt.Value)
            end = StartedAt.Value;
        return (end - StartedAt.Value).TotalSeconds;
    }

    /// <summary>Damage dealt divided by fight seconds. The first second uses 1 so DPS is not infinite.</summary>
    public double DpsAt(DateTime now)
    {
        if (DamageDealt <= 0)
            return 0;
        return DamageDealt / Math.Max(1, SecondsAt(now));
    }

    public static string AbilityLabel(string? detail)
    {
        var text = (detail ?? string.Empty).Trim();
        if (text.Length == 0 || text.Equals("taken", StringComparison.OrdinalIgnoreCase))
            return "Hit";
        if (text.Contains(' '))
            return text;
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private void Begin(DateTime when)
    {
        _abilities.Clear();
        DamageDealt = 0;
        SlainBy = string.Empty;
        SlainTarget = string.Empty;
        EndedAt = null;
        StartedAt = when;
        Tracking = true;
        HasFight = true;
    }
}
