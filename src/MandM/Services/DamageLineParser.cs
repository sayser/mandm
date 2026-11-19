using System.Text.RegularExpressions;

namespace MandM.Services;

/// <summary>One personal hit. Other players' damage is not represented.</summary>
public readonly record struct ParsedDamage(bool Outgoing, int Amount, string Who, string Detail);

/// <summary>
/// Personal combat lines only: damage you deal, and damage dealt to you.
/// Party hits ("Bob slashes a rat…") are ignored.
/// </summary>
public static class DamageLineParser
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex Timestamp = new(
        @"^(?:\[\s*)?\d{1,2}:\d{2}(?::\d{2})?(?:\s*[AaPp][Mm])?\s*\]?\s*",
        RegexOptions.Compiled);
    private static readonly Regex FromYour = new(
        @"^(?<target>.+?)\s+(?:has|have)\s+taken\s+(?<amount>\d+)\s+(?:points?\s+(?:of\s+)?)?(?:(?<dtype>[A-Za-z][\w-]*)\s+)?damage\s+from\s+your\s+(?<ability>.+?)\.?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex YouTaken = new(
        @"^You\s+(?:have\s+|has\s+|'ve\s+)?taken\s+(?<amount>\d+)\s+(?:points?\s+(?:of\s+)?)?(?:(?<dtype>[A-Za-z][\w-]*)\s+)?damage(?:\s+from\s+(?<source>.+?))?\.?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Points = new(
        @"\bfor\s+(?<amount>\d+)\s+points?\s+(?:of\s+)?(?<dtype>[A-Za-z][\w-]*\s+)?damage\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex YouWere = new(
        @"^You\s+(?:were|was|are|get|got)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex YourMelee = new(
        @"^You\s+(?<verb>[A-Za-z]+)\s+(?<target>.+?)(?:\s+with\s+your\s+(?<via>.+))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex YourSpell = new(
        @"^Your\s+(?<ability>.+?)\s+hits?\s+(?<target>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Incoming = new(
        @"^(?<source>.+?)\s+(?<verb>[A-Za-z]+)\s+YOU$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EndsYou = new(@"\bYOU$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex YouSlain = new(
        @"^You\s+(?:have\s+)?slain\s+(?<target>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AnyoneSlain = new(
        @"^(?:Your\s+party\s+member\s+(?<member>.+?)\s+(?:has\s+)?slain\s+|(?<slayer>.+?)\s+has\s+slain\s+)(?<target>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Collapse(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? string.Empty : Whitespace.Replace(raw.Trim(), " ");

    /// <summary>
    /// Windows OCR wraps one combat sentence across visual lines. Join a fragment
    /// with the following line when neither is a hit on its own and together they are.
    /// </summary>
    public static List<string> JoinWrapped(IReadOnlyList<string> lines)
    {
        var result = new List<string>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var combined = Collapse(lines[i]);
            if (combined.Length == 0)
                continue;
            if (IsCompleteEvent(combined))
            {
                result.Add(combined);
                continue;
            }

            var acc = combined;
            var consumed = 0;
            for (var extra = 1; extra <= 2 && i + extra < lines.Count; extra++)
            {
                var next = Collapse(lines[i + extra]);
                if (next.Length == 0 || IsCompleteEvent(next))
                    break;
                acc = acc + " " + next;
                consumed = extra;
                if (IsCompleteEvent(acc))
                    break;
            }

            if (consumed > 0 && IsCompleteEvent(acc))
            {
                result.Add(acc);
                i += consumed;
            }
            else
            {
                result.Add(combined);
            }
        }

        return result;
    }

    private static bool IsCompleteEvent(string line) =>
        IsSlainLine(line) || TryParse(line, out _);

    /// <summary>Letters, digits, and single spaces. Timestamps and punctuation are removed.</summary>
    public static string Canonical(string? raw)
    {
        var message = StripTimestamp(Collapse(raw)).ToLowerInvariant();
        var chars = new System.Text.StringBuilder(message.Length);
        var pendingSpace = false;
        foreach (var ch in message)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSpace && chars.Length > 0)
                    chars.Append(' ');
                chars.Append(ch);
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return chars.ToString();
    }

    /// <summary>
    /// Any slain line ends a fight: "You have slain …", a party member's kill,
    /// or another "has slain" line. A missing "have" still counts.
    /// </summary>
    public static bool IsSlainLine(string? raw)
    {
        var text = Canonical(raw);
        return Regex.IsMatch(text, @"\bslain\b");
    }

    /// <summary>Who died. "You have slain a crystal dervish!" yields the mob name.</summary>
    public static bool TryParseSlain(string? raw, out string target) =>
        TryParseSlain(raw, out _, out target);

    /// <summary>
    /// Who got the kill and who died. A party line names the member, not you.
    /// The line is never damage.
    /// </summary>
    public static bool TryParseSlain(string? raw, out string slayer, out string target)
    {
        slayer = string.Empty;
        target = string.Empty;
        if (!IsSlainLine(raw))
            return false;

        var message = StripTimestamp(Collapse(raw)).Trim().TrimEnd('.', '!', '?', ' ');
        if (message.Length == 0)
            return false;

        var you = YouSlain.Match(message);
        if (you.Success)
        {
            slayer = "You";
            target = Clean(you.Groups["target"].Value);
            return target.Length > 0;
        }

        var other = AnyoneSlain.Match(message);
        if (other.Success)
        {
            var member = other.Groups["member"].Value;
            slayer = Clean(member.Length > 0 ? member : other.Groups["slayer"].Value);
            target = Clean(other.Groups["target"].Value);
            return target.Length > 0;
        }

        var canonical = Canonical(raw);
        var index = canonical.LastIndexOf("slain ", StringComparison.Ordinal);
        if (index >= 0)
            target = canonical[(index + "slain ".Length)..].Trim();
        return true;
    }

    public static bool TryParse(string? raw, out ParsedDamage parsed)
    {
        parsed = default;
        if (IsSlainLine(raw))
            return false;
        var message = StripTimestamp(Collapse(raw));
        if (message.Length == 0)
            return false;

        var fromYour = FromYour.Match(message);
        if (fromYour.Success && TryAmount(fromYour, out var dotAmount))
        {
            parsed = new ParsedDamage(
                true,
                dotAmount,
                Clean(fromYour.Groups["target"].Value),
                Clean(fromYour.Groups["ability"].Value));
            return true;
        }

        var taken = YouTaken.Match(message);
        if (taken.Success && TryAmount(taken, out var takenAmount))
        {
            var source = taken.Groups["source"].Success
                ? Clean(taken.Groups["source"].Value)
                : "You";
            parsed = new ParsedDamage(false, takenAmount, source, "taken");
            return true;
        }

        var points = Points.Match(message);
        if (!points.Success || !TryAmount(points, out var amount))
            return false;

        var before = message[..points.Index].Trim();
        var dtype = points.Groups["dtype"].Success ? Clean(points.Groups["dtype"].Value) : string.Empty;

        if (StartsWithWord(message, "Your"))
        {
            var (who, ability) = SplitYourSpell(before);
            parsed = new ParsedDamage(true, amount, who, Prefer(ability, dtype));
            return true;
        }

        if (StartsWithWord(message, "You"))
        {
            if (YouWere.IsMatch(message))
            {
                parsed = new ParsedDamage(false, amount, "You", Prefer(dtype, "hit"));
                return true;
            }

            var (who, detail) = SplitYourMelee(before);
            parsed = new ParsedDamage(true, amount, who, Prefer(detail, dtype));
            return true;
        }

        if (EndsYou.IsMatch(before))
        {
            var (who, detail) = SplitIncoming(before);
            parsed = new ParsedDamage(false, amount, who, Prefer(dtype, detail));
            return true;
        }

        return false;
    }

    private static string StripTimestamp(string line) => Timestamp.Replace(line, string.Empty).Trim();

    private static bool TryAmount(Match match, out int amount)
    {
        amount = 0;
        return int.TryParse(match.Groups["amount"].Value, out amount) && amount > 0;
    }

    private static bool StartsWithWord(string message, string word) =>
        message.StartsWith(word + " ", StringComparison.OrdinalIgnoreCase)
        || string.Equals(message, word, StringComparison.OrdinalIgnoreCase);

    private static (string Who, string Detail) SplitYourMelee(string before)
    {
        var match = YourMelee.Match(before);
        if (!match.Success)
            return (Clean(before), string.Empty);

        var via = match.Groups["via"].Success ? Clean(match.Groups["via"].Value) : string.Empty;
        var verb = Clean(match.Groups["verb"].Value);
        return (Clean(match.Groups["target"].Value), string.IsNullOrEmpty(via) ? verb : via);
    }

    private static (string Who, string Ability) SplitYourSpell(string before)
    {
        var match = YourSpell.Match(before);
        if (!match.Success)
            return (Clean(before), string.Empty);
        return (Clean(match.Groups["target"].Value), Clean(match.Groups["ability"].Value));
    }

    private static (string Who, string Detail) SplitIncoming(string before)
    {
        var match = Incoming.Match(before);
        if (!match.Success)
        {
            var who = EndsYou.Replace(before, string.Empty).Trim();
            return (Clean(who), string.Empty);
        }

        return (Clean(match.Groups["source"].Value), Clean(match.Groups["verb"].Value));
    }

    private static string Prefer(string primary, string fallback) =>
        string.IsNullOrWhiteSpace(primary) ? fallback : primary;

    private static string Clean(string value)
    {
        var text = value.Trim().TrimEnd('.', '!', ',');
        return Whitespace.Replace(text, " ").Trim();
    }
}
