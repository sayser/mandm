namespace MandM.Models;

public sealed class UserTimerDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Timer";
    /// <summary>Countdown length in seconds.</summary>
    public int DurationSeconds { get; set; } = 300;

    public AlertMode AlertMode { get; set; } = AlertMode.Sound;
    public string AlarmSound { get; set; } = "Exclamation";
    /// <summary>Spoken text when the timer hits zero (Voice mode). Empty → "{Name} finished".</summary>
    public string VoiceText { get; set; } = string.Empty;

    public string DurationDisplay =>
        DurationSeconds >= 3600
            ? $"{DurationSeconds / 3600.0:0.#}h"
            : DurationSeconds >= 60
                ? $"{DurationSeconds / 60.0:0.#}m"
                : $"{DurationSeconds}s";

    public UserTimerDefinition Clone() => new()
    {
        Id = Id,
        Name = Name,
        DurationSeconds = DurationSeconds,
        AlertMode = AlertMode,
        AlarmSound = AlarmSound,
        VoiceText = VoiceText
    };
}
