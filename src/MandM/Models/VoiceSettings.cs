namespace MandM.Models;

public enum VoiceEngineKind
{
    Windows = 0,
    Natural = 1
}

/// <summary>Global spoken-alert settings — same shape as EQDM VoiceSettings.</summary>
public sealed class VoiceSettings
{
    public VoiceEngineKind Engine { get; set; } = VoiceEngineKind.Natural;
    /// <summary>SAPI voice description, or empty for the system default.</summary>
    public string WindowsVoiceId { get; set; } = string.Empty;
    /// <summary>Kokoro voice id (e.g. af_heart).</summary>
    public string NaturalVoiceId { get; set; } = "af_heart";
    public int VolumePercent { get; set; } = 100;

    public VoiceSettings Clone() => new()
    {
        Engine = Engine,
        WindowsVoiceId = WindowsVoiceId ?? string.Empty,
        NaturalVoiceId = string.IsNullOrWhiteSpace(NaturalVoiceId) ? "af_heart" : NaturalVoiceId,
        VolumePercent = Math.Clamp(VolumePercent <= 0 ? 100 : VolumePercent, 0, 100)
    };
}

public sealed record VoiceOption(string Id, string Label)
{
    public override string ToString() => Label;
}
