namespace MandM.Services;

/// <summary>
/// One personal fight. Damage dealt and ability totals run until a slain line.
/// The next outgoing or incoming hit starts a new fight. No party attribution.
/// </summary>
public sealed class DamageFight
{
    private readonly Dictionary<string, int> _abilities = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sealed = new(StringComparer.Ordinal);
    private string _slainCanonical = string.Empty;
    private bool _holding;

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
    /// Apply one OCR frame. A slain line closes the fight. Leftover OCR of that slain
    /// line cannot open another fight. A later outgoing or incoming hit can, even when
    /// the wording matches a line that was on the kill screen.
    /// </summary>
    public bool ApplyFrame(IReadOnlyList<string> fresh, IReadOnlyList<string> visible, DateTime when)
    {
        var slainLine = fresh.FirstOrDefault(DamageLineParser.IsSlainLine)
                        ?? fresh.FirstOrDefault(LooksLikeSlainLeftover);
        if (slainLine is not null && (Tracking || !HasFight))
        {
            foreach (var line in fresh)
            {
                if (IsBlocked(line))
                    continue;
                if (DamageLineParser.TryParse(line, out var hit))
                    OnDamage(hit, when);
            }

            if (Tracking)
            {
                DamageLineParser.TryParseSlain(slainLine, out var slayer, out var target);
                FinishSlain(slayer, target, when);
            }

            Seal(visible, slainLine);
            return true;
        }

        if (_holding || slainLine is not null)
        {
            var starters = new List<ParsedDamage>();
            foreach (var line in fresh)
            {
                if (IsBlocked(line))
                    continue;
                if (DamageLineParser.TryParse(line, out var hit))
                    starters.Add(hit);
            }

            if (starters.Count == 0)
                return false;

            _holding = false;
            _sealed.Clear();
            _slainCanonical = string.Empty;
            foreach (var hit in starters)
                OnDamage(hit, when);
            return true;
        }

        var changed = false;
        foreach (var line in fresh)
        {
            if (IsBlocked(line))
                continue;
            if (!DamageLineParser.TryParse(line, out var hit))
                continue;
            OnDamage(hit, when);
            changed = true;
        }

        return changed;
    }

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
        _sealed.Clear();
        _slainCanonical = string.Empty;
        _holding = false;
    }

    private void Seal(IReadOnlyList<string> visible, string slainLine)
    {
        _sealed.Clear();
        foreach (var line in visible)
        {
            var canonical = DamageLineParser.Canonical(line);
            if (canonical.Length > 0)
                _sealed.Add(canonical);
        }

        var slain = DamageLineParser.Canonical(slainLine);
        if (slain.Length > 0)
        {
            _sealed.Add(slain);
            _slainCanonical = slain;
        }

        _holding = true;
    }

    private bool IsBlocked(string line)
    {
        if (DamageLineParser.IsSlainLine(line) || LooksLikeSlainLeftover(line))
            return true;
        // A real hit is never discarded because its text was on the kill screen.
        if (DamageLineParser.TryParse(line, out _))
            return false;
        var canonical = DamageLineParser.Canonical(line);
        return canonical.Length > 0 && _sealed.Contains(canonical);
    }

    private bool LooksLikeSlainLeftover(string line)
    {
        if (_slainCanonical.Length == 0)
            return false;
        var canonical = DamageLineParser.Canonical(line);
        if (canonical.Length == 0)
            return false;
        if (canonical == _slainCanonical)
            return true;
        if (Math.Abs(canonical.Length - _slainCanonical.Length) > 3)
            return false;
        return EditDistance(canonical, _slainCanonical) <= 2;
    }

    private static int EditDistance(string left, string right)
    {
        var rows = left.Length + 1;
        var cols = right.Length + 1;
        var prev = new int[cols];
        var curr = new int[cols];
        for (var col = 0; col < cols; col++)
            prev[col] = col;
        for (var row = 1; row < rows; row++)
        {
            curr[0] = row;
            for (var col = 1; col < cols; col++)
            {
                var cost = left[row - 1] == right[col - 1] ? 0 : 1;
                curr[col] = Math.Min(
                    Math.Min(curr[col - 1] + 1, prev[col] + 1),
                    prev[col - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[cols - 1];
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
