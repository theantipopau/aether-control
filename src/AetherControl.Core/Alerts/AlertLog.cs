namespace AetherControl.Core.Alerts;

/// <summary>One alert fired during this session (a temperature threshold crossing, a test
/// notification). <see cref="Utc"/> is when it fired; <see cref="Title"/> is the short headline
/// ("CPU running hot"); <see cref="Message"/> is the full sentence the toast showed.</summary>
public sealed record AlertEntry(DateTimeOffset Utc, string Title, string Message);

/// <summary>
/// Session-only, bounded log of alerts the app has fired. Written from the hardware monitor's
/// background thread (temperature trips) and the UI thread (test alerts), read from the UI thread
/// once per dashboard poll — hence the lock around a simple queue rather than a concurrent
/// collection: writes are rare (an alert crossing is not a per-second event), reads are a single
/// peek.
/// <para>
/// Pure and UI-free specifically so the age-gating rule ("only surface alerts while they're still
/// recent — a CPU that ran hot two hours ago must not read as a current warning") is unit-tested.
/// </para>
/// </summary>
public sealed class AlertLog
{
    private readonly object _gate = new();
    private readonly Queue<AlertEntry> _entries = new();
    private readonly int _capacity;

    public AlertLog(int capacity = 20)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public void Add(DateTimeOffset utc, string title, string message)
    {
        lock (_gate)
        {
            _entries.Enqueue(new AlertEntry(utc, title, message));
            while (_entries.Count > _capacity)
            {
                _entries.Dequeue();
            }
        }
    }

    /// <summary>Newest first. Empty when nothing has fired this session.</summary>
    public IReadOnlyList<AlertEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.Reverse().ToList();
        }
    }

    /// <summary>Alerts that fired within <paramref name="maxAge"/> of <paramref name="now"/>,
    /// newest first — empty once everything has aged out (an hour-old crossing must not read as a
    /// current warning).</summary>
    public IReadOnlyList<AlertEntry> Recent(TimeSpan maxAge, DateTimeOffset now)
    {
        lock (_gate)
        {
            var cutoff = now - maxAge;
            return _entries.Where(e => e.Utc >= cutoff).Reverse().ToList();
        }
    }
}
