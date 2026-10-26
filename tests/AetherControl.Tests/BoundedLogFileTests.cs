using AetherControl.Core.Diagnostics;

namespace AetherControl.Tests;

public sealed class BoundedLogFileTests
{
    [Fact]
    public void Append_PastCap_RotatesToOldAndStartsFresh()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"aether-boundedlog-{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "test.log");
        try
        {
            BoundedLogFile.Append(path, new string('a', 200), maxBytes: 100);
            BoundedLogFile.Append(path, "fresh", maxBytes: 100);

            Assert.Equal("fresh", File.ReadAllText(path));
            Assert.Equal(200, File.ReadAllText(Path.Combine(dir, "test.old.log")).Length);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp dir — best effort */ }
        }
    }
}
