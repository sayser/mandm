using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Media;
using MandM.Models;

namespace MandM.Services;

public sealed class AlarmService
{
    private MediaPlayer? _player;
    private VoiceSettings _voice = new();

    public static IReadOnlyList<string> BuiltInSounds { get; } =
    [
        "Beep",
        "Asterisk",
        "Exclamation",
        "Hand",
        "Question"
    ];

    public void UpdateVoiceSettings(VoiceSettings settings)
    {
        _voice = settings?.Clone() ?? new VoiceSettings();
        SpeechPlayback.Settings = _voice.Clone();
    }

    public VoiceSettings CurrentVoiceSettings => _voice.Clone();

    /// <summary>Legacy sound-only helper (built-in / wav path).</summary>
    public void Play(string? soundIdOrPath) =>
        _ = Task.Run(() => PlaySoundCore(soundIdOrPath));

    public void PlayAlert(AlertMode mode, string? soundIdOrPath, string? voiceText, string defaultPhrase)
    {
        var volume = Math.Clamp(_voice.VolumePercent <= 0 ? 100 : _voice.VolumePercent, 0, 100);
        if (volume <= 0) return;

        var normalized = AlertModeOptions.Normalize(mode);
        _ = Task.Run(() =>
        {
            try
            {
                if (normalized == AlertMode.Voice)
                {
                    var spoken = string.IsNullOrWhiteSpace(voiceText)
                        ? defaultPhrase
                        : voiceText.Trim();
                    SpeechPlayback.Settings = _voice.Clone();
                    if (SpeechPlayback.Speak(spoken, volume))
                        return;
                    // Fall back: try the other engine, then sound.
                    if (_voice.Engine == VoiceEngineKind.Natural)
                    {
                        if (WindowsTts.Speak(spoken, _voice.WindowsVoiceId, volume))
                            return;
                    }
                    else if (NaturalTts.IsDownloaded
                             && NaturalTts.Speak(spoken, _voice.NaturalVoiceId, volume))
                    {
                        return;
                    }
                }

                PlaySoundCore(soundIdOrPath);
            }
            catch
            {
                try { PlaySoundCore(soundIdOrPath); } catch { /* best-effort */ }
            }
        });
    }

    public void PreviewVoice(string? sampleText = null)
    {
        SpeechPlayback.Settings = _voice.Clone();
        if (!string.IsNullOrWhiteSpace(sampleText))
        {
            PlayAlert(AlertMode.Voice, "Exclamation", sampleText, sampleText);
            return;
        }

        _ = Task.Run(() =>
        {
            if (!SpeechPlayback.Preview())
                PlaySoundCore("Exclamation");
        });
    }

    private void PlaySoundCore(string? soundIdOrPath)
    {
        if (string.IsNullOrWhiteSpace(soundIdOrPath))
        {
            SystemSounds.Exclamation.Play();
            return;
        }

        if (File.Exists(soundIdOrPath))
        {
            try
            {
                using var player = new SoundPlayer(soundIdOrPath);
                player.PlaySync();
                return;
            }
            catch
            {
                try
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        _player?.Stop();
                        _player ??= new MediaPlayer();
                        _player.Open(new Uri(soundIdOrPath));
                        _player.Volume = 1.0;
                        _player.Play();
                    });
                    return;
                }
                catch
                {
                    // fall through
                }
            }
        }

        switch (soundIdOrPath.Trim())
        {
            case "Beep":
                SystemSounds.Beep.Play();
                break;
            case "Asterisk":
                SystemSounds.Asterisk.Play();
                break;
            case "Hand":
                SystemSounds.Hand.Play();
                break;
            case "Question":
                SystemSounds.Question.Play();
                break;
            default:
                SystemSounds.Exclamation.Play();
                break;
        }
    }
}
