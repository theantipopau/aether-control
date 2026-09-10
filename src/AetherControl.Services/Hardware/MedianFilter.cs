namespace AetherControl.Services.Hardware;

/// <summary>
/// Rejects single-sample outliers before they ever reach <see cref="EmaSmoother"/>. Confirmed
/// pattern (used the same way by HWiNFO's "sensor smoothing" and RTSS): CPU/GPU load readings are
/// occasionally a genuine one-off spike — one bad instantaneous sample, not a real sustained level
/// change — and EMA alone still lets a chunk of that spike through and takes a couple of seconds to
/// forget it. A median of the last N samples throws the spike out entirely (it's the max or min of
/// the window, never the middle value) while a real sustained change still reaches the median within
/// N/2 samples, so it doesn't add meaningfully more lag than EMA already does on its own.
/// </summary>
internal sealed class MedianFilter
{
    private readonly double[] _window;
    private int _count;
    private int _next;

    public MedianFilter(int size = 3)
    {
        if (size < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        _window = new double[size];
    }

    public double Update(double sample)
    {
        _window[_next] = sample;
        _next = (_next + 1) % _window.Length;
        _count = Math.Min(_count + 1, _window.Length);

        var sorted = _window[.._count];
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }
}
