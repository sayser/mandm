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
    /// Phrases on <paramref name="line"/> that are not beaten by a longer phrase.
    /// When one line matches more than one spell, the longer phrase wins.
    /// </summary>
    public static List<string> WinningPhrases(string? line, IEnumerable<string> phrases)
    {
        var list = new List<string>();
        foreach (var phrase in phrases)
        {
            if (!string.IsNullOrWhiteSpace(phrase))
                list.Add(phrase);
        }

        var winners = new List<string>();
        foreach (var phrase in list)
        {
            if (Owns(line, phrase, list))
                winners.Add(phrase);
        }

        return winners;
    }

    /// <summary>
    /// Word-boundary phrase match. A longer different phrase that also matches the line wins,
    /// so a short name does not claim a longer spell on the same line.
    /// "Invisibility" does not own "Invisibility versus Undead."
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
        if (left.Length == 0 || right.Length == 0)
            return false;
        if (left == right)
            return true;

        // "a bloated plague rat" and "the bloated plague rat" already share a key.
        // A slightly longer OCR name still names that monster. A one-word scrap does not.
        var shorter = left.Length <= right.Length ? left : right;
        var longer = left.Length <= right.Length ? right : left;
        if (shorter.Length < 12 || !shorter.Contains(' '))
            return false;
        return ContainsPhrase(longer, shorter);
    }

    /// <summary>
    /// Which tracked debuffs a slain line removes. The fade sound never plays.
    /// A name that matches none of them removes every tracked debuff.
    /// </summary>
    public readonly record struct SlainClear(bool ClearAll, string Monster)
    {
        public bool PlayFade => false;
    }

    public static bool TrySlainClear(IReadOnlyList<string> trackedMonsters, string? slainLine, out SlainClear clear)
    {
        clear = default;
        if (!DamageLineParser.IsSlainLine(slainLine))
            return false;

        DamageLineParser.TryParseSlain(slainLine, out _, out var target);
        foreach (var name in trackedMonsters)
        {
            if (!SameMonster(name, target))
                continue;
            clear = new SlainClear(false, target);
            return true;
        }

        clear = new SlainClear(true, target);
        return true;
    }

    private static bool ContainsPhrase(string longer, string shorter)
    {
        var start = 0;
        while (start <= longer.Length - shorter.Length)
        {
            var at = longer.IndexOf(shorter, start, StringComparison.Ordinal);
            if (at < 0)
                return false;
            var before = at == 0 || !char.IsLetterOrDigit(longer[at - 1]);
            var end = at + shorter.Length;
            var after = end == longer.Length || !char.IsLetterOrDigit(longer[end]);
            if (before && after)
                return true;
            start = at + 1;
        }

        return false;
    }

    /// <summary>Same debuff slot: one unnamed row, or one row per monster.</summary>
    public static bool SameSlot(string? tracked, string? named) => Key(tracked) == Key(named);

    public readonly record struct DebuffSight(string Line, string Monster);

    /// <summary>
    /// Stable identity for one visible land or fade. Punctuation and a leading article
    /// do not make a new sight. Text after the phrase does not either.
    /// </summary>
    public static string SightKey(string? phrase, string? monster)
    {
        var phraseKey = DamageLineParser.Canonical(phrase);
        if (phraseKey.Length == 0)
            return string.Empty;
        var monsterKey = Key(monster);
        return monsterKey.Length == 0 ? phraseKey : phraseKey + " " + monsterKey;
    }

    /// <summary>
    /// Sights that were not already remembered.
    /// <paramref name="remembered"/> maps a sight key to how many successful reads in a row
    /// it has been missing. An empty or failed capture does not change it.
    /// A sight drops only after it has been absent for more than one successful read.
    /// Focus loss does not belong here.
    /// </summary>
    public static List<DebuffSight> FreshSights(
        Dictionary<string, int> remembered,
        IEnumerable<DebuffSight> visible,
        string? phrase,
        bool emptyCapture = false)
    {
        if (emptyCapture)
            return [];

        var current = new HashSet<string>(StringComparer.Ordinal);
        var fresh = new List<DebuffSight>();
        foreach (var sight in visible)
        {
            var key = SightKey(phrase, sight.Monster);
            if (key.Length == 0 || !current.Add(key))
                continue;
            if (!remembered.ContainsKey(key))
                fresh.Add(sight);
            remembered[key] = 0;
        }

        foreach (var key in remembered.Keys.Where(key => !current.Contains(key)).ToList())
        {
            var misses = remembered[key] + 1;
            if (misses > 1)
                remembered.Remove(key);
            else
                remembered[key] = misses;
        }

        return fresh;
    }

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
