namespace MandM.Models;

public sealed class BuffDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    /// <summary>Full buff duration in seconds.</summary>
    public int DurationSeconds { get; set; }
    /// <summary>Warn when remaining seconds ≤ this value (used when WarnText is empty).</summary>
    public int WarnSeconds { get; set; } = 60;
    public string CastText { get; set; } = string.Empty;
    public string LandText { get; set; } = string.Empty;
    public string FadeText { get; set; } = string.Empty;
    /// <summary>Optional OCR phrase that fires the alarm immediately (e.g. invis drop warning).</summary>
    public string WarnText { get; set; } = string.Empty;

    /// <summary>Named capture region ids to watch for each match line.</summary>
    public string CastRegionId { get; set; } = string.Empty;
    public string LandRegionId { get; set; } = string.Empty;
    public string FadeRegionId { get; set; } = string.Empty;
    public string WarnRegionId { get; set; } = string.Empty;

    public AlertMode AlertMode { get; set; } = AlertMode.Sound;
    /// <summary>Built-in sound id or absolute .wav path (Sound mode).</summary>
    public string AlarmSound { get; set; } = "Exclamation";
    /// <summary>Spoken warn alert text (Voice mode). Empty → "{Name} warning".</summary>
    public string WarnVoiceText { get; set; } = string.Empty;
    /// <summary>Spoken fade alert text (Voice mode). Empty → "{Name} faded".</summary>
    public string FadeVoiceText { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string DurationDisplay =>
        DurationSeconds >= 60
            ? $"{DurationSeconds / 60.0:0.#}m"
            : $"{DurationSeconds}s";

    public string WarnDisplay =>
        string.IsNullOrWhiteSpace(WarnText)
            ? $"{WarnSeconds}s"
            : "text";

    public string AlertDisplay =>
        AlertModeOptions.Normalize(AlertMode) == AlertMode.Voice ? "Voice" : AlarmSound;

    public string StatusDisplay => Enabled ? "Enabled" : "Disabled";

    public BuffDefinition Clone() => new()
    {
        Id = Id,
        Name = Name,
        DurationSeconds = DurationSeconds,
        WarnSeconds = WarnSeconds,
        CastText = CastText,
        LandText = LandText,
        FadeText = FadeText,
        WarnText = WarnText,
        CastRegionId = CastRegionId,
        LandRegionId = LandRegionId,
        FadeRegionId = FadeRegionId,
        WarnRegionId = WarnRegionId,
        AlertMode = AlertMode,
        AlarmSound = AlarmSound,
        WarnVoiceText = WarnVoiceText,
        FadeVoiceText = FadeVoiceText,
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
        WarnText = string.Empty,
        AlertMode = AlertMode.Sound,
        AlarmSound = "Exclamation",
        Enabled = true
    };
}
