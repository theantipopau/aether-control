namespace AetherControl.Services.Infrastructure;

/// <summary>
/// Best-effort single-instance enforcement for the app process. A second Aether Control process
/// opening its own LibreHardwareMonitor <c>Computer</c> session would contend with the first for
/// the same Super I/O chip — the project's own live evidence says two concurrent readers/writers
/// make BOTH sides read back garbage persistently — so the second launch must exit (after
/// foregrounding the existing window, handled App-side) instead of starting a second session.
/// <para>
/// <c>Local\</c>, not <c>Global\</c>: per-session is all that's needed (two sessions on one
/// desktop contend; two users' sessions don't share the same interactive hardware view), and
/// <c>Global\</c> needs SeCreateGlobalPrivilege. An abandoned mutex (previous process died
/// holding it) is treated as acquirable — the OS releases the handle on death, so a crash can
/// never wedge the app into refusing to start ever again.
/// </para>
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private bool _disposed;

    public bool IsFirstInstance { get; }

    public SingleInstanceGuard(string name)
    {
        _mutex = new Mutex(false, name);
        try
        {
            IsFirstInstance = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // Previous owner died while holding it — ownership transfers to us.
            IsFirstInstance = true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (IsFirstInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                // Best-effort — the OS releases it on process exit regardless.
            }
        }

        _mutex.Dispose();
    }
}
