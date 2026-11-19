using System.Reflection;
using System.Runtime.InteropServices;
using MandM.Models;

namespace MandM.Services;

/// <summary>
/// Windows SAPI text-to-speech (same approach as EQDM's built-in voice engine).
/// </summary>
public static class WindowsTts
{
    private static readonly object Gate = new();
    private static Type? _voiceType;
    private static object? _voice;
    private static string _boundVoiceId = "\0"; // sentinel = unset

    // SVSFlagsAsync — do not block the worker for the full utterance.
    private const int SpeakAsyncFlag = 1;

    public static IReadOnlyList<VoiceOption> ListVoices()
    {
        object? probe = null;
        try
        {
            var voiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (voiceType is null) return [new VoiceOption("", "Default voice")];
            probe = Activator.CreateInstance(voiceType);
            if (probe is null) return [new VoiceOption("", "Default voice")];
            var tokens = voiceType.InvokeMember("GetVoices", BindingFlags.InvokeMethod, null, probe, []);
            if (tokens is null) return [new VoiceOption("", "Default voice")];
            var tokenType = tokens.GetType();
            var count = Convert.ToInt32(tokenType.InvokeMember("Count", BindingFlags.GetProperty, null, tokens, []));
            var list = new List<VoiceOption> { new("", "Default voice") };
            for (var i = 0; i < count; i++)
            {
                var token = tokenType.InvokeMember("Item", BindingFlags.InvokeMethod, null, tokens, [i]);
                if (token is null) continue;
                var description = token.GetType()
                    .InvokeMember("GetDescription", BindingFlags.InvokeMethod, null, token, [])
                    ?.ToString();
                if (string.IsNullOrWhiteSpace(description)) continue;
                list.Add(new VoiceOption(description, description));
            }

            return list;
        }
        catch (COMException)
        {
            return [new VoiceOption("", "Default voice")];
        }
        catch (TargetInvocationException)
        {
            return [new VoiceOption("", "Default voice")];
        }
        finally
        {
            if (probe is not null && Marshal.IsComObject(probe))
                Marshal.FinalReleaseComObject(probe);
        }
    }

    public static bool Speak(string text, string voiceId, int volumePercent)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        lock (Gate)
        {
            try
            {
                if (!EnsureVoice())
                    return false;

                var id = voiceId?.Trim() ?? string.Empty;
                if (!string.Equals(_boundVoiceId, id, StringComparison.Ordinal))
                {
                    if (!string.IsNullOrWhiteSpace(id))
                        TrySetVoice(_voiceType!, _voice!, id);
                    _boundVoiceId = id;
                }

                _voiceType!.InvokeMember("Volume", BindingFlags.SetProperty, null, _voice,
                    [Math.Clamp(volumePercent, 0, 100)]);
                // Purge pending speech so a new alert replaces a long previous one.
                try
                {
                    _voiceType.InvokeMember("Speak", BindingFlags.InvokeMethod, null, _voice,
                        [string.Empty, 2]); // SVSFPurgeBeforeSpeak
                }
                catch
                {
                    // optional
                }

                _voiceType.InvokeMember("Speak", BindingFlags.InvokeMethod, null, _voice,
                    [text, SpeakAsyncFlag]);
                return true;
            }
            catch (COMException)
            {
                ResetVoice();
                return false;
            }
            catch (TargetInvocationException)
            {
                ResetVoice();
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (MissingMethodException)
            {
                return false;
            }
        }
    }

    private static bool EnsureVoice()
    {
        if (_voice is not null && _voiceType is not null)
            return true;
        _voiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
        if (_voiceType is null) return false;
        _voice = Activator.CreateInstance(_voiceType);
        _boundVoiceId = "\0";
        return _voice is not null;
    }

    private static void ResetVoice()
    {
        if (_voice is not null && Marshal.IsComObject(_voice))
        {
            try { Marshal.FinalReleaseComObject(_voice); } catch { /* ignore */ }
        }

        _voice = null;
        _voiceType = null;
        _boundVoiceId = "\0";
    }

    private static void TrySetVoice(Type voiceType, object voice, string voiceId)
    {
        var tokens = voiceType.InvokeMember("GetVoices", BindingFlags.InvokeMethod, null, voice, []);
        if (tokens is null) return;
        var tokenType = tokens.GetType();
        var count = Convert.ToInt32(tokenType.InvokeMember("Count", BindingFlags.GetProperty, null, tokens, []));
        for (var i = 0; i < count; i++)
        {
            var token = tokenType.InvokeMember("Item", BindingFlags.InvokeMethod, null, tokens, [i]);
            if (token is null) continue;
            var description = token.GetType()
                .InvokeMember("GetDescription", BindingFlags.InvokeMethod, null, token, [])
                ?.ToString();
            if (!string.Equals(description, voiceId, StringComparison.OrdinalIgnoreCase)) continue;
            voiceType.InvokeMember("Voice", BindingFlags.SetProperty, null, voice, [token]);
            return;
        }
    }
}
