using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MandM.Models;

namespace MandM.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string SettingsPath { get; }

    public SettingsService()
    {
        SettingsPath = ResolveStableSettingsPath();
    }

    public AppSettings Load()
    {
        try
        {
            // Prefer stable path; otherwise migrate from single-file extract temp / legacy locations.
            var source = FindBestSettingsFile(SettingsPath);
            if (source is null)
            {
                var defaults = AppSettings.CreateDefault();
                Save(defaults);
                return defaults;
            }

            var json = File.ReadAllText(source);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings is null)
                return AppSettings.CreateDefault();

            settings.Buffs ??= [];
            settings.UserTimers ??= [];
            settings.CaptureRegions ??= [];
            settings.MouseHighlight ??= new MouseHighlightSettings();
            settings.Voice ??= new VoiceSettings();
            MigrateAndNormalize(settings);

            // Always persist to the stable location (and out of Temp\.net\…).
            if (!PathsEqual(source, SettingsPath))
                Save(settings);

            return settings;
        }
        catch
        {
            return AppSettings.CreateDefault();
        }
    }

    public void Save(AppSettings settings)
    {
        MigrateAndNormalize(settings);

        var dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    /// <summary>
    /// Fold legacy Abilities / Combat Text rectangles into <see cref="AppSettings.CaptureRegions"/>
    /// and ensure every buff match line has a region id.
    /// </summary>
    public static void MigrateAndNormalize(AppSettings settings)
    {
        settings.CaptureRegions ??= [];
        settings.Buffs ??= [];
        settings.UserTimers ??= [];
        settings.MouseHighlight ??= new MouseHighlightSettings();
        settings.Voice ??= new VoiceSettings();
        settings.Voice.WindowsVoiceId ??= string.Empty;
        settings.Voice.NaturalVoiceId = string.IsNullOrWhiteSpace(settings.Voice.NaturalVoiceId)
            ? "af_heart"
            : settings.Voice.NaturalVoiceId.Trim();
        if (settings.Voice.Engine is not VoiceEngineKind.Windows and not VoiceEngineKind.Natural)
            settings.Voice.Engine = VoiceEngineKind.Natural;
        settings.Voice.VolumePercent = Math.Clamp(
            settings.Voice.VolumePercent <= 0 ? 100 : settings.Voice.VolumePercent, 0, 100);

        if (settings.CaptureRegions.Count == 0)
        {
            settings.CaptureRegions.Add(new NamedCaptureRegion
            {
                Name = "Abilities",
                Region = settings.AbilitiesRegion?.Clone() ?? new CaptureRegion()
            });
            settings.CaptureRegions.Add(new NamedCaptureRegion
            {
                Name = "Combat Text",
                Region = settings.CombatTextRegion?.Clone() ?? new CaptureRegion()
            });
        }
        else
        {
            // Keep legacy fields mirrored for older readers / partial upgrades.
            var abilities = settings.FindRegionByName("Abilities");
            if (abilities is not null)
                settings.AbilitiesRegion = abilities.Region.Clone();
            var combat = settings.FindRegionByName("Combat Text");
            if (combat is not null)
                settings.CombatTextRegion = combat.Region.Clone();
        }

        foreach (var region in settings.CaptureRegions)
        {
            if (string.IsNullOrWhiteSpace(region.Id))
                region.Id = Guid.NewGuid().ToString("N");
            region.Name = string.IsNullOrWhiteSpace(region.Name) ? "New Region" : region.Name.Trim();
            region.Region ??= new CaptureRegion();
        }

        var defaultRegionId = settings.DefaultBuffRegionId() ?? string.Empty;
        foreach (var buff in settings.Buffs)
        {
            buff.CastText ??= string.Empty;
            buff.LandText ??= string.Empty;
            buff.FadeText ??= string.Empty;
            buff.WarnText ??= string.Empty;
            buff.WarnVoiceText ??= string.Empty;
            buff.FadeVoiceText ??= string.Empty;
            buff.AlarmSound = string.IsNullOrWhiteSpace(buff.AlarmSound) ? "Exclamation" : buff.AlarmSound;
            buff.AlertMode = AlertModeOptions.Normalize(buff.AlertMode);
            if (string.IsNullOrWhiteSpace(buff.CastRegionId)) buff.CastRegionId = defaultRegionId;
            if (string.IsNullOrWhiteSpace(buff.LandRegionId)) buff.LandRegionId = defaultRegionId;
            if (string.IsNullOrWhiteSpace(buff.FadeRegionId)) buff.FadeRegionId = defaultRegionId;
            if (string.IsNullOrWhiteSpace(buff.WarnRegionId)) buff.WarnRegionId = defaultRegionId;
        }

        foreach (var timer in settings.UserTimers)
        {
            timer.VoiceText ??= string.Empty;
            timer.AlarmSound = string.IsNullOrWhiteSpace(timer.AlarmSound) ? "Exclamation" : timer.AlarmSound;
            timer.AlertMode = AlertModeOptions.Normalize(timer.AlertMode);
        }

        settings.Version = Math.Max(settings.Version, 3);
    }

    /// <summary>
    /// Single-file publish extracts to %TEMP%\.net\MandM\… — AppContext.BaseDirectory points there.
    /// Prefer the real exe directory, then a stable LocalAppData folder.
    /// </summary>
    private static string ResolveStableSettingsPath()
    {
        var exeDir = GetRealExeDirectory();
        if (!string.IsNullOrEmpty(exeDir) && !IsDotNetExtractTemp(exeDir))
            return Path.Combine(exeDir, "settings.json");

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MandM");
        Directory.CreateDirectory(appData);
        return Path.Combine(appData, "settings.json");
    }

    private static string? GetRealExeDirectory()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(processPath));
                if (!string.IsNullOrEmpty(dir))
                    return dir;
            }
        }
        catch
        {
            // fall through
        }

        try
        {
            var main = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(main))
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(main));
                if (!string.IsNullOrEmpty(dir))
                    return dir;
            }
        }
        catch
        {
            // fall through
        }

        return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsDotNetExtractTemp(string directory)
    {
        var full = Path.GetFullPath(directory);
        var marker = Path.Combine("Temp", ".net", "MandM");
        return full.Contains(marker, StringComparison.OrdinalIgnoreCase)
               || full.Contains($"{Path.DirectorySeparatorChar}.net{Path.DirectorySeparatorChar}MandM",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindBestSettingsFile(string stablePath)
    {
        if (File.Exists(stablePath))
            return stablePath;

        // Legacy / extract-dir copy next to BaseDirectory.
        var baseDir = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var baseSettings = Path.Combine(baseDir, "settings.json");
        if (File.Exists(baseSettings))
            return baseSettings;

        // Any leftover single-file extract folders under %TEMP%\.net\MandM\
        var tempRoot = Path.Combine(
            Path.GetTempPath(), ".net", "MandM");
        if (!Directory.Exists(tempRoot))
            return null;

        try
        {
            return Directory.EnumerateFiles(tempRoot, "settings.json", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .Where(fi => fi.Exists && fi.Length > 0)
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .Select(fi => fi.FullName)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
