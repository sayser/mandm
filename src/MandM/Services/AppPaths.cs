using System.IO;

namespace MandM.Services;

/// <summary>
/// Portable app folder next to MandM.exe (not %TEMP%\.net single-file extract).
/// </summary>
public static class AppPaths
{
    public static string AppDirectory { get; } = ResolveAppDirectory();

    public static string Combine(string fileName) => Path.Combine(AppDirectory, fileName);

    private static string ResolveAppDirectory()
    {
        var baseDirectory = NormalizeDirectory(AppContext.BaseDirectory);
        if (IsSingleFileExtractDirectory(baseDirectory))
        {
            try
            {
                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(processPath))
                {
                    var processDirectory = Path.GetDirectoryName(Path.GetFullPath(processPath));
                    if (!string.IsNullOrWhiteSpace(processDirectory) && Directory.Exists(processDirectory))
                        return processDirectory;
                }
            }
            catch
            {
                // fall through
            }
        }

        return baseDirectory;
    }

    private static bool IsSingleFileExtractDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;
        var extractRoot = NormalizeDirectory(Path.Combine(Path.GetTempPath(), ".net"));
        return directory.StartsWith(extractRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || directory.Equals(extractRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
