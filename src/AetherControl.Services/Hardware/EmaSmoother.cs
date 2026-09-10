namespace AetherControl.Services.Hardware;

/// <summary>
/// Exponential moving average for display metrics that legitimately change reading-to-reading.
/// Confirmed via a real poll-by-poll trace (see PollDiagnosticsLog, now removed): on Matt's
/// machine CPU clock genuinely cycles through a repeating ~4-second pattern at idle (418, 393,
/// 693, 572, 809, 648, 858... then the same sequence again) — Windows/Intel core parking rotating
/// which cores are active, not a mapping bug. The same trace showed storage free space was
/// already perfectly stable poll-to-poll, confirming that fix held — so smoothing is applied to
/// clock speed only; applying it to free space would risk masking a real future regression there.
/// alpha=0.15 trades slower reaction to genuine load spikes (~2s to mostly settle) for meaningfully
/// damping a swing this large — alpha=0.35 was tried first and still showed the full up-down cycle.
/// </summary>
internal sealed class EmaSmoother
{
    private readonly double _alpha;
    private double? _value;

    public EmaSmoother(double alpha = 0.15)
    {
        _alpha = alpha;
    }

    public double Update(double sample)
    {
        _value = _value is null ? sample : _alpha * sample + (1 - _alpha) * _value.Value;
        return _value.Value;
    }
}
