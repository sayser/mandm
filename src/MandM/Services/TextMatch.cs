using System.Text.RegularExpressions;

namespace MandM.Services;

public static partial class TextMatch
{
    /// <summary>
    /// Case-insensitive contains match with light punctuation tolerance
    /// (ignore trailing periods / extra spaces on either side).
    /// </summary>
    public static bool ContainsLoose(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(needle)) return false;
        if (string.IsNullOrWhiteSpace(haystack)) return false;

        var h = Normalize(haystack);
        var n = Normalize(needle);
        return h.Contains(n, StringComparison.OrdinalIgnoreCase);
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
