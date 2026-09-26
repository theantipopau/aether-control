namespace AetherControl.Core.Updates;

public enum UpdateStatus
{
    UpToDate,
    UpdateAvailable,
    /// <summary>The repository has no published release to compare against — not an error.</summary>
    NoReleasesPublished,
    Failed
}

public sealed record UpdateCheckResult(
    UpdateStatus Status,
    string CurrentVersion,
    string? LatestVersion = null,
    string? ReleaseUrl = null,
    string? Message = null);

/// <summary>
/// Checks for a newer release. Deliberately check-only: it never downloads or installs anything —
/// the user follows the release link and decides. No silent auto-update (ROADMAP Phase 39).
/// </summary>
public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default);
}

public static class ReleaseVersion
{
    /// <summary>Parses "v1.2.3", "1.2.3-preview.1" or "1.2.3+abcdef" down to its numeric core;
    /// null when there's no leading dotted number at all.</summary>
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var core = text.Trim().TrimStart('v', 'V');
        var cut = core.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0)
        {
            core = core[..cut];
        }

        if (!Version.TryParse(core.Contains('.') ? core : core + ".0", out var parsed))
        {
            return null;
        }

        // Normalise missing components to 0 so 1.2 == 1.2.0 == 1.2.0.0.
        return new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0), Math.Max(parsed.Revision, 0));
    }

    /// <summary>True only when <paramref name="candidate"/> parses and is strictly newer. An
    /// unparseable tag is never reported as an update — better to miss one than to nag wrongly.</summary>
    public static bool IsNewer(string? candidate, string? current)
    {
        var candidateVersion = Parse(candidate);
        var currentVersion = Parse(current);
        return candidateVersion is not null && currentVersion is not null && candidateVersion > currentVersion;
    }
}
