namespace AetherControl.Services.Optimisation;

public sealed record CleanupLocation(string Name, string Path, bool SafeToDeleteWhileRunning);

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

    public long Clean(IEnumerable<CleanupLocation> locations)
    {
        long freed = 0;
        foreach (var location in locations.Where(l => l.SafeToDeleteWhileRunning))
        {
            freed += DeleteContents(location.Path);
        }

        return freed;
    }

    private static long DirectorySize(string path)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        long total = 0;
        foreach (var file in EnumerateFilesSafely(path))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch (IOException)
            {
            }
        }

        return total;
    }

    private static long DeleteContents(string path)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        long freed = 0;
        foreach (var file in EnumerateFilesSafely(path))
        {
            try
            {
                var length = new FileInfo(file).Length;
                File.Delete(file);
                freed += length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In-use or protected files are skipped rather than aborting the whole sweep.
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(path))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return freed;
    }

    private static IEnumerable<string> EnumerateFilesSafely(string path)
    {
        List<string> files;
        List<string> subdirectories;
        try
        {
            files = Directory.EnumerateFiles(path).ToList();
            subdirectories = Directory.EnumerateDirectories(path).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files)
        {
            yield return file;
        }

        foreach (var subdirectory in subdirectories)
        {
            foreach (var file in EnumerateFilesSafely(subdirectory))
            {
                yield return file;
            }
        }
    }
}
