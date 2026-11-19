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
            settings.AbilitiesRegion ??= new CaptureRegion();
            settings.CombatTextRegion ??= new CaptureRegion();
            settings.MouseHighlight ??= new MouseHighlightSettings();

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
        var dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
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
