using System.Text.RegularExpressions;

namespace MandM.Services;

public static partial class TextMatch
{
    /// <summary>
    /// Case-insensitive phrase contains with light punctuation tolerance.
    /// Uses word boundaries so "You appear" does not match inside
    /// "You appearing" / "You disappear" / "yourself appearing".
    /// </summary>
    public static bool ContainsLoose(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(needle)) return false;
        if (string.IsNullOrWhiteSpace(haystack)) return false;

        var h = Normalize(haystack);
        var n = Normalize(needle);
        if (n.Length == 0) return false;

        // Whole-phrase word boundaries: short fade/warn needles must not hitchhike
        // inside longer related combat lines.
        var pattern = $@"(?<!\w){Regex.Escape(n)}(?!\w)";
        return Regex.IsMatch(h, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static string Normalize(string value)
    {
        var collapsed = WhitespaceRegex().Replace(value.Trim(), " ");
        return PunctuationRegex().Replace(collapsed, string.Empty);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[^\w\s]")]
    private static partial Regex PunctuationRegex();
}
