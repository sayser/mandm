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
    public string SlainTarget { get; private set; } = string.Empty;
    public int DamageDealt { get; private set; }

    public IReadOnlyList<(string Name, int Damage)> Abilities =>
        _abilities
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => (pair.Key, pair.Value))
            .ToList();

    public void OnDamage(ParsedDamage hit, DateTime when)
    {
        if (!Tracking)
            Begin(when);
        if (!hit.Outgoing)
            return;

        DamageDealt += hit.Amount;
        var name = AbilityLabel(hit.Detail);
        _abilities[name] = _abilities.GetValueOrDefault(name) + hit.Amount;
    }

    public void OnSlain(string target, DateTime when)
    {
        if (!Tracking)
            return;
        Tracking = false;
        EndedAt = when;
        SlainTarget = string.IsNullOrWhiteSpace(target) ? string.Empty : target.Trim();
    }

    public void Reset()
    {
        Tracking = false;
        HasFight = false;
        StartedAt = null;
        EndedAt = null;
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
        SlainTarget = string.Empty;
        EndedAt = null;
        StartedAt = when;
        Tracking = true;
        HasFight = true;
    }
}
