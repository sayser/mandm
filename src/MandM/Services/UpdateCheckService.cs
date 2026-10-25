using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace MandM.Services;

public sealed class UpdateCheckService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MandM", GetLocalVersion()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static string GetLocalVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    public async Task<UpdateCheckResult> CheckAsync(string repo, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://api.github.com/repos/{repo}/releases/latest";
            using var response = await Http.GetAsync(url, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(
                    false,
                    GetLocalVersion(),
                    null,
                    null,
                    "No GitHub releases yet — you're on the latest published build (or none exist).");
            }

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
            var htmlUrl = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() : null;
            var remote = NormalizeVersion(tag);
            var local = NormalizeVersion(GetLocalVersion());
            var newer = IsNewer(remote, local);

            return new UpdateCheckResult(
                newer,
                GetLocalVersion(),
                tag,
                htmlUrl,
                newer
                    ? $"Update available: {tag}"
                    : $"Up to date ({GetLocalVersion()}). Latest release: {tag ?? "(none)"}.");
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(
                false,
                GetLocalVersion(),
                null,
                null,
                $"Update check failed: {ex.Message}");
        }
    }

    private static string NormalizeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "0.0.0";
        var v = value.Trim();
        if (v.StartsWith('v') || v.StartsWith('V'))
            v = v[1..];
        var parts = v.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var nums = new int[3];
        for (var i = 0; i < Math.Min(3, parts.Length); i++)
        {
            var digits = new string(parts[i].TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var n))
                nums[i] = n;
        }

        return $"{nums[0]}.{nums[1]}.{nums[2]}";
    }

    private static bool IsNewer(string remote, string local)
    {
        var r = remote.Split('.').Select(int.Parse).ToArray();
        var l = local.Split('.').Select(int.Parse).ToArray();
        for (var i = 0; i < 3; i++)
        {
            if (r[i] > l[i]) return true;
            if (r[i] < l[i]) return false;
        }

        return false;
    }
}

public readonly record struct UpdateCheckResult(
    bool UpdateAvailable,
    string LocalVersion,
    string? RemoteTag,
    string? ReleaseUrl,
    string Message);
