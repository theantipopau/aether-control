using AetherControl.Services.Optimisation;
using System.Diagnostics;

namespace AetherControl.Tests;

/// <summary>
/// Pins that the cleanup sweep never follows reparse points (directory junctions) out of a cache
/// location, and reports how many it skipped. Junctions are created with mklink /J, which needs no
/// elevation, so these run on CI and locally.
/// </summary>
public sealed class WindowsCleanupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aethercontrol-cleanup-{Guid.NewGuid():N}");
    private readonly string _location;
    private readonly string _outside;
    private readonly string _junction;

    public WindowsCleanupServiceTests()
    {
        _location = Path.Combine(_root, "cache");
        _outside = Path.Combine(_root, "outside");
        _junction = Path.Combine(_location, "linked");
        Directory.CreateDirectory(_location);
        Directory.CreateDirectory(_outside);

        // Data that lives outside the cache location must survive a sweep that reaches the junction.
        File.WriteAllBytes(Path.Combine(_outside, "keep.dat"), new byte[4096]);

        var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{_junction}\" \"{_outside}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        mklink.WaitForExit();
        Assert.Equal(0, mklink.ExitCode);
    }

    [Fact]
    public void Clean_DoesNotFollowJunction_AndReportsSkippedCount()
    {
        File.WriteAllBytes(Path.Combine(_location, "cache.bin"), new byte[1000]);
        var service = new WindowsCleanupService();

        var result = service.Clean([new CleanupLocation("Test", _location, true)]);

        Assert.True(File.Exists(Path.Combine(_outside, "keep.dat")), "file behind the junction was deleted");
        Assert.False(File.Exists(Path.Combine(_location, "cache.bin")));
        Assert.Equal(1000, result.BytesFreed);
        Assert.Equal(1, result.ItemsSkipped);
    }

    [Fact]
    public void CalculateReclaimableBytes_ExcludesJunctionTarget()
    {
        File.WriteAllBytes(Path.Combine(_location, "cache.bin"), new byte[1000]);
        var service = new WindowsCleanupService();

        var bytes = service.CalculateReclaimableBytes([new CleanupLocation("Test", _location, true)]);

        Assert.Equal(1000, bytes);
    }

    [Fact]
    public void Clean_LocationRootThatIsAJunction_IsSkippedWholesale()
    {
        // The location itself is the junction: nothing under it may be touched.
        var service = new WindowsCleanupService();

        var result = service.Clean([new CleanupLocation("Linked root", _junction, true)]);

        Assert.True(File.Exists(Path.Combine(_outside, "keep.dat")));
        Assert.Equal(0, result.BytesFreed);
        Assert.Equal(1, result.ItemsSkipped);
    }

    public void Dispose()
    {
        // Remove the junction link itself (non-recursive) before the recursive delete, so the
        // recursive walk cannot descend into the target.
        if (Directory.Exists(_junction))
        {
            Directory.Delete(_junction, recursive: false);
        }

        try { Directory.Delete(_root, recursive: true); } catch { /* temp dir — best effort */ }
    }
}
