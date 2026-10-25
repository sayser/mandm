namespace MandM.Models;

public sealed class BuffDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    /// <summary>Full buff duration in seconds.</summary>
    public int DurationSeconds { get; set; }
    /// <summary>Warn when remaining seconds ≤ this value.</summary>
    public int WarnSeconds { get; set; } = 60;
    public string CastText { get; set; } = string.Empty;
    public string LandText { get; set; } = string.Empty;
    public string FadeText { get; set; } = string.Empty;
    /// <summary>Built-in sound id (Beep, Asterisk, Exclamation, Hand, Question) or absolute .wav path.</summary>
    public string AlarmSound { get; set; } = "Exclamation";
    public bool Enabled { get; set; } = true;

    public BuffDefinition Clone() => new()
    {
        Id = Id,
        Name = Name,
        DurationSeconds = DurationSeconds,
        WarnSeconds = WarnSeconds,
        CastText = CastText,
        LandText = LandText,
        FadeText = FadeText,
        AlarmSound = AlarmSound,
        Enabled = Enabled
    };

    public static BuffDefinition CreateSpeedOfThePackSeed() => new()
    {
        Name = "Speed of the Pack",
        DurationSeconds = 35 * 60,
        WarnSeconds = 60,
        CastText = "You begin casting Speed of the Pack",
        LandText = "You feel the speed of the pack within you",
        FadeText = "Your bestial speed fades",
        AlarmSound = "Exclamation",
        Enabled = true
    };
}
