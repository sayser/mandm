using System.IO;
using System.Media;
using System.Windows.Media;

namespace MandM.Services;

public sealed class AlarmService
{
    private MediaPlayer? _player;

    public static IReadOnlyList<string> BuiltInSounds { get; } =
    [
        "Beep",
        "Asterisk",
        "Exclamation",
        "Hand",
        "Question"
    ];

    public void Play(string? soundIdOrPath)
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
                _player?.Stop();
                _player ??= new MediaPlayer();
                _player.Open(new Uri(soundIdOrPath));
                _player.Volume = 1.0;
                _player.Play();
                return;
            }
            catch
            {
                // fall through to system sound
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
