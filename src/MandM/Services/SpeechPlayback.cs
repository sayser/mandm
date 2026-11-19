using MandM.Models;

namespace MandM.Services;

/// <summary>EQDM-style speech router: Natural (Kokoro) or Windows SAPI.</summary>
public static class SpeechPlayback
{
    public static VoiceSettings Settings { get; set; } = new();

    public static bool Speak(string text, int volumePercent)
    {
        if (string.IsNullOrWhiteSpace(text) || volumePercent <= 0) return false;
        if (Settings.Engine == VoiceEngineKind.Natural && NaturalTts.IsDownloaded)
            return NaturalTts.Speak(text, Settings.NaturalVoiceId, volumePercent);
        return WindowsTts.Speak(text, Settings.WindowsVoiceId, volumePercent);
    }

    public static bool Preview()
    {
        var name = Settings.Engine == VoiceEngineKind.Natural
            ? NaturalTts.Voices.FirstOrDefault(voice =>
                  voice.Id.Equals(Settings.NaturalVoiceId, StringComparison.OrdinalIgnoreCase))?.Label
              ?? "the natural voice"
            : string.IsNullOrWhiteSpace(Settings.WindowsVoiceId)
                ? "the default Windows voice"
                : Settings.WindowsVoiceId;
        return Speak($"This is {name}.", Math.Clamp(Settings.VolumePercent <= 0 ? 100 : Settings.VolumePercent, 0, 100));
    }
}
