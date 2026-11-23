namespace MandM.Services;

/// <summary>
/// Last successful OCR read for one capture region.
/// A blank frame, a failed OCR, focus loss, and alt-tab do not clear it.
/// A line drops only after two successful reads that do not contain it.
/// </summary>
public sealed class RegionLineMemory
{
    public const int MissesBeforeDrop = 2;
    public const int MaxLines = 48;
    private const int MinFragment = 8;

    private sealed class Hold
    {
        public int Count;
        public int Misses;
        public int Stamp;
    }

    private readonly Dictionary<string, Hold> _lines = new(StringComparer.Ordinal);
    private int _stamp;

    public int Count => _lines.Count;

    public void Clear() => _lines.Clear();

    /// <summary>
    /// Lines that were not in the previous good read.
    /// Punctuation, spacing, and a wrapped fragment of a remembered line are not new.
    /// An empty or failed read returns nothing and leaves memory as it is.
    /// </summary>
    public List<string> Observe(IEnumerable<string>? lines)
    {
        var normalized = Normalize(lines);
        if (normalized.Count == 0)
            return [];

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var display = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new List<string>(normalized.Count);
        foreach (var line in normalized)
        {
            var key = DamageLineParser.Canonical(line);
            if (key.Length == 0)
                continue;
            if (counts.TryGetValue(key, out var n))
            {
                counts[key] = n + 1;
                continue;
            }

            counts[key] = 1;
            display[key] = line;
            order.Add(key);
        }

        if (order.Count == 0)
            return [];

        var fresh = new List<string>();
        var matched = new HashSet<string>(StringComparer.Ordinal);
        var absorbed = new HashSet<string>(StringComparer.Ordinal);
        var remainders = new HashSet<string>(StringComparer.Ordinal);

        foreach (var key in order)
        {
            if (!_lines.TryGetValue(key, out var hold))
                continue;
            if (counts[key] > hold.Count)
                fresh.Add(display[key]);
            hold.Count = counts[key];
            hold.Misses = 0;
            hold.Stamp = ++_stamp;
            matched.Add(key);
        }

        foreach (var key in order)
        {
            if (matched.Contains(key))
                continue;
            TryAbsorb(key, matched, absorbed, remainders);
        }

        foreach (var key in order)
        {
            if (matched.Contains(key) || absorbed.Contains(key) || remainders.Contains(key))
                continue;
            fresh.Add(display[key]);
            _lines[key] = new Hold
            {
                Count = counts[key],
                Misses = 0,
                Stamp = ++_stamp
            };
            matched.Add(key);
        }

        var drop = new List<string>();
        foreach (var pair in _lines)
        {
            if (matched.Contains(pair.Key))
                continue;
            pair.Value.Misses++;
            if (pair.Value.Misses >= MissesBeforeDrop)
                drop.Add(pair.Key);
        }

        foreach (var key in drop)
            _lines.Remove(key);

        Trim();
        return fresh;
    }

    private void TryAbsorb(
        string current,
        HashSet<string> matched,
        HashSet<string> absorbed,
        HashSet<string> remainders)
    {
        if (current.Length < MinFragment)
            return;

        string? best = null;
        var bestLength = 0;
        foreach (var key in _lines.Keys)
        {
            if (key.Length <= current.Length || key.Length <= bestLength)
                continue;
            if (!key.StartsWith(current, StringComparison.Ordinal)
                && !key.EndsWith(current, StringComparison.Ordinal))
                continue;
            best = key;
            bestLength = key.Length;
        }

        if (best is null)
            return;

        var hold = _lines[best];
        hold.Misses = 0;
        hold.Stamp = ++_stamp;
        matched.Add(best);
        absorbed.Add(current);

        var rest = best.StartsWith(current, StringComparison.Ordinal)
            ? best[current.Length..].Trim()
            : best[..^current.Length].Trim();
        if (rest.Length > 0)
            remainders.Add(rest);
    }

    private void Trim()
    {
        // Lines still on this read stay. Only text that has already left is capped.
        while (_lines.Count > MaxLines)
        {
            string? drop = null;
            var worstMiss = 0;
            var oldest = int.MaxValue;
            foreach (var pair in _lines)
            {
                var hold = pair.Value;
                if (hold.Misses == 0)
                    continue;
                if (hold.Misses > worstMiss || (hold.Misses == worstMiss && hold.Stamp < oldest))
                {
                    worstMiss = hold.Misses;
                    oldest = hold.Stamp;
                    drop = pair.Key;
                }
            }

            if (drop is null)
                break;
            _lines.Remove(drop);
        }
    }

    private static List<string> Normalize(IEnumerable<string>? lines)
    {
        var list = new List<string>();
        if (lines is null)
            return list;
        foreach (var raw in lines)
        {
            var collapsed = DamageLineParser.Collapse(raw);
            if (collapsed.Length > 0)
                list.Add(collapsed);
        }

        return list;
    }
}
