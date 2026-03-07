using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Serilog;

namespace PureType.Services;

public static class UpdateChecker
{
    private static readonly HttpClient Http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "PureType" } },
        Timeout = TimeSpan.FromSeconds(10),
    };

    private const string ReleasesUrl = "https://api.github.com/repos/jadirc/PureType/releases/latest";

    public record ReleaseInfo(string TagName, string HtmlUrl);

    public static async Task<ReleaseInfo?> CheckAsync()
    {
        try
        {
            var response = await Http.GetFromJsonAsync<GitHubRelease>(ReleasesUrl);
            if (response?.TagName == null) return null;

            var current = Assembly.GetExecutingAssembly().GetName().Version;
            if (current == null) return null;

            var tag = response.TagName.TrimStart('v');
            if (!Version.TryParse(tag, out var remote)) return null;

            if (remote > current)
                return new ReleaseInfo(response.TagName, response.HtmlUrl ?? ReleasesUrl);

            return null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Update check failed");
            return null;
        }
    }

    private record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);
}
