namespace MandM.Models;

/// <summary>Global spoken-alert settings (EQDM-style Windows voice picker).</summary>
public sealed class VoiceSettings
{
    /// <summary>SAPI voice description, or empty for the system default.</summary>
    public string WindowsVoiceId { get; set; } = string.Empty;
    public int VolumePercent { get; set; } = 100;

    public VoiceSettings Clone() => new()
    {
        WindowsVoiceId = WindowsVoiceId ?? string.Empty,
        VolumePercent = Math.Clamp(VolumePercent <= 0 ? 100 : VolumePercent, 0, 100)
    };
}

public sealed record VoiceOption(string Id, string Label)
{
    public override string ToString() => Label;
}
