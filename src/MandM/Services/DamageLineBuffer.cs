namespace MandM.Services;

/// <summary>
/// Same baseline rule as buffs: the first OCR pass is remembered and does not count.
/// Later passes emit only lines that newly appeared. Wrapped OCR lines are joined first.
/// Punctuation-only changes of a line still on screen are not new hits.
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
        // A blank or failed capture is not a new frame. Keep the last good lines.
        if (current.Count == 0)
            return [];
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

        return DamageLineParser.JoinWrapped(list);
    }

    /// <summary>
    /// Lines whose canonical copy count rose since the previous frame.
    /// A real new swing counts. Reordering, or a dropped exclamation mark on the same line, does not.
    /// </summary>
    public static List<string> NewLines(IReadOnlyList<string> previous, IReadOnlyList<string> current)
    {
        if (previous.Count == 0)
            return current.ToList();

        var was = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in previous)
        {
            var key = DamageLineParser.Canonical(line);
            if (key.Length == 0)
                continue;
            was[key] = was.GetValueOrDefault(key) + 1;
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var added = new List<string>();
        foreach (var line in current)
        {
            var key = DamageLineParser.Canonical(line);
            if (key.Length == 0)
                continue;
            var n = seen.GetValueOrDefault(key) + 1;
            seen[key] = n;
            if (n > was.GetValueOrDefault(key))
                added.Add(line);
        }

        return added;
    }
}
