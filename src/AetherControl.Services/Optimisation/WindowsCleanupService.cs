namespace AetherControl.Services.Optimisation;

public sealed record CleanupLocation(string Name, string Path, bool SafeToDeleteWhileRunning);

/// <summary>
/// Outcome of a cleanup sweep. <see cref="ItemsSkipped"/> counts reparse points (directory
/// junctions, symbolic links) that were deliberately neither followed nor deleted.
/// </summary>
public sealed record CleanupResult(long BytesFreed, int ItemsSkipped);

/// <summary>
/// Scans and clears well-known temp/cache locations. Only locations that are
/// safe to touch while Windows is running are ever deleted automatically —
/// things like the Windows Update download cache are reported as a
/// recommendation only, since clearing them correctly requires stopping the
/// Windows Update service first and is out of scope for an automatic sweep.
/// </summary>
public sealed class WindowsCleanupService
{
    public IReadOnlyList<CleanupLocation> GetLocations()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var temp = Path.GetTempPath();

        return
        [
            new CleanupLocation("User Temp", temp, true),
            new CleanupLocation("Windows Temp", @"C:\Windows\Temp", true),
            new CleanupLocation("Edge Cache", Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Cache"), true),
            new CleanupLocation("Chrome Cache", Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Cache"), true)
        ];
    }

    public long CalculateReclaimableBytes(IEnumerable<CleanupLocation> locations)
        => locations.Sum(location => DirectorySize(location.Path));

    public CleanupResult Clean(IEnumerable<CleanupLocation> locations)
    {
        long freed = 0;
        int skipped = 0;
        foreach (var location in locations.Where(l => l.SafeToDeleteWhileRunning))
        {
            var result = DeleteContents(location.Path);
            freed += result.BytesFreed;
            skipped += result.ItemsSkipped;
        }

        return new CleanupResult(freed, skipped);
    }

    private static long DirectorySize(string path)
    {
        var (files, _) = CollectLocationFiles(path);

        long total = 0;
        foreach (var file in files)
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // File deleted/renamed by its owning process (browser caches are actively written
                // to while running) between being listed and being stat'd here — skip it.
            }
        }

        return total;
    }

    private static CleanupResult DeleteContents(string path)
    {
        if (!Directory.Exists(path))
        {
            return new CleanupResult(0, 0);
        }

        var (files, skipped) = CollectLocationFiles(path);

        long freed = 0;
        foreach (var file in files)
        {
            try
            {
                var length = new FileInfo(file).Length;
                File.Delete(file);
                freed += length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException)
            {
                // In-use or protected files are skipped rather than aborting the whole sweep.
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(path))
        {
            try
            {
                // Reparse points were already counted as skipped during the walk; never delete them here.
                var attributes = TryGetAttributes(directory);
                if (attributes is null || attributes.Value.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException)
            {
            }
        }

        return new CleanupResult(freed, skipped);
    }

    /// <summary>
    /// Collects the regular files under a cleanup location. If the location root is itself a reparse
    /// point (e.g. a junction redirecting the cache elsewhere), the whole location is skipped and
    /// counted as one item.
    /// </summary>
    private static (List<string> Files, int Skipped) CollectLocationFiles(string path)
    {
        var files = new List<string>();
        if (!Directory.Exists(path))
        {
            return (files, 0);
        }

        var rootAttributes = TryGetAttributes(path);
        if (rootAttributes is null)
        {
            return (files, 0);
        }

        if (rootAttributes.Value.HasFlag(FileAttributes.ReparsePoint))
        {
            return (files, 1);
        }

        int skipped = 0;
        CollectFiles(path, files, ref skipped);
        return (files, skipped);
    }

    /// <summary>
    /// Walks the tree collecting regular files. Reparse points are counted in <paramref name="skipped"/>
    /// and never followed: a junction inside a cache folder could otherwise lead the sweep outside the
    /// location and delete data that is not cache.
    /// </summary>
    private static void CollectFiles(string path, List<string> files, ref int skipped)
    {
        List<string> entries;
        try
        {
            // Materialised up front. This crashed the whole app before (confirmed via a crash dump): an
            // unhandled PathTooLongException from deep inside Edge/Chrome's cache folder structure, which
            // does not derive from IOException. These are locations actively written to by other running
            // processes, so any enumeration failure is an expected outcome of scanning live external state.
            // Losing this one directory must not propagate and abort the sweep.
            entries = Directory.EnumerateFileSystemEntries(path).ToList();
        }
        catch (Exception)
        {
            return;
        }

        foreach (var entry in entries)
        {
            var attributes = TryGetAttributes(entry);
            if (attributes is null)
            {
                // Vanished or became unreadable mid-walk; nothing to count.
                continue;
            }

            if (attributes.Value.HasFlag(FileAttributes.ReparsePoint))
            {
                skipped++;
            }
            else if (attributes.Value.HasFlag(FileAttributes.Directory))
            {
                CollectFiles(entry, files, ref skipped);
            }
            else
            {
                files.Add(entry);
            }
        }
    }

    /// <summary>
    /// File.GetAttributes reads the link's own attributes without following it, which is what lets
    /// the reparse-point check see junctions and symlinks as such.
    /// </summary>
    private static FileAttributes? TryGetAttributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException)
        {
            return null;
        }
    }
}
