using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using AetherControl.Core.Updates;

namespace AetherControl.Services.Updates;

/// <summary>
/// Asks GitHub's public releases API for the latest published release of this repository. One
/// anonymous GET, only when the user presses "Check for updates" or has opted in to a check at
/// startup — no identifiers beyond the standard User-Agent GitHub requires, nothing downloaded.
/// </summary>
public sealed class GitHubUpdateCheckService(HttpClient httpClient) : IUpdateCheckService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/theantipopau/aether-control/releases/latest";

    public static string CurrentVersion { get; } = ReadCurrentVersion();

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AetherControl", CurrentVersion));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // GitHub returns 404 from /releases/latest when nothing has been published yet.
                return new UpdateCheckResult(UpdateStatus.NoReleasesPublished, CurrentVersion,
                    Message: "No releases have been published yet.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(UpdateStatus.Failed, CurrentVersion,
                    Message: $"GitHub responded {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var tag = json.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            var url = json.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() : null;

            return ReleaseVersion.IsNewer(tag, CurrentVersion)
                ? new UpdateCheckResult(UpdateStatus.UpdateAvailable, CurrentVersion, tag, url, $"Version {tag} is available.")
                : new UpdateCheckResult(UpdateStatus.UpToDate, CurrentVersion, tag, url, "You're on the latest release.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Offline, DNS, proxy, rate limit, malformed JSON — reported, never thrown at the UI.
            return new UpdateCheckResult(UpdateStatus.Failed, CurrentVersion, Message: $"Couldn't check: {ex.Message}");
        }
    }

    private static string ReadCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(GitHubUpdateCheckService).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return informational ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
