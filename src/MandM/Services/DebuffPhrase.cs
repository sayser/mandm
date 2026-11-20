using System.Text.RegularExpressions;

namespace MandM.Services;

/// <summary>
/// Debuff land and fade text is a phrase inside a combat line, not the whole line.
/// The words in front of the phrase are the monster name.
/// </summary>
public static class DebuffPhrase
{
    public static bool TryRead(string? line, string? phrase, IEnumerable<string>? longerPeers, out string monster)
    {
        monster = string.Empty;
        if (!Owns(line, phrase, longerPeers))
            return false;

        var haystack = TextMatch.Normalize(line!);
        var needle = TextMatch.Normalize(phrase!);
        var match = Regex.Match(
            haystack,
            $@"(?<!\w){Regex.Escape(needle)}(?!\w)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            return false;

        monster = haystack[..match.Index].Trim();
        return true;
    }

    /// <summary>
    /// Word-boundary phrase match. A longer different phrase that also matches the line wins,
    /// so a short land phrase does not claim a fade line that only shares part of the text.
    /// </summary>
    public static bool Owns(string? line, string? phrase, IEnumerable<string>? longerPeers)
    {
        if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(phrase))
            return false;
        if (!TextMatch.ContainsLoose(line, phrase))
            return false;

        var needleNorm = TextMatch.Normalize(phrase);
        if (longerPeers is null)
            return true;

        foreach (var other in longerPeers)
        {
            if (string.IsNullOrWhiteSpace(other))
                continue;
            if (string.Equals(other, phrase, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!TextMatch.ContainsLoose(line, other))
                continue;

            var otherNorm = TextMatch.Normalize(other);
            if (otherNorm.Length > needleNorm.Length)
                return false;
            if (otherNorm.Length < needleNorm.Length && TextMatch.ContainsLoose(other, phrase))
                return false;
        }

        return true;
    }

    /// <summary>Compare key. Empty names are not a monster.</summary>
    public static string Key(string? name)
    {
        var text = TextMatch.Normalize(name ?? string.Empty).ToLowerInvariant();
        foreach (var article in new[] { "a ", "an ", "the " })
        {
            if (!text.StartsWith(article, StringComparison.Ordinal))
                continue;
            text = text[article.Length..].Trim();
            break;
        }

        return text;
    }

    public static bool SameMonster(string? tracked, string? named)
    {
        var left = Key(tracked);
        var right = Key(named);
        return left.Length > 0 && left == right;
    }

    /// <summary>Same debuff slot: one unnamed row, or one row per monster.</summary>
    public static bool SameSlot(string? tracked, string? named) => Key(tracked) == Key(named);

    /// <summary>
    /// Lines whose canonical text was not in <paramref name="previous"/>.
    /// <paramref name="previous"/> becomes the lines still visible.
    /// </summary>
    public static List<string> FreshLines(HashSet<string> previous, IEnumerable<string> visible)
    {
        var current = new HashSet<string>(StringComparer.Ordinal);
        var fresh = new List<string>();
        foreach (var line in visible)
        {
            var key = DamageLineParser.Canonical(line);
            if (key.Length == 0 || !current.Add(key))
                continue;
            if (!previous.Contains(key))
                fresh.Add(line);
        }

        previous.Clear();
        previous.UnionWith(current);
        return fresh;
    }
}
