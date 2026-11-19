namespace MandM.Services;

/// <summary>
/// Same baseline rule as buffs: the first OCR pass is remembered and does not count.
/// Later passes emit only lines that newly appeared (a scroll-in, or a higher copy count).
/// </summary>
public sealed class DamageLineBuffer
{
    private List<string> _previous = [];
    private bool _accept;

    public void Invalidate()
    {
        _accept = false;
        _previous = [];
    }

    public void MarkReady() => _accept = true;

    /// <summary>Seed the visible lines without treating them as new.</summary>
    public void Prime(IEnumerable<string> lines) => _previous = Normalize(lines);

    public List<string> Observe(IEnumerable<string> lines)
    {
        var current = Normalize(lines);
        if (!_accept)
        {
            _previous = current;
            return [];
        }

        var fresh = NewLines(_previous, current);
        _previous = current;
        return fresh;
    }

    public static List<string> Normalize(IEnumerable<string> lines)
    {
        var list = new List<string>();
        foreach (var raw in lines)
        {
            var collapsed = DamageLineParser.Collapse(raw);
            if (collapsed.Length > 0)
                list.Add(collapsed);
        }

        return list;
    }

    /// <summary>
    /// Lines whose copy count rose since the previous frame.
    /// A count increase is a new event, same as buffs. Reordering the same text does not count.
    /// </summary>
    public static List<string> NewLines(IReadOnlyList<string> previous, IReadOnlyList<string> current)
    {
        if (previous.Count == 0)
            return current.ToList();

        var was = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in previous)
            was[line] = was.GetValueOrDefault(line) + 1;

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var added = new List<string>();
        foreach (var line in current)
        {
            var n = seen.GetValueOrDefault(line) + 1;
            seen[line] = n;
            if (n > was.GetValueOrDefault(line))
                added.Add(line);
        }

        return added;
    }
}
