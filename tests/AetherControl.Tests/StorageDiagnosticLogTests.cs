using AetherControl.Services.Hardware;

namespace AetherControl.Tests;

public sealed class StorageDiagnosticLogTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"storage-trace-test-{Guid.NewGuid():N}.log");

    public void Dispose()
    {
        StorageDiagnosticLog.LogPathOverride = null;
        try
        {
            File.Delete(_tempFile);
        }
        catch
        {
            // Best-effort test cleanup.
        }
    }

    [Fact]
    public void Write_CreatesFileAtOverridePath()
    {
        StorageDiagnosticLog.LogPathOverride = _tempFile;

        StorageDiagnosticLog.Write("hello");

        Assert.True(File.Exists(_tempFile));
        Assert.Contains("hello", File.ReadAllText(_tempFile));
    }

    [Fact]
    public void Write_TruncatesFileBeyondSizeCap()
    {
        StorageDiagnosticLog.LogPathOverride = _tempFile;

        // Grow past the 256 KB cap, then confirm a further write truncates rather than
        // appending forever — this trace fires every few seconds in the shipped app.
        File.WriteAllText(_tempFile, new string('x', 300 * 1024));
        StorageDiagnosticLog.Write("after-cap");

        Assert.True(new FileInfo(_tempFile).Length < 300 * 1024);
        Assert.Contains("after-cap", File.ReadAllText(_tempFile));
    }
}
