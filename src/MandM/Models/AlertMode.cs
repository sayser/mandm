namespace MandM.Models;

public enum AlertMode
{
    Sound = 0,
    Voice = 1
}

public static class AlertModeOptions
{
    public static IReadOnlyList<AlertMode> Choices { get; } = [AlertMode.Sound, AlertMode.Voice];

    public static AlertMode Normalize(AlertMode mode) =>
        mode == AlertMode.Voice ? AlertMode.Voice : AlertMode.Sound;

    public static string Label(AlertMode mode) =>
        mode == AlertMode.Voice ? "Voice" : "Sound";
}
